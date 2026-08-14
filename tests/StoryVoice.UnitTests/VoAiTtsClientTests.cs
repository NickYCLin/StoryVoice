using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class VoAiTtsClientTests
{
    [Fact]
    public async Task SynthesizeWavAsync_posts_the_pinned_voice_and_wav_contract()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateWavBytes())
        });
        using var factory = new FixedHttpClientFactory(handler);
        var client = CreateClient(factory);
        await using var destination = new MemoryStream();

        await client.SynthesizeWavAsync(
            new VoAiSpeechSynthesisRequest(
                "測試文字",
                "Neo",
                "佑希",
                "預設",
                Speed: 1.1,
                PitchShift: 4),
            destination,
            CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://connect.voai.ai/TTS/Speech", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("test-api-key", handler.Headers["x-api-key"]);
        Assert.Equal("wav", handler.Headers["x-output-format"]);
        Assert.Equal("32000", handler.Headers["x-sample-rate"]);

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("Neo", root.GetProperty("version").GetString());
        Assert.Equal("測試文字", root.GetProperty("text").GetString());
        Assert.Equal("佑希", root.GetProperty("speaker").GetString());
        Assert.Equal("預設", root.GetProperty("style").GetString());
        Assert.Equal(1.1, root.GetProperty("speed").GetDouble(), precision: 10);
        Assert.Equal(4, root.GetProperty("pitch_shift").GetInt32());
        Assert.Equal(0, root.GetProperty("style_weight").GetDouble());
        Assert.Equal(0, root.GetProperty("breath_pause").GetDouble());
        Assert.Equal(CreateWavBytes(), destination.ToArray());
        Assert.Equal([VoAiTtsClient.HttpClientName], factory.RequestedNames);
    }

    [Fact]
    public async Task SynthesizeWavAsync_creates_the_named_client_for_each_call()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateWavBytes())
        });
        using var factory = new FixedHttpClientFactory(handler);
        var client = CreateClient(factory);

        for (var index = 0; index < 2; index++)
        {
            await using var destination = new MemoryStream();
            await client.SynthesizeWavAsync(
                new VoAiSpeechSynthesisRequest("text", "Neo", "佑希", "預設", 1, 0),
                destination,
                CancellationToken.None);
        }

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(
            [VoAiTtsClient.HttpClientName, VoAiTtsClient.HttpClientName],
            factory.RequestedNames);
    }

    [Fact]
    public async Task SynthesizeWavAsync_rejects_a_non_https_base_url_before_creating_a_client()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateWavBytes())
        });
        using var factory = new FixedHttpClientFactory(handler);
        var client = CreateClient(factory, baseUrl: "http://connect.voai.ai/");
        await using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SynthesizeWavAsync(
                new VoAiSpeechSynthesisRequest("text", "Neo", "佑希", "預設", 1, 0),
                destination,
                CancellationToken.None));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
        Assert.Empty(factory.RequestedNames);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SynthesizeWavAsync_does_not_retry_or_expose_the_error_body()
    {
        const string errorBody = "provider-secret-error-body";
        const string text = "private-story-text";
        var handler = new RecordingHandler(() => new HttpResponseMessage((HttpStatusCode)529)
        {
            Content = new StringContent(errorBody)
        });
        using var factory = new FixedHttpClientFactory(handler);
        var client = CreateClient(factory);
        await using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SynthesizeWavAsync(
                new VoAiSpeechSynthesisRequest(text, "Neo", "佑希", "預設", 1, 0),
                destination,
                CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal((HttpStatusCode)529, exception.StatusCode);
        Assert.DoesNotContain(errorBody, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(text, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-api-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynthesizeWavAsync_rejects_a_response_over_the_configured_cap()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateWavBytes())
        });
        using var factory = new FixedHttpClientFactory(handler);
        var client = CreateClient(factory, maximumResponseBytes: 20);
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.SynthesizeWavAsync(
                new VoAiSpeechSynthesisRequest("text", "Neo", "佑希", "預設", 1, 0),
                destination,
                CancellationToken.None));

        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task SynthesizeWavAsync_rejects_a_success_response_that_is_not_wav()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not-wave-data"))
        });
        using var factory = new FixedHttpClientFactory(handler);
        var client = CreateClient(factory);
        await using var destination = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.SynthesizeWavAsync(
                new VoAiSpeechSynthesisRequest("text", "Neo", "佑希", "預設", 1, 0),
                destination,
                CancellationToken.None));

        Assert.Empty(destination.ToArray());
    }

    private static VoAiTtsClient CreateClient(
        IHttpClientFactory httpClientFactory,
        long maximumResponseBytes = 1_024,
        string baseUrl = "https://connect.voai.ai/") =>
        new(
            httpClientFactory,
            Options.Create(new VoAiOptions
            {
                BaseUrl = baseUrl,
                ApiKey = "test-api-key",
                TimeoutSeconds = 10,
                MaximumResponseBytes = maximumResponseBytes,
                SampleRate = 32_000
            }));

    private static byte[] CreateWavBytes()
    {
        var bytes = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        return bytes;
    }

    private sealed class RecordingHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = Assert.Single(header.Value);
            }

            return responseFactory();
        }
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        public List<string> RequestedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return new HttpClient(handler, disposeHandler: false);
        }

        public void Dispose() => handler.Dispose();
    }
}
