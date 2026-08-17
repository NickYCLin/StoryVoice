using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class ThreeWaVoiceProfileClientTests
{
    private const string BaseUrl = "https://3wa.tw/3waAIHub/";
    private const string ApiToken = "test-three-wa-profile-token";

    [Theory]
    [InlineData("731245", "731245")]
    [InlineData("\"profile-task-8\"", "profile-task-8")]
    public async Task PrepareAsync_posts_the_canonical_profile_contract_and_accepts_numeric_or_string_task_ids(
        string jsonTaskId,
        string expectedTaskId)
    {
        var handler = new RecordingHandler(() => JsonResponse(
            $$"""
            {
              "ok": true,
              "task_id": {{jsonTaskId}},
              "prompt_text": "請確認這段語音"
            }
            """));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);
        await using var referenceWav = new MemoryStream("RIFF-test-WAVE"u8.ToArray());

        var result = await client.PrepareAsync(
            referenceWav,
            "sample.wav",
            "測試角色",
            "explicit_permission",
            "自訂逐字稿",
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://3wa.tw/3waAIHub/api.php?mode=voice_generate",
            request.Uri.AbsoluteUri);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(ApiToken, request.AuthorizationParameter);
        Assert.StartsWith("multipart/form-data;", request.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile_prepare", request.Body, StringComparison.Ordinal);
        Assert.Contains("profile_name", request.Body, StringComparison.Ordinal);
        Assert.Contains("測試角色", request.Body, StringComparison.Ordinal);
        Assert.Contains("consent_type", request.Body, StringComparison.Ordinal);
        Assert.Contains("explicit_permission", request.Body, StringComparison.Ordinal);
        Assert.Contains("prompt_text", request.Body, StringComparison.Ordinal);
        Assert.Contains("自訂逐字稿", request.Body, StringComparison.Ordinal);
        Assert.Contains("reference_wav", request.Body, StringComparison.Ordinal);
        Assert.Contains("sample.wav", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("cluster_api.php", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(expectedTaskId, result.TaskId);
        Assert.Equal("請確認這段語音", result.DraftTranscript);
    }

    [Fact]
    public async Task GetStatusAsync_uses_the_canonical_GET_query_and_reads_profile_state()
    {
        var handler = new RecordingHandler(() => JsonResponse(
            """
            {
              "ok": true,
              "task_status": "ready",
              "transcript_confirmed": true,
              "prompt_text": "確認完成",
              "transcription_status": "completed"
            }
            """));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.GetStatusAsync(" 731245 /? ", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://3wa.tw/3waAIHub/api.php?mode=voice_generate&operation=profile_status&voice_profile_task_id=731245%20%2F%3F",
            request.Uri.AbsoluteUri);
        Assert.Null(request.Body);
        Assert.Equal("ready", result.TaskStatus);
        Assert.True(result.TranscriptConfirmed);
        Assert.Equal("確認完成", result.DraftTranscript);
        Assert.False(result.TranscriptionFailed);
    }

    [Fact]
    public async Task ConfirmAsync_posts_the_canonical_JSON_contract()
    {
        var handler = new RecordingHandler(() => JsonResponse("{\"ok\":true}"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await client.ConfirmAsync(" 731245 ", " 這是確認逐字稿。 ", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://3wa.tw/3waAIHub/api.php?mode=voice_generate",
            request.Uri.AbsoluteUri);
        Assert.StartsWith("application/json", request.ContentType, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("profile_confirm", body.RootElement.GetProperty("operation").GetString());
        Assert.Equal("731245", body.RootElement.GetProperty("voice_profile_task_id").GetString());
        Assert.Equal("這是確認逐字稿。", body.RootElement.GetProperty("prompt_text").GetString());
    }

    [Fact]
    public async Task HTTP_failures_are_bounded_and_never_expose_provider_details_or_secrets()
    {
        const string privateProviderBody = "private-provider-diagnostics";
        const string privateTaskId = "private-task-id";
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(privateProviderBody),
        });
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetStatusAsync(privateTaskId, CancellationToken.None));

        Assert.Equal("3wa Cluster API request failed with HTTP 400.", exception.Message);
        Assert.DoesNotContain(privateProviderBody, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(privateTaskId, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JSON_responses_are_streamed_only_to_the_configured_limit()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 65));
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(bytes)),
        });
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, maximumJsonResponseBytes: 64);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetStatusAsync("731245", CancellationToken.None));

        Assert.Contains("超過允許大小", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 65), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_JSON_and_transport_failures_return_safe_errors()
    {
        const string malformedPrivateBody = "private-malformed-json";
        var malformedHandler = new RecordingHandler(() => JsonResponse(malformedPrivateBody));
        using var malformedHttpClient = CreateHttpClient(malformedHandler);
        var malformedClient = CreateClient(malformedHttpClient);

        var malformedException = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            malformedClient.GetStatusAsync("731245", CancellationToken.None));

        Assert.Equal("3wa Cluster API 回應內容無法解析。", malformedException.Message);
        Assert.DoesNotContain(malformedPrivateBody, malformedException.ToString(), StringComparison.Ordinal);

        const string transportPrivateDetail = "private-transport-detail";
        var transportHandler = new RecordingHandler(() => throw new HttpRequestException(transportPrivateDetail));
        using var transportHttpClient = CreateHttpClient(transportHandler);
        var transportClient = CreateClient(transportHttpClient);

        var transportException = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            transportClient.GetStatusAsync("731245", CancellationToken.None));

        Assert.Equal("3wa Cluster API request failed.", transportException.Message);
        Assert.DoesNotContain(transportPrivateDetail, transportException.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiToken, transportException.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_token_fails_before_the_HTTP_handler_is_called()
    {
        var handler = new RecordingHandler(() => throw new InvalidOperationException("must not send"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, apiToken: " ");

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetStatusAsync("731245", CancellationToken.None));

        Assert.Contains("ApiToken", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Invalid_token_format_is_rejected_safely_before_the_HTTP_handler_is_called()
    {
        const string invalidToken = "private\r\ntoken";
        var handler = new RecordingHandler(() => throw new InvalidOperationException("must not send"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, apiToken: invalidToken);

        var exception = await Assert.ThrowsAsync<ThreeWaAiHubException>(() =>
            client.GetStatusAsync("731245", CancellationToken.None));

        Assert.Equal("3wa Cluster API token format is invalid.", exception.Message);
        Assert.DoesNotContain(invalidToken, exception.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://3wa.tw/3waAIHub/")]
    [InlineData("https://3wa.tw.evil.example/3waAIHub/")]
    [InlineData("https://3wa.tw/3waAIHub")]
    [InlineData("https://3wa.tw/3waAIHub/?redirect=evil")]
    public void Infrastructure_options_reject_noncanonical_3wa_base_urls(string baseUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=storyvoice;Username=test;Password=test",
                [$"{ThreeWaAiHubOptions.SectionName}:BaseUrl"] = baseUrl,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddStoryVoiceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ThreeWaAiHubOptions>>().Value);
    }

    [Theory]
    [InlineData("MaximumJsonResponseBytes", "1023")]
    [InlineData("MaximumJsonResponseBytes", "1048577")]
    [InlineData("MaximumAudioResponseBytes", "65535")]
    [InlineData("MaximumAudioResponseBytes", "104857601")]
    public void Infrastructure_options_reject_unsafe_3wa_response_limits(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=storyvoice;Username=test;Password=test",
                [$"{ThreeWaAiHubOptions.SectionName}:{key}"] = value,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddStoryVoiceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ThreeWaAiHubOptions>>().Value);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri(BaseUrl),
    };

    private static ThreeWaVoiceProfileClient CreateClient(
        HttpClient httpClient,
        string apiToken = ApiToken,
        int maximumJsonResponseBytes = 64 * 1024) =>
        new(
            httpClient,
            Options.Create(new ThreeWaAiHubOptions
            {
                BaseUrl = BaseUrl,
                ApiToken = apiToken,
                MaximumJsonResponseBytes = maximumJsonResponseBytes,
            }));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        string? ContentType,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class RecordingHandler(params Func<HttpResponseMessage>[] responseFactories) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> responseFactories = new(responseFactories);

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
                request.Content?.Headers.ContentType?.ToString(),
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
