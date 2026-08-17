using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class ThreeWaSynthesisClientTests
{
    private const string BaseUrl = "https://3wa.tw/3waAIHub/";
    private const string ApiToken = "test-three-wa-token";

    [Fact]
    public async Task SubmitAsync_posts_the_canonical_design_contract_and_accepts_a_numeric_task_id()
    {
        var handler = new RecordingHandler(() => JsonResponse(
            """
            {
              "ok": true,
              "task_id": 123,
              "status_url": "/3waAIHub/tasks/123/status",
              "result_url": "https://3wa.tw/3waAIHub/tasks/123/result",
              "artifact_url_template": "/3waAIHub/artifacts/{artifact_id}"
            }
            """));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var handle = await client.SubmitAsync(
            new ThreeWaSynthesisRequest(
                "測試。",
                "design",
                VoiceProfileTaskId: null,
                VoicePromptText: "台灣華語，中性自然。",
                Seed: 42),
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://3wa.tw/3waAIHub/api.php?mode=voice_generate", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(ApiToken, request.AuthorizationParameter);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("synthesize", root.GetProperty("operation").GetString());
        Assert.Equal("design", root.GetProperty("mode").GetString());
        Assert.Equal("測試。", root.GetProperty("text").GetString());
        Assert.Equal("台灣華語，中性自然。", root.GetProperty("voice_prompt").GetString());
        Assert.Equal(42, root.GetProperty("seed").GetInt32());
        Assert.Equal("fixed", root.GetProperty("seed_policy").GetString());
        Assert.Equal("voxcpm2", root.GetProperty("model").GetString());
        Assert.Equal(0, root.GetProperty("priority").GetInt32());
        Assert.False(root.TryGetProperty("voice_profile_task_id", out _));
        Assert.Equal("123", handle.TaskId);
        Assert.Equal("https://3wa.tw/3waAIHub/tasks/123/status", handle.StatusUrl);
        Assert.Equal("https://3wa.tw/3waAIHub/tasks/123/result", handle.ResultUrl);
        Assert.Equal("/3waAIHub/artifacts/{artifact_id}", handle.ArtifactUrlTemplate);
    }

    [Fact]
    public async Task SubmitAsync_posts_only_the_clone_reference_and_accepts_a_string_task_id()
    {
        var handler = new RecordingHandler(() => JsonResponse(
            """
            {
              "ok": true,
              "task_id": "task-abc",
              "status_url": "tasks/task-abc/status",
              "result_url": "tasks/task-abc/result"
            }
            """));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var handle = await client.SubmitAsync(
            new ThreeWaSynthesisRequest(
                "clone text",
                "ultimate_clone",
                VoiceProfileTaskId: "profile-9",
                VoicePromptText: null),
            CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body!);
        var root = body.RootElement;
        Assert.Equal("profile-9", root.GetProperty("voice_profile_task_id").GetString());
        Assert.False(root.TryGetProperty("voice_prompt", out _));
        Assert.Equal("task-abc", handle.TaskId);
        Assert.Equal("https://3wa.tw/3waAIHub/tasks/task-abc/status", handle.StatusUrl);
    }

    [Theory]
    [InlineData("{\"status\":\"RUNNING\"}", "running")]
    [InlineData("{\"task_status\":\" success \"}", "success")]
    [InlineData("{\"result\":{\"status\":\"COMPLETED\"}}", "completed")]
    [InlineData("{\"result\":{\"task_status\":\"Failed\"}}", "failed")]
    [InlineData("{\"ok\":true}", "unknown")]
    public async Task GetTaskStatusAsync_reads_defensive_status_variants(string json, string expected)
    {
        var handler = new RecordingHandler(() => JsonResponse(json));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var actual = await client.GetTaskStatusAsync("tasks/7/status", CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal("https://3wa.tw/3waAIHub/tasks/7/status", Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task GetResultArtifactsAsync_accepts_numeric_or_string_ids_and_filters_non_audio_artifacts()
    {
        var handler = new RecordingHandler(() => JsonResponse(
            """
            {
              "result": {
                "artifacts": [
                  { "id": 17, "mime_type": "audio/wav" },
                  { "artifact_id": "voice-2", "content_type": "Audio/MPEG" },
                  { "id": "metadata", "mime_type": "application/json" },
                  { "id": "missing-mime" },
                  { "id": false, "mime_type": "audio/ogg" }
                ]
              }
            }
            """));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var artifacts = await client.GetResultArtifactsAsync("tasks/7/result", CancellationToken.None);

        Assert.Collection(
            artifacts,
            artifact =>
            {
                Assert.Equal("17", artifact.Id);
                Assert.Equal("audio/wav", artifact.MimeType);
            },
            artifact =>
            {
                Assert.Equal("voice-2", artifact.Id);
                Assert.Equal("Audio/MPEG", artifact.MimeType);
            });
    }

    [Fact]
    public async Task SubmitAsync_rejects_a_cross_origin_follow_up_before_any_token_can_be_sent_there()
    {
        const string secretStoryText = "private-story-text";
        var handler = new RecordingHandler(() => JsonResponse(
            """
            {
              "ok": true,
              "task_id": "task-1",
              "status_url": "https://attacker.invalid/status",
              "result_url": "/3waAIHub/result"
            }
            """));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() => client.SubmitAsync(
            new ThreeWaSynthesisRequest(
                secretStoryText,
                "design",
                VoiceProfileTaskId: null,
                VoicePromptText: "voice"),
            CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.DoesNotContain("attacker.invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretStoryText, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Follow_up_and_artifact_urls_are_rejected_before_sending_when_not_same_origin_https()
    {
        var handler = new RecordingHandler(() => throw new InvalidOperationException("must not send"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetTaskStatusAsync("https://attacker.invalid/status", CancellationToken.None));
        await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetResultArtifactsAsync("http://3wa.tw/3waAIHub/result", CancellationToken.None));
        await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetResultArtifactsAsync("https://3wa.tw/another-service/result", CancellationToken.None));
        await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.DownloadArtifactAsync(
                "https://attacker.invalid/artifacts/{id}",
                "7",
                destination,
                CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://3wa.tw/3waAIHub/%2E%2E%2Fadmin")]
    [InlineData("https://3wa.tw/3waAIHub/voice%2F..%2F..%2Fadmin")]
    [InlineData("https://3wa.tw/3waAIHub/voice%5C..%5C..%5Cadmin")]
    [InlineData("https://3wa.tw/3waAIHub/%252E%252E%252Fadmin")]
    public async Task Encoded_path_traversal_is_rejected_before_the_Bearer_token_is_sent(string url)
    {
        var handler = new RecordingHandler(() => throw new InvalidOperationException("must not send"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetTaskStatusAsync(url, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_treats_a_redirect_as_failure_and_does_not_expose_its_body()
    {
        const string providerBody = "provider-secret-error-body";
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://attacker.invalid/redirect") },
            Content = new StringContent(providerBody),
        });
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() => client.SubmitAsync(
            new ThreeWaSynthesisRequest("text", "design", null, "voice"),
            CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Contains("HTTP 302", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(providerBody, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_responses_with_a_known_length_are_rejected_at_the_configured_cap()
    {
        var handler = new RecordingHandler(() => JsonResponse(new string('x', 65)));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, maximumJsonResponseBytes: 64);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetTaskStatusAsync("status", CancellationToken.None));

        Assert.Contains("size limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_responses_without_a_length_are_streamed_only_to_the_configured_cap()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 65));
        var handler = new RecordingHandler(() =>
        {
            var content = new UnknownLengthContent(bytes);
            Assert.Null(content.Headers.ContentLength);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, maximumJsonResponseBytes: 64);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetTaskStatusAsync("status", CancellationToken.None));

        Assert.Contains("size limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadArtifactAsync_downloads_only_audio_and_escapes_the_artifact_id()
    {
        var audio = new byte[] { 1, 2, 3, 4 };
        var handler = new RecordingHandler(() => AudioResponse(audio, "audio/wav"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, maximumAudioResponseBytes: 8);
        await using var destination = new MemoryStream();

        await client.DownloadArtifactAsync(
            "/3waAIHub/artifacts/{artifact_id}",
            "part/1 ?",
            destination,
            CancellationToken.None);

        Assert.Equal(audio, destination.ToArray());
        Assert.Equal(
            "https://3wa.tw/3waAIHub/artifacts/part%2F1%20%3F",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task DownloadArtifactAsync_rejects_non_audio_and_oversized_responses()
    {
        var handler = new RecordingHandler(
            () => AudioResponse([1, 2, 3], "application/json"),
            () => AudioResponse([1, 2, 3, 4, 5], "audio/wav"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, maximumAudioResponseBytes: 4);
        await using var nonAudioDestination = new MemoryStream();
        await using var oversizedDestination = new MemoryStream();

        var nonAudioException = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.DownloadArtifactAsync("artifacts/{id}", "one", nonAudioDestination, CancellationToken.None));
        var oversizedException = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.DownloadArtifactAsync("artifacts/{id}", "two", oversizedDestination, CancellationToken.None));

        Assert.Contains("not audio", nonAudioException.Message, StringComparison.Ordinal);
        Assert.Contains("invalid size", oversizedException.Message, StringComparison.Ordinal);
        Assert.Empty(nonAudioDestination.ToArray());
        Assert.Empty(oversizedDestination.ToArray());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Missing_token_and_network_failures_are_safe_and_not_retried()
    {
        var missingTokenHandler = new RecordingHandler(() => throw new InvalidOperationException("must not send"));
        using var missingTokenHttpClient = CreateHttpClient(missingTokenHandler);
        var missingTokenClient = CreateClient(missingTokenHttpClient, apiToken: " ");

        var missingTokenException = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            missingTokenClient.GetTaskStatusAsync("status", CancellationToken.None));

        Assert.Contains("token", missingTokenException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(missingTokenHandler.Requests);

        const string transportSecret = "transport-secret-detail";
        var failureHandler = new RecordingHandler(() => throw new HttpRequestException(transportSecret));
        using var failureHttpClient = CreateHttpClient(failureHandler);
        var failureClient = CreateClient(failureHttpClient);

        var failureException = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            failureClient.GetTaskStatusAsync("status", CancellationToken.None));

        Assert.Single(failureHandler.Requests);
        Assert.DoesNotContain(transportSecret, failureException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, failureException.Message, StringComparison.Ordinal);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri(BaseUrl),
    };

    private static ThreeWaSynthesisClient CreateClient(
        HttpClient httpClient,
        string apiToken = ApiToken,
        int maximumJsonResponseBytes = 64 * 1024,
        int maximumAudioResponseBytes = 20 * 1024 * 1024) =>
        new(
            httpClient,
            Options.Create(new ThreeWaAiHubOptions
            {
                BaseUrl = BaseUrl,
                ApiToken = apiToken,
                MaximumJsonResponseBytes = maximumJsonResponseBytes,
                MaximumAudioResponseBytes = maximumAudioResponseBytes,
            }));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage AudioResponse(byte[] bytes, string mediaType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> responseFactories;

        public RecordingHandler(params Func<HttpResponseMessage>[] responseFactories)
        {
            this.responseFactories = new Queue<Func<HttpResponseMessage>>(responseFactories);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            if (responseFactories.Count == 0)
            {
                throw new InvalidOperationException("No fake HTTP response was configured.");
            }

            return responseFactories.Dequeue()();
        }
    }
}
