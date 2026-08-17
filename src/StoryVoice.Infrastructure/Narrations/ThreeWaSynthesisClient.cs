using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// HTTP implementation of <see cref="IThreeWaSynthesisClient"/>. Parses responses with
/// <see cref="JsonDocument"/> rather than strict record binding — the public docs don't pin down
/// every field name for the status/result/artifact steps, so this reads defensive documented
/// variants while keeping every authenticated follow-up on the configured HTTPS origin.
/// </summary>
public sealed class ThreeWaSynthesisClient(HttpClient httpClient, IOptions<ThreeWaAiHubOptions> options)
    : IThreeWaSynthesisClient
{
    internal const string CanonicalApiPath = "api.php?mode=voice_generate";
    private const string CanonicalModel = "voxcpm2";
    private const string CanonicalSeedPolicy = "fixed";
    private const int CanonicalPriority = 0;

    public async Task<ThreeWaSynthesisTaskHandle> SubmitAsync(
        ThreeWaSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = Require(request.Text, "3wa synthesis text is required.");
        var mode = Require(request.Mode, "3wa synthesis mode is required.");
        if (string.Equals(mode, "design", StringComparison.Ordinal))
        {
            throw ContractViolation(ThreeWaSynthesisCapabilities.DesignVoiceUnavailableMessage);
        }

        if (!string.Equals(mode, "ultimate_clone", StringComparison.Ordinal))
        {
            throw ContractViolation("3wa synthesis mode is not supported.");
        }

        var payload = new Dictionary<string, object>
        {
            ["operation"] = "synthesize",
            ["mode"] = mode,
            ["text"] = text,
            ["seed"] = request.Seed,
            ["seed_policy"] = CanonicalSeedPolicy,
            ["model"] = CanonicalModel,
            ["priority"] = CanonicalPriority,
            ["voice_profile_task_id"] = Require(
                request.VoiceProfileTaskId,
                "3wa clone synthesis requires voice_profile_task_id."),
        };

        using var content = JsonContent.Create(payload);
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            CanonicalApiPath,
            content,
            cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.True)
        {
            throw ContractViolation("3wa synthesize response did not report success.");
        }

        var taskId = GetIdentifier(root, "task_id")
            ?? throw ContractViolation("3wa synthesize response did not include task_id.");
        var statusUrl = GetString(root, "status_url")
            ?? throw ContractViolation("3wa synthesize response did not include status_url.");
        var resultUrl = GetString(root, "result_url")
            ?? throw ContractViolation("3wa synthesize response did not include result_url.");
        var safeStatusUrl = ResolveSameOriginHttpsUri(statusUrl, "status_url");
        var safeResultUrl = ResolveSameOriginHttpsUri(resultUrl, "result_url");
        var artifactUrlTemplate = GetString(root, "artifact_url_template");
        if (artifactUrlTemplate is not null)
        {
            _ = ResolveArtifactUri(artifactUrlTemplate, "0");
        }

        return new ThreeWaSynthesisTaskHandle(
            taskId,
            safeStatusUrl.AbsoluteUri,
            safeResultUrl.AbsoluteUri,
            artifactUrlTemplate);
    }

    public async Task<string> GetTaskStatusAsync(string statusUrl, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            statusUrl,
            content: null,
            cancellationToken);
        var root = document.RootElement;
        var status = GetString(root, "status")
            ?? GetString(root, "task_status")
            ?? GetNestedString(root, "result", "status")
            ?? GetNestedString(root, "result", "task_status");
        return string.IsNullOrWhiteSpace(status)
            ? "unknown"
            : status.Trim().ToLowerInvariant();
    }

    public async Task<IReadOnlyList<ThreeWaSynthesisArtifact>> GetResultArtifactsAsync(
        string resultUrl,
        CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            resultUrl,
            content: null,
            cancellationToken);
        var root = document.RootElement;
        var artifactsElement = root.TryGetProperty("artifacts", out var direct)
            ? direct
            : root.TryGetProperty("result", out var resultElement)
                && resultElement.TryGetProperty("artifacts", out var nested)
                    ? nested
                    : default;
        if (artifactsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var artifacts = new List<ThreeWaSynthesisArtifact>();
        foreach (var element in artifactsElement.EnumerateArray())
        {
            var id = GetIdentifier(element, "id") ?? GetIdentifier(element, "artifact_id");
            var mimeType = GetString(element, "mime_type") ?? GetString(element, "content_type");
            if (id is null || !IsAudioMimeType(mimeType))
            {
                continue;
            }

            artifacts.Add(new ThreeWaSynthesisArtifact(id, mimeType));
        }

        return artifacts;
    }

    public async Task DownloadArtifactAsync(
        string artifactUrlTemplate,
        string artifactId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The artifact destination must be writable.", nameof(destination));
        }

        var uri = ResolveArtifactUri(artifactUrlTemplate, artifactId);
        using var request = BuildRequest(HttpMethod.Get, uri, content: null);
        using var response = await SendHttpAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw SafeHttpFailure(response.StatusCode);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!IsAudioMimeType(mediaType))
        {
            throw ContractViolation("3wa artifact response was not audio.");
        }

        try
        {
            await CopyBoundedAsync(
                response.Content,
                destination,
                options.Value.MaximumAudioResponseBytes,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ThreeWaAiHubException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ThreeWaAiHubException("3wa audio response could not be read.");
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string urlOrRelativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var uri = ResolveSameOriginHttpsUri(urlOrRelativePath, "response URL");
        using var request = BuildRequest(method, uri, content);
        using var response = await SendHttpAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw SafeHttpFailure(response.StatusCode);
        }

        byte[] bytes;
        try
        {
            bytes = await ReadBoundedAsync(
                response.Content,
                options.Value.MaximumJsonResponseBytes,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ThreeWaAiHubException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API response could not be read.");
        }
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            throw ContractViolation("3wa Cluster API returned invalid JSON.");
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, HttpContent? content)
    {
        var apiToken = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ThreeWaAiHubException("尚未設定 3wa Cluster API token（ThreeWaAiHub__ApiToken）。");
        }

        var request = new HttpRequestMessage(method, uri) { Content = content };
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
        }
        catch (FormatException)
        {
            request.Dispose();
            throw new ThreeWaAiHubException("3wa Cluster API token format is invalid.");
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendHttpAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API request failed.");
        }
        catch (IOException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API response could not be read.");
        }
    }

    private Uri ResolveArtifactUri(string template, string artifactId)
    {
        var normalizedTemplate = Require(template, "3wa artifact URL template is required.");
        var normalizedId = Require(artifactId, "3wa artifact id is required.");
        var escapedId = Uri.EscapeDataString(normalizedId);
        string expanded;
        if (normalizedTemplate.Contains("{artifact_id}", StringComparison.Ordinal))
        {
            expanded = normalizedTemplate.Replace("{artifact_id}", escapedId, StringComparison.Ordinal);
        }
        else if (normalizedTemplate.Contains("{id}", StringComparison.Ordinal))
        {
            expanded = normalizedTemplate.Replace("{id}", escapedId, StringComparison.Ordinal);
        }
        else
        {
            throw ContractViolation("3wa artifact URL template did not contain an id placeholder.");
        }

        return ResolveSameOriginHttpsUri(expanded, "artifact_url_template");
    }

    private Uri ResolveSameOriginHttpsUri(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ContractViolation($"3wa {fieldName} was missing.");
        }

        var baseUri = httpClient.BaseAddress;
        if (baseUri is null
            || !baseUri.IsAbsoluteUri
            || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment)
            || !baseUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            throw ContractViolation("3wa base URL must be an HTTPS directory URL.");
        }

        if (!Uri.TryCreate(baseUri, value, out var resolved)
            || !string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(resolved.UserInfo)
            || !string.IsNullOrEmpty(resolved.Fragment)
            || !string.Equals(resolved.IdnHost, baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || resolved.Port != baseUri.Port
            || !IsPathWithinBase(resolved, baseUri))
        {
            throw ContractViolation($"3wa {fieldName} must remain on the configured HTTPS origin.");
        }

        return resolved;
    }

    private static bool IsPathWithinBase(Uri candidate, Uri baseUri)
    {
        if (!TryFullyUnescapePath(candidate.AbsolutePath, out var candidatePath)
            || !TryFullyUnescapePath(baseUri.AbsolutePath, out var basePath))
        {
            return false;
        }

        candidatePath = candidatePath.Replace('\\', '/');
        basePath = basePath.Replace('\\', '/');
        if (!candidatePath.StartsWith(basePath, StringComparison.Ordinal))
        {
            return false;
        }

        var baseDepth = basePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        var depth = 0;
        foreach (var segment in candidatePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (depth <= baseDepth)
                {
                    return false;
                }

                depth--;
                continue;
            }

            depth++;
        }

        return depth >= baseDepth;
    }

    private static bool TryFullyUnescapePath(string path, out string unescaped)
    {
        const int MaximumPasses = 8;
        unescaped = path;
        try
        {
            for (var pass = 0; pass < MaximumPasses; pass++)
            {
                var next = Uri.UnescapeDataString(unescaped);
                if (string.Equals(next, unescaped, StringComparison.Ordinal))
                {
                    return true;
                }

                unescaped = next;
            }

            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes < 1)
        {
            throw ContractViolation("3wa JSON response size limit is invalid.");
        }

        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            throw ContractViolation("3wa JSON response exceeded the configured size limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw ContractViolation("3wa JSON response exceeded the configured size limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static async Task CopyBoundedAsync(
        HttpContent content,
        Stream destination,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes < 1)
        {
            throw ContractViolation("3wa audio response size limit is invalid.");
        }

        if (content.Headers.ContentLength is long contentLength
            && (contentLength < 1 || contentLength > maximumBytes))
        {
            throw ContractViolation("3wa audio response had an invalid size.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw ContractViolation("3wa audio response exceeded the configured size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total < 1)
        {
            throw ContractViolation("3wa audio response was empty.");
        }
    }

    private static string? GetIdentifier(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => NormalizeOptional(property.GetString()),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static string? GetNestedString(JsonElement element, string containerName, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(containerName, out var container)
            ? GetString(container, propertyName)
            : null;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? NormalizeOptional(property.GetString())
                : null;

    private static bool IsAudioMimeType(string? mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType)
        && mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);

    private static string Require(string? value, string message) =>
        NormalizeOptional(value) ?? throw ContractViolation(message);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ThreeWaAiHubException SafeHttpFailure(System.Net.HttpStatusCode statusCode) =>
        new($"3wa Cluster API request failed with HTTP {(int)statusCode}.");

    private static ThreeWaAiHubException ContractViolation(string message) => new(message);
}
