using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Thin wrapper over the 3wa Cluster API's <c>voice_generate</c> mode profile lifecycle
/// (<c>api.php?mode=voice_generate</c> with an <c>operation</c> field selecting
/// profile_prepare/profile_status/profile_confirm). Never called directly from the frontend — the
/// Bearer token only ever lives in this process's configuration.
/// </summary>
public sealed class ThreeWaVoiceProfileClient(HttpClient httpClient, IOptions<ThreeWaAiHubOptions> options)
    : IThreeWaVoiceProfileClient
{
    internal const string CanonicalVoiceGeneratePath = "api.php?mode=voice_generate";

    public async Task<VoiceProfilePrepareResult> PrepareAsync(
        Stream referenceWav,
        string fileName,
        string profileName,
        string consentType,
        string? promptText,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("profile_prepare"), "operation" },
            { new StringContent(profileName), "profile_name" },
            { new StringContent(consentType), "consent_type" },
        };
        if (!string.IsNullOrWhiteSpace(promptText))
        {
            content.Add(new StringContent(promptText), "prompt_text");
        }

        var audioContent = new StreamContent(referenceWav);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "reference_wav", fileName);

        using var response = await SendAsync(
            HttpMethod.Post,
            CanonicalVoiceGeneratePath,
            content,
            cancellationToken);
        using var body = await ReadJsonAsync(response, cancellationToken);
        var root = body.RootElement;
        var taskId = GetIdentifier(root, "task_id");
        if (!IsOk(root) || taskId is null)
        {
            throw new ThreeWaAiHubException("聲線 profile_prepare 未回傳有效的 task_id。");
        }

        return new VoiceProfilePrepareResult(taskId, GetString(root, "prompt_text"));
    }

    public async Task<VoiceProfileStatusResult> GetStatusAsync(string taskId, CancellationToken cancellationToken)
    {
        var normalizedTaskId = Require(taskId, "3wa voice profile task id is required.");
        var path = $"{CanonicalVoiceGeneratePath}&operation=profile_status&voice_profile_task_id={Uri.EscapeDataString(normalizedTaskId)}";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        using var body = await ReadJsonAsync(response, cancellationToken);
        var root = body.RootElement;
        if (!IsOk(root))
        {
            throw new ThreeWaAiHubException("聲線 profile_status 查詢失敗。");
        }

        return new VoiceProfileStatusResult(
            GetString(root, "task_status") ?? "unknown",
            GetBoolean(root, "transcript_confirmed"),
            GetString(root, "prompt_text"),
            string.Equals(GetString(root, "transcription_status"), "failed", StringComparison.OrdinalIgnoreCase));
    }

    public async Task ConfirmAsync(string taskId, string transcript, CancellationToken cancellationToken)
    {
        var normalizedTaskId = Require(taskId, "3wa voice profile task id is required.");
        var normalizedTranscript = Require(transcript, "3wa voice profile transcript is required.");
        using var content = System.Net.Http.Json.JsonContent.Create(new
        {
            operation = "profile_confirm",
            voice_profile_task_id = normalizedTaskId,
            prompt_text = normalizedTranscript,
        });
        using var response = await SendAsync(
            HttpMethod.Post,
            CanonicalVoiceGeneratePath,
            content,
            cancellationToken);
        using var body = await ReadJsonAsync(response, cancellationToken);
        if (!IsOk(body.RootElement))
        {
            throw new ThreeWaAiHubException("聲線 profile_confirm 確認失敗。");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var apiToken = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ThreeWaAiHubException("尚未設定 3wa Cluster API token（ThreeWaAiHub__ApiToken）。");
        }

        using var request = new HttpRequestMessage(method, relativePath) { Content = content };
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
        }
        catch (FormatException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API token format is invalid.");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
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

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new ThreeWaAiHubException($"3wa Cluster API request failed with HTTP {(int)statusCode}.");
        }

        return response;
    }

    private async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var maximumBytes = options.Value.MaximumJsonResponseBytes;
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            throw new ThreeWaAiHubException("3wa Cluster API 回應超過允許大小。");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
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
                    throw new ThreeWaAiHubException("3wa Cluster API 回應超過允許大小。");
                }

                output.Write(buffer, 0, read);
            }

            return JsonDocument.Parse(output.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ThreeWaAiHubException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API 回應內容無法解析。");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new ThreeWaAiHubException("3wa Cluster API response could not be read.");
        }
    }

    private static bool IsOk(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("ok", out var value)
        && value.ValueKind == JsonValueKind.True;

    private static bool GetBoolean(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string? GetIdentifier(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => Normalize(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? Normalize(value.GetString())
            : null;

    private static string Require(string? value, string message) =>
        Normalize(value) ?? throw new ThreeWaAiHubException(message);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ThreeWaAiHubException(string message) : InvalidOperationException(message);
