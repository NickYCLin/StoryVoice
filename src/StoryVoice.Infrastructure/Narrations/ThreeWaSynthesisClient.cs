using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// HTTP implementation of <see cref="IThreeWaSynthesisClient"/>. Parses responses with
/// <see cref="JsonDocument"/> rather than strict record binding — the public docs don't pin down
/// every field name for the status/result/artifact steps, so this reads defensively (falling back
/// to null/"unknown" for anything unrecognized) instead of throwing on an unexpected shape.
/// </summary>
public sealed class ThreeWaSynthesisClient(HttpClient httpClient, IOptions<ThreeWaAiHubOptions> options)
    : IThreeWaSynthesisClient
{
    public async Task<ThreeWaSynthesisTaskHandle> SubmitAsync(
        ThreeWaSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new
        {
            operation = "synthesize",
            mode = request.Mode,
            text = request.Text,
            voice_profile_task_id = request.VoiceProfileTaskId,
            voice_prompt = request.VoicePromptText,
        });

        using var document = await SendAsync(HttpMethod.Post, "cluster_api.php?mode=voice_generate", content, cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.True)
        {
            throw new ThreeWaAiHubException("聲線合成 synthesize 提交失敗。");
        }

        var taskId = GetString(root, "task_id")
            ?? throw new ThreeWaAiHubException("聲線合成 synthesize 未回傳 task_id。");
        var statusUrl = GetString(root, "status_url")
            ?? throw new ThreeWaAiHubException("聲線合成 synthesize 未回傳 status_url。");
        var resultUrl = GetString(root, "result_url")
            ?? throw new ThreeWaAiHubException("聲線合成 synthesize 未回傳 result_url。");

        return new ThreeWaSynthesisTaskHandle(
            taskId,
            statusUrl,
            resultUrl,
            GetString(root, "artifact_url_template"),
            GetString(root, "ack_url_template"));
    }

    public async Task<string> GetTaskStatusAsync(string statusUrl, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, statusUrl, content: null, cancellationToken);
        return GetString(document.RootElement, "status") ?? "unknown";
    }

    public async Task<IReadOnlyList<ThreeWaSynthesisArtifact>> GetResultArtifactsAsync(
        string resultUrl,
        CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, resultUrl, content: null, cancellationToken);
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
            var id = GetString(element, "id");
            if (id is null)
            {
                continue;
            }

            artifacts.Add(new ThreeWaSynthesisArtifact(id, GetString(element, "mime_type")));
        }

        return artifacts;
    }

    public async Task DownloadArtifactAsync(
        string artifactUrlTemplate,
        string artifactId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var url = ExpandTemplate(artifactUrlTemplate, artifactId);
        var request = BuildRequest(HttpMethod.Get, url, content: null);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ThreeWaAiHubException($"聲線合成音檔下載失敗（{(int)response.StatusCode}）。");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(destination, cancellationToken);
    }

    public async Task AcknowledgeArtifactAsync(string? ackUrlTemplate, string artifactId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ackUrlTemplate))
        {
            return;
        }

        var url = ExpandTemplate(ackUrlTemplate, artifactId);
        var request = BuildRequest(HttpMethod.Post, url, content: null);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        // Acknowledgement failures shouldn't fail an otherwise-successful synthesis — the artifact
        // has already been downloaded to local storage by this point, which is the canonical copy.
    }

    private static string ExpandTemplate(string template, string artifactId) =>
        template.Replace("{artifact_id}", artifactId).Replace("{id}", artifactId);

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string urlOrRelativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(method, urlOrRelativePath, content);
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ThreeWaAiHubException($"3wa Cluster API 回傳 {(int)response.StatusCode}。");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string urlOrRelativePath, HttpContent? content)
    {
        var apiToken = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new ThreeWaAiHubException("尚未設定 3wa Cluster API token（ThreeWaAiHub__ApiToken）。");
        }

        var uri = Uri.IsWellFormedUriString(urlOrRelativePath, UriKind.Absolute)
            ? new Uri(urlOrRelativePath)
            : new Uri(httpClient.BaseAddress!, urlOrRelativePath);
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return request;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
}
