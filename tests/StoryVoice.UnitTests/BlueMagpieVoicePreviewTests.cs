using System.Net;
using System.Net.Http.Headers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class BlueMagpieVoicePreviewTests
{
    [Fact]
    public async Task Client_posts_the_internal_fixed_contract_and_validates_response_identity()
    {
        var handler = new RecordingHandler(CreateSuccessfulResponse);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://bluemagpie-gateway:8081/"),
        };
        var client = CreateClient(httpClient);

        var result = await client.SynthesizeAsync(
            "固定試音",
            BlueMagpieOptions.FemaleVoice,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "http://bluemagpie-gateway:8081/v1/audio/speech",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal("test-internal-token", handler.Headers["X-StoryVoice-Internal-Token"]);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("固定試音", body.RootElement.GetProperty("text").GetString());
        Assert.Equal(BlueMagpieOptions.FemaleVoice, body.RootElement.GetProperty("voice").GetString());
        Assert.Equal("audio/wav", result.ContentType);
        Assert.Equal(BlueMagpieOptions.PinnedModelRevision, result.ModelRevision);
        Assert.Equal(BlueMagpieOptions.FemaleVoice, result.Voice);
        Assert.Equal(CreateWavBytes(), result.Content);
    }

    [Fact]
    public async Task Client_rejects_revision_mismatch_without_exposing_response_body()
    {
        const string privateBody = "private-gateway-error";
        var handler = new RecordingHandler(() =>
        {
            var response = CreateSuccessfulResponse();
            response.Headers.Remove("X-BlueMagpie-Model-Revision");
            response.Headers.Add("X-BlueMagpie-Model-Revision", new string('0', 40));
            response.Content = new ByteArrayContent(CreateWavBytes());
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            response.ReasonPhrase = privateBody;
            return response;
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://bluemagpie-gateway:8081/"),
        };
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAsync<SeriesVoicePreviewUnavailableException>(() =>
            client.SynthesizeAsync("不得記錄的文字", BlueMagpieOptions.FemaleVoice, CancellationToken.None));

        Assert.DoesNotContain(privateBody, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("不得記錄的文字", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-internal-token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_rejects_non_wave_and_oversized_responses()
    {
        var nonWaveHandler = new RecordingHandler(() =>
            CreateSuccessfulResponse(Encoding.UTF8.GetBytes(new string('x', 64))));
        using var nonWaveHttpClient = new HttpClient(nonWaveHandler)
        {
            BaseAddress = new Uri("http://bluemagpie-gateway:8081/"),
        };

        await Assert.ThrowsAsync<SeriesVoicePreviewUnavailableException>(() =>
            CreateClient(nonWaveHttpClient).SynthesizeAsync(
                "固定試音",
                BlueMagpieOptions.FemaleVoice,
                CancellationToken.None));

        var oversizedHandler = new RecordingHandler(() => CreateSuccessfulResponse(new byte[65]));
        using var oversizedHttpClient = new HttpClient(oversizedHandler)
        {
            BaseAddress = new Uri("http://bluemagpie-gateway:8081/"),
        };

        await Assert.ThrowsAsync<SeriesVoicePreviewUnavailableException>(() =>
            CreateClient(oversizedHttpClient, maximumResponseBytes: 64).SynthesizeAsync(
                "固定試音",
                BlueMagpieOptions.FemaleVoice,
                CancellationToken.None));
    }

    [Fact]
    public async Task Client_rejects_ambiguous_gateway_identity_headers()
    {
        var handler = new RecordingHandler(() =>
        {
            var response = CreateSuccessfulResponse();
            response.Headers.Add(
                "X-BlueMagpie-Model-Revision",
                BlueMagpieOptions.PinnedModelRevision);
            return response;
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://bluemagpie-gateway:8081/"),
        };

        await Assert.ThrowsAsync<SeriesVoicePreviewUnavailableException>(() =>
            CreateClient(httpClient).SynthesizeAsync(
                "固定試音",
                BlueMagpieOptions.FemaleVoice,
                CancellationToken.None));
    }

    [Fact]
    public async Task Preview_service_only_accepts_the_two_local_voices_and_uses_no_book_text()
    {
        var fakeClient = new FakeBlueMagpieClient();
        var service = new SeriesVoicePreviewService(
            fakeClient,
            Options.Create(CreateOptions()));

        var result = await service.GenerateAsync(
            new SeriesVoicePreviewRequest(
                BlueMagpieOptions.ProviderName,
                BlueMagpieOptions.MaleVoice),
            CancellationToken.None);

        Assert.Equal(BlueMagpieOptions.MaleVoice, fakeClient.Voice);
        Assert.Equal("這是一段不含書籍正文的台灣華語聲線示範。", fakeClient.Text);
        Assert.False(fakeClient.SynthesisTokenCanBeCanceled);
        Assert.Equal(BlueMagpieOptions.PinnedModelRevision, result.ModelRevision);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateAsync(
            new SeriesVoicePreviewRequest("voai", "佑希"),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateAsync(
            new SeriesVoicePreviewRequest(BlueMagpieOptions.ProviderName, "unknown"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Preview_service_singleflights_and_caches_each_successful_fixed_voice()
    {
        var fakeClient = new BlockingBlueMagpieClient();
        var service = new SeriesVoicePreviewService(
            fakeClient,
            Options.Create(CreateOptions()));
        var request = new SeriesVoicePreviewRequest(
            BlueMagpieOptions.ProviderName,
            BlueMagpieOptions.MaleVoice);

        var first = service.GenerateAsync(request, CancellationToken.None);
        await fakeClient.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var concurrent = service.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(1, fakeClient.CallCount);
        fakeClient.Release.TrySetResult();
        await Task.WhenAll(first, concurrent);

        var cached = await service.GenerateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(1, fakeClient.CallCount);
        Assert.Equal(BlueMagpieOptions.MaleVoice, cached.Voice);
    }

    [Fact]
    public async Task Preview_service_does_not_cache_failures()
    {
        var fakeClient = new FailOnceBlueMagpieClient();
        var service = new SeriesVoicePreviewService(
            fakeClient,
            Options.Create(CreateOptions()));
        var request = new SeriesVoicePreviewRequest(
            BlueMagpieOptions.ProviderName,
            BlueMagpieOptions.FemaleVoice);

        await Assert.ThrowsAsync<SeriesVoicePreviewUnavailableException>(() =>
            service.GenerateAsync(request, TestContext.Current.CancellationToken));
        var recovered = await service.GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, fakeClient.CallCount);
        Assert.Equal(BlueMagpieOptions.FemaleVoice, recovered.Voice);
    }

    [Fact]
    public async Task Preview_service_fails_closed_when_disabled()
    {
        var service = new SeriesVoicePreviewService(
            new FakeBlueMagpieClient(),
            Options.Create(new BlueMagpieOptions { Enabled = false }));

        await Assert.ThrowsAsync<SeriesVoicePreviewUnavailableException>(() => service.GenerateAsync(
            new SeriesVoicePreviewRequest(
                BlueMagpieOptions.ProviderName,
                BlueMagpieOptions.FemaleVoice),
            CancellationToken.None));
    }

    private static BlueMagpieTtsClient CreateClient(
        HttpClient httpClient,
        int maximumResponseBytes = 1024) =>
        new(
            httpClient,
            Options.Create(CreateOptions(maximumResponseBytes)));

    private static BlueMagpieOptions CreateOptions(int maximumResponseBytes = 1024) => new()
    {
        Enabled = true,
        BaseUrl = "http://bluemagpie-gateway:8081/",
        InternalToken = "test-internal-token",
        ModelRevision = BlueMagpieOptions.PinnedModelRevision,
        ConnectTimeoutSeconds = 10,
        QueueTimeoutSeconds = 15,
        MaximumResponseBytes = maximumResponseBytes,
    };

    private static HttpResponseMessage CreateSuccessfulResponse() =>
        CreateSuccessfulResponse(CreateWavBytes());

    private static HttpResponseMessage CreateSuccessfulResponse(byte[] content)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        response.Headers.Add("X-BlueMagpie-Model-Revision", BlueMagpieOptions.PinnedModelRevision);
        response.Headers.Add("X-BlueMagpie-Voice", BlueMagpieOptions.FemaleVoice);
        return response;
    }

    private static byte[] CreateWavBytes()
    {
        var bytes = new byte[46];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 38);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), 48_000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), 96_000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34, 2), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), 2);
        return bytes;
    }

    private sealed class RecordingHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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

    private sealed class FakeBlueMagpieClient : IBlueMagpieTtsClient
    {
        public string? Text { get; private set; }
        public string? Voice { get; private set; }
        public bool SynthesisTokenCanBeCanceled { get; private set; }
        public int CallCount { get; private set; }

        public Task<BlueMagpieSynthesisResult> SynthesizeAsync(
            string text,
            string voice,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Text = text;
            Voice = voice;
            SynthesisTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            return Task.FromResult(new BlueMagpieSynthesisResult(
                CreateWavBytes(),
                "audio/wav",
                BlueMagpieOptions.PinnedModelRevision,
                voice));
        }
    }

    private sealed class BlockingBlueMagpieClient : IBlueMagpieTtsClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public async Task<BlueMagpieSynthesisResult> SynthesizeAsync(
            string text,
            string voice,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Assert.False(cancellationToken.CanBeCanceled);
            Started.TrySetResult();
            await Release.Task;
            return new BlueMagpieSynthesisResult(
                CreateWavBytes(),
                "audio/wav",
                BlueMagpieOptions.PinnedModelRevision,
                voice);
        }
    }

    private sealed class FailOnceBlueMagpieClient : IBlueMagpieTtsClient
    {
        public int CallCount { get; private set; }

        public Task<BlueMagpieSynthesisResult> SynthesizeAsync(
            string text,
            string voice,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new SeriesVoicePreviewUnavailableException();
            }

            return Task.FromResult(new BlueMagpieSynthesisResult(
                CreateWavBytes(),
                "audio/wav",
                BlueMagpieOptions.PinnedModelRevision,
                voice));
        }
    }
}
