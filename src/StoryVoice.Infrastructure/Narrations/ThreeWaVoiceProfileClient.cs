using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Thin wrapper over the 3wa Cluster API's <c>voice_generate</c> mode profile lifecycle
/// (<c>cluster_api.php?mode=voice_generate</c> with an <c>operation</c> field selecting
/// profile_prepare/profile_status/profile_confirm). Never called directly from the frontend — the
/// Bearer token only ever lives in this process's configuration.
/// </summary>
public sealed class ThreeWaVoiceProfileClient(HttpClient httpClient, IOptions<ThreeWaAiHubOptions> options)
    : IThreeWaVoiceProfileClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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

        var response = await SendAsync(HttpMethod.Post, "cluster_api.php?mode=voice_generate", content, cancellationToken);
        var body = await ReadAsync<PrepareResponseBody>(response, cancellationToken);
        if (!body.Ok || string.IsNullOrWhiteSpace(body.TaskId))
        {
            throw new ThreeWaAiHubException("聲線 profile_prepare 未回傳有效的 task_id。");
        }

        return new VoiceProfilePrepareResult(body.TaskId, body.PromptText);
    }

    public async Task<VoiceProfileStatusResult> GetStatusAsync(string taskId, CancellationToken cancellationToken)
    {
        var path = $"cluster_api.php?mode=voice_generate&operation=profile_status&voice_profile_task_id={Uri.EscapeDataString(taskId)}";
        var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        var body = await ReadAsync<StatusResponseBody>(response, cancellationToken);
        if (!body.Ok)
        {
            throw new ThreeWaAiHubException("聲線 profile_status 查詢失敗。");
        }

        return new VoiceProfileStatusResult(
            body.TaskStatus ?? "unknown",
            body.TranscriptConfirmed,
            body.PromptText,
            string.Equals(body.TranscriptionStatus, "failed", StringComparison.OrdinalIgnoreCase));
    }

    public async Task ConfirmAsync(string taskId, string transcript, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(
            new { operation = "profile_confirm", voice_profile_task_id = taskId, prompt_text = transcript },
            options: SerializerOptions);
        var response = await SendAsync(HttpMethod.Post, "cluster_api.php?mode=voice_generate", content, cancellationToken);
        var body = await ReadAsync<ConfirmResponseBody>(response, cancellationToken);
        if (!body.Ok)
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

        var request = new HttpRequestMessage(method, relativePath) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ThreeWaAiHubException(
                $"3wa Cluster API 回傳 {(int)response.StatusCode}：{Truncate(errorBody, 500)}");
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return body ?? throw new ThreeWaAiHubException("3wa Cluster API 回應內容無法解析。");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed record PrepareResponseBody(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("task_id")] string? TaskId,
        [property: JsonPropertyName("prompt_text")] string? PromptText);

    private sealed record StatusResponseBody(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("task_status")] string? TaskStatus,
        [property: JsonPropertyName("transcript_confirmed")] bool TranscriptConfirmed,
        [property: JsonPropertyName("prompt_text")] string? PromptText,
        [property: JsonPropertyName("transcription_status")] string? TranscriptionStatus);

    private sealed record ConfirmResponseBody([property: JsonPropertyName("ok")] bool Ok);
}

public sealed class ThreeWaAiHubException(string message) : InvalidOperationException(message);
