using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;
using StoryVoice.Infrastructure;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class LocalCloneGatewayClientTests
{
    [Fact]
    public async Task Sends_only_the_pinned_internal_contract_and_accepts_strict_pcm_output()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = CreatePcmWave(sampleRate: 24_000, durationSeconds: 1);
        var handler = new StubHandler(_ => CreateSuccessResponse(output));
        using var httpClient = CreateHttpClient(handler);
        var client = new LocalCloneGatewayClient(httpClient, CreateOptions());

        var result = await client.SynthesizeAsync(
            new LocalCloneGatewayRequest(
                "這是私人試音。",
                "這是參考逐字稿。",
                CreatePcmWave(sampleRate: 48_000, durationSeconds: 10)),
            cancellationToken);

        Assert.Equal("audio/wav", result.ContentType);
        Assert.Equal(output, result.Content);
        Assert.Equal(new Uri("http://local-clone-gateway:8082/v1/voice-clone/speech"), handler.RequestUri);
        Assert.Equal(new string('t', 32), handler.Token);
        Assert.Equal(
            ["reference_audio", "reference_text", "text"],
            handler.MultipartNames.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Rejects_an_output_that_is_not_pcm16_24khz_mono()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new StubHandler(_ => CreateSuccessResponse(
            CreatePcmWave(sampleRate: 48_000, durationSeconds: 1)));
        using var httpClient = CreateHttpClient(handler);
        var client = new LocalCloneGatewayClient(httpClient, CreateOptions());

        var exception = await Assert.ThrowsAsync<LocalClonePreviewUnavailableException>(() =>
            client.SynthesizeAsync(
                new LocalCloneGatewayRequest("試音", "逐字稿", [1, 2, 3]),
                cancellationToken));

        Assert.Equal(LocalClonePreviewFailureKind.GatewayContractInvalid, exception.FailureKind);
        Assert.Equal("local_clone_preview_gateway_contract_invalid", exception.StableCode);
    }

    [Fact]
    public async Task Maps_transient_gateway_status_without_reading_an_error_body()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("private diagnostic must not escape"),
        });
        using var httpClient = CreateHttpClient(handler);
        var client = new LocalCloneGatewayClient(httpClient, CreateOptions());

        var exception = await Assert.ThrowsAsync<LocalClonePreviewUnavailableException>(() =>
            client.SynthesizeAsync(
                new LocalCloneGatewayRequest("試音", "逐字稿", [1, 2, 3]),
                cancellationToken));

        Assert.Equal(LocalClonePreviewFailureKind.GatewayUnavailable, exception.FailureKind);
        Assert.Equal("local_clone_preview_gateway_unavailable", exception.StableCode);
    }

    [Fact]
    public async Task Rejects_duplicate_attestation_headers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var output = CreatePcmWave(sampleRate: 24_000, durationSeconds: 1);
        var handler = new StubHandler(_ =>
        {
            var response = CreateSuccessResponse(output);
            response.Headers.TryAddWithoutValidation(
                "X-CosyVoice-Model-Revision",
                LocalClonePreviewOptions.PinnedModelRevision);
            return response;
        });
        using var httpClient = CreateHttpClient(handler);
        var client = new LocalCloneGatewayClient(httpClient, CreateOptions());

        var exception = await Assert.ThrowsAsync<LocalClonePreviewUnavailableException>(() =>
            client.SynthesizeAsync(
                new LocalCloneGatewayRequest("試音", "逐字稿", [1, 2, 3]),
                cancellationToken));

        Assert.Equal(LocalClonePreviewFailureKind.GatewayContractInvalid, exception.FailureKind);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8082/")]
    [InlineData("http://local-clone-gateway:8093/")]
    [InlineData("https://local-clone-gateway:8082/")]
    [InlineData("http://local-clone-gateway:8082/redirect")]
    public void Infrastructure_options_reject_any_non_pinned_gateway_origin(string gatewayBaseUrl)
    {
        using var provider = BuildInfrastructureProvider(new Dictionary<string, string?>
        {
            [$"{LocalClonePreviewOptions.SectionName}:GatewayBaseUrl"] = gatewayBaseUrl,
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LocalClonePreviewOptions>>().Value);
    }

    [Fact]
    public void Infrastructure_options_reject_parent_traversal_in_private_asset_mapping()
    {
        var profileId = Guid.NewGuid();
        var prefix = $"{LocalClonePreviewOptions.SectionName}:AllowedProfiles:{profileId:D}";
        using var provider = BuildInfrastructureProvider(new Dictionary<string, string?>
        {
            [$"{LocalClonePreviewOptions.SectionName}:Enabled"] = "true",
            [$"{LocalClonePreviewOptions.SectionName}:InternalToken"] = new string('t', 32),
            [$"{LocalClonePreviewOptions.SectionName}:AssetRootPath"] = "private-assets",
            [$"{prefix}:Label"] = "私人試音",
            [$"{prefix}:ReferenceAudioRelativePath"] = "../reference.wav",
            [$"{prefix}:TranscriptRelativePath"] = "transcript.txt",
            [$"{prefix}:ExpectedReferenceAudioSha256"] = new string('a', 64),
            [$"{prefix}:ExpectedTranscriptSha256"] = new string('b', 64),
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LocalClonePreviewOptions>>().Value);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    public void Infrastructure_options_reject_unsafe_internal_tokens(string token)
    {
        var profileId = Guid.NewGuid();
        var prefix = $"{LocalClonePreviewOptions.SectionName}:AllowedProfiles:{profileId:D}";
        using var provider = BuildInfrastructureProvider(new Dictionary<string, string?>
        {
            [$"{LocalClonePreviewOptions.SectionName}:Enabled"] = "true",
            [$"{LocalClonePreviewOptions.SectionName}:InternalToken"] = token,
            [$"{LocalClonePreviewOptions.SectionName}:AssetRootPath"] = "private-assets",
            [$"{prefix}:Label"] = "私人試音",
            [$"{prefix}:ReferenceAudioRelativePath"] = "reference.wav",
            [$"{prefix}:TranscriptRelativePath"] = "transcript.txt",
            [$"{prefix}:ExpectedReferenceAudioSha256"] = new string('a', 64),
            [$"{prefix}:ExpectedTranscriptSha256"] = new string('b', 64),
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LocalClonePreviewOptions>>().Value);
    }

    private static ServiceProvider BuildInfrastructureProvider(
        Dictionary<string, string?> localCloneConfiguration)
    {
        var values = new Dictionary<string, string?>(localCloneConfiguration)
        {
            ["ConnectionStrings:Postgres"] =
                "Host=localhost;Database=storyvoice;Username=test;Password=test",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddStoryVoiceInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri(LocalClonePreviewOptions.PinnedGatewayBaseUrl),
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static IOptions<LocalClonePreviewOptions> CreateOptions() => Options.Create(
        new LocalClonePreviewOptions
        {
            Enabled = true,
            InternalToken = new string('t', 32),
            MaximumResponseBytes = 16 * 1024 * 1024,
        });

    private static HttpResponseMessage CreateSuccessResponse(byte[] content)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        response.Headers.Add(
            "X-CosyVoice-Source-Revision",
            LocalClonePreviewOptions.PinnedCosyVoiceSourceRevision);
        response.Headers.Add("X-CosyVoice-Model-Id", LocalClonePreviewOptions.PinnedModelId);
        response.Headers.Add(
            "X-CosyVoice-Model-Revision",
            LocalClonePreviewOptions.PinnedModelRevision);
        return response;
    }

    private static byte[] CreatePcmWave(int sampleRate, int durationSeconds)
    {
        const ushort channels = 1;
        const ushort bitsPerSample = 16;
        const ushort blockAlign = 2;
        var dataLength = checked(sampleRate * durationSeconds * blockAlign);
        var content = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(content);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(4), checked((uint)(content.Length - 8)));
        "WAVEfmt "u8.CopyTo(content.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(22), channels);
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(24), checked((uint)sampleRate));
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(28), checked((uint)(sampleRate * blockAlign)));
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(32), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(34), bitsPerSample);
        "data"u8.CopyTo(content.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(40), checked((uint)dataLength));
        return content;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Token { get; private set; }

        public string[] MultipartNames { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Token = request.Headers.GetValues("X-StoryVoice-Internal-Token").Single();
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            MultipartNames = multipart
                .Select(part => part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty)
                .ToArray();
            return Task.FromResult(responseFactory(request));
        }
    }
}
