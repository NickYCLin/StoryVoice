using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Series;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class BlueMagpieMultiVoiceNarrationProviderTests
{
    [Fact]
    public void Provider_identity_is_the_exact_BM1_runtime_contract()
    {
        var provider = CreateProvider(new RecordingClient(), new RecordingComposer());

        Assert.Equal("bluemagpie", provider.ProviderName);
        Assert.Equal(
            "bm1-d2d7ef3e81456915eb7a3cfe2446a9f19417c21b",
            provider.ProviderVersion);
        Assert.NotEqual(BlueMagpieOptions.PinnedModelRevision, provider.ProviderVersion);
    }

    [Fact]
    public async Task SynthesizeAsync_chunks_at_120_scalars_and_composes_48khz_with_turn_policies()
    {
        var root = CreateRoot();
        var outputPath = Path.Combine(root, "book.mp3");
        var client = new RecordingClient();
        var composer = new RecordingComposer();
        var provider = CreateProvider(client, composer);
        var progress = new List<NarrationSynthesisProgress>();
        var request = CreateRequest(
        [
            new NarrationTurn(
                new string('甲', 121),
                BlueMagpieOptions.FemaleVoice,
                "+0%",
                "+0Hz",
                "-5%",
                250),
            new NarrationTurn(
                "你好",
                BlueMagpieOptions.MaleVoice,
                "+0%",
                "+0Hz",
                "+0%",
                500),
        ]);

        try
        {
            await provider.SynthesizeAsync(
                request,
                outputPath,
                (value, _) =>
                {
                    progress.Add(value);
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.Collection(
                client.Requests,
                item =>
                {
                    Assert.Equal(120, item.Text.EnumerateRunes().Count());
                    Assert.Equal(BlueMagpieOptions.FemaleVoice, item.Voice);
                },
                item =>
                {
                    Assert.Single(item.Text.EnumerateRunes());
                    Assert.Equal(BlueMagpieOptions.FemaleVoice, item.Voice);
                },
                item =>
                {
                    Assert.Equal("你好", item.Text);
                    Assert.Equal(BlueMagpieOptions.MaleVoice, item.Voice);
                });
            Assert.Equal(48_000, composer.OutputSampleRate);
            Assert.Collection(
                composer.Segments,
                item =>
                {
                    Assert.Equal(250, item.PauseBeforeMs);
                    Assert.Equal("-5%", item.Volume);
                },
                item =>
                {
                    Assert.Equal(0, item.PauseBeforeMs);
                    Assert.Equal("-5%", item.Volume);
                },
                item =>
                {
                    Assert.Equal(500, item.PauseBeforeMs);
                    Assert.Equal("+0%", item.Volume);
                });
            Assert.Collection(
                progress,
                value => Assert.Equal(new NarrationSynthesisProgress(1, 3), value),
                value => Assert.Equal(new NarrationSynthesisProgress(2, 3), value),
                value => Assert.Equal(new NarrationSynthesisProgress(3, 3), value));
            Assert.True(File.Exists(outputPath));
            Assert.Empty(Directory.EnumerateDirectories(root, "bluemagpie-tts-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SplitText_prefers_Chinese_punctuation_and_never_splits_surrogate_pairs()
    {
        var punctuationText = new string('甲', 100) + "。" + new string('乙', 30);
        var punctuationChunks = BlueMagpieMultiVoiceNarrationProvider.SplitText(punctuationText);
        var emojiChunks = BlueMagpieMultiVoiceNarrationProvider.SplitText(
            string.Concat(Enumerable.Repeat("😀", 121)));

        Assert.Collection(
            punctuationChunks,
            first => Assert.Equal(new string('甲', 100) + "。", first),
            second => Assert.Equal(new string('乙', 30), second));
        Assert.Equal(2, emojiChunks.Count);
        Assert.All(
            emojiChunks,
            chunk => Assert.InRange(
                chunk.EnumerateRunes().Count(),
                1,
                BlueMagpieMultiVoiceNarrationProvider.MaximumTextScalarsPerChunk));
        Assert.Equal(121, emojiChunks.Sum(chunk => chunk.EnumerateRunes().Count()));
    }

    [Theory]
    [InlineData("unknown", "+0%", "+0Hz")]
    [InlineData("female_voice", "+1%", "+0Hz")]
    [InlineData("female_voice", "+0%", "+1Hz")]
    public async Task SynthesizeAsync_rejects_voice_or_unsupported_prosody_before_HTTP(
        string voice,
        string rate,
        string pitch)
    {
        var client = new RecordingClient();
        var composer = new RecordingComposer();
        var provider = CreateProvider(client, composer);
        var request = CreateRequest(
            [new NarrationTurn("你好", voice, rate, pitch, "+0%", 0)]);

        var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
            provider.SynthesizeAsync(request, "unused.mp3", null, CancellationToken.None));

        Assert.Equal("bluemagpie_provider_contract_invalid", exception.ErrorCode);
        Assert.Empty(client.Requests);
        Assert.Empty(composer.Segments);
    }

    [Fact]
    public async Task Dispatcher_rejects_a_character_version_mismatch_before_HTTP()
    {
        var client = new RecordingClient();
        var provider = CreateProvider(client, new RecordingComposer());
        var dispatcher = new NarrationProviderDispatcher(new NarrationProviderRegistry([provider]));
        var request = CreateRequest(
            [NeutralTurn()],
            characterContracts:
            [
                new NarrationProviderContract(
                    provider.ProviderName,
                    BlueMagpieOptions.PinnedModelRevision),
            ]);

        var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
            dispatcher.SynthesizeAsync(
                provider.ProviderName,
                request,
                "unused.mp3",
                null,
                CancellationToken.None));

        Assert.Equal("provider_version_mismatch", exception.ErrorCode);
        Assert.Empty(client.Requests);
    }

    [Theory]
    [InlineData("BLUEMAGPIE", "bm1-d2d7ef3e81456915eb7a3cfe2446a9f19417c21b")]
    [InlineData("bluemagpie", "6f7cab914a1e27c56b504ec663c0144dc25cc0a3")]
    public async Task Dispatcher_requires_the_exact_provider_name_and_runtime_version_before_HTTP(
        string requestedProvider,
        string requestedVersion)
    {
        var client = new RecordingClient();
        var provider = CreateProvider(client, new RecordingComposer());
        var dispatcher = new NarrationProviderDispatcher(new NarrationProviderRegistry([provider]));
        var request = new MultiVoiceNarrationRequest(
            [NeutralTurn()],
            new NarrationProviderContract(requestedProvider, requestedVersion),
            []);

        var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
            dispatcher.SynthesizeAsync(
                requestedProvider,
                request,
                "unused.mp3",
                null,
                CancellationToken.None));

        Assert.Equal("provider_version_mismatch", exception.ErrorCode);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Gateway_unavailability_is_retryable_and_cleans_incomplete_artifacts()
    {
        var root = CreateRoot();
        var outputPath = Path.Combine(root, "book.mp3");
        var client = new RecordingClient { Failure = new SeriesVoicePreviewUnavailableException() };
        var provider = CreateProvider(client, new RecordingComposer());

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provider.SynthesizeAsync(
                    CreateRequest([NeutralTurn()]),
                    outputPath,
                    null,
                    CancellationToken.None));

            Assert.IsNotType<PermanentNarrationProviderException>(exception);
            Assert.Equal("bluemagpie_provider_unavailable", exception.Message);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.EnumerateDirectories(root, "bluemagpie-tts-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Gateway_contract_violation_is_permanent_and_never_composes()
    {
        var client = new RecordingClient
        {
            Failure = new SeriesVoicePreviewUnavailableException(
                SeriesVoicePreviewFailureKind.ContractViolation),
        };
        var composer = new RecordingComposer();
        var provider = CreateProvider(client, composer);

        var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
            provider.SynthesizeAsync(
                CreateRequest([NeutralTurn()]),
                "unused.mp3",
                null,
                CancellationToken.None));

        Assert.Equal("bluemagpie_provider_contract_invalid", exception.ErrorCode);
        Assert.Empty(composer.Segments);
    }

    [Fact]
    public async Task Preview_only_enablement_rejects_formal_narration_before_HTTP()
    {
        var client = new RecordingClient();
        var provider = new BlueMagpieMultiVoiceNarrationProvider(
            client,
            new RecordingComposer(),
            Options.Create(new BlueMagpieOptions
            {
                Enabled = true,
                FormalNarrationEnabled = false,
                InternalToken = new string('t', 32),
                ModelRevision = BlueMagpieOptions.PinnedModelRevision,
            }),
            NullLogger<BlueMagpieMultiVoiceNarrationProvider>.Instance);

        var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
            provider.SynthesizeAsync(
                CreateRequest([NeutralTurn()]),
                "unused.mp3",
                null,
                CancellationToken.None));

        Assert.Equal("provider_version_mismatch", exception.ErrorCode);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task Invalid_returned_model_contract_is_permanent_and_does_not_compose()
    {
        var root = CreateRoot();
        var client = new RecordingClient { ReturnedRevision = new string('0', 40) };
        var composer = new RecordingComposer();
        var provider = CreateProvider(client, composer);

        try
        {
            var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
                provider.SynthesizeAsync(
                    CreateRequest([NeutralTurn()]),
                    Path.Combine(root, "unused.mp3"),
                    null,
                    CancellationToken.None));

            Assert.Equal("bluemagpie_provider_contract_invalid", exception.ErrorCode);
            Assert.Empty(composer.Segments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_returned_provider_contract_is_permanent_and_does_not_compose()
    {
        var root = CreateRoot();
        var client = new RecordingClient { ReturnedProviderVersion = BlueMagpieOptions.PinnedModelRevision };
        var composer = new RecordingComposer();
        var provider = CreateProvider(client, composer);

        try
        {
            var exception = await Assert.ThrowsAsync<PermanentNarrationProviderException>(() =>
                provider.SynthesizeAsync(
                    CreateRequest([NeutralTurn()]),
                    Path.Combine(root, "unused.mp3"),
                    null,
                    CancellationToken.None));

            Assert.Equal("bluemagpie_provider_contract_invalid", exception.ErrorCode);
            Assert.Empty(composer.Segments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_stops_the_current_chunk_and_removes_working_files()
    {
        var root = CreateRoot();
        var outputPath = Path.Combine(root, "book.mp3");
        var client = new BlockingClient();
        var provider = CreateProvider(client, new RecordingComposer());
        using var cancellation = new CancellationTokenSource();

        try
        {
            var synthesis = provider.SynthesizeAsync(
                CreateRequest([NeutralTurn()]),
                outputPath,
                null,
                cancellation.Token);
            await client.Entered.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synthesis);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.EnumerateDirectories(root, "bluemagpie-tts-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BlueMagpieMultiVoiceNarrationProvider CreateProvider(
        IBlueMagpieTtsClient client,
        IFfmpegAudioComposer composer) =>
        new(
            client,
            composer,
            Options.Create(new BlueMagpieOptions
            {
                Enabled = true,
                FormalNarrationEnabled = true,
                InternalToken = new string('t', 32),
                ModelRevision = BlueMagpieOptions.PinnedModelRevision,
            }),
            NullLogger<BlueMagpieMultiVoiceNarrationProvider>.Instance);

    private static MultiVoiceNarrationRequest CreateRequest(
        IReadOnlyList<NarrationTurn> turns,
        IReadOnlyList<NarrationProviderContract>? characterContracts = null) =>
        new(
            turns,
            new NarrationProviderContract(
                BlueMagpieOptions.ProviderName,
                BlueMagpieMultiVoiceNarrationProvider.PinnedProviderVersion),
            characterContracts ??
            [
                new NarrationProviderContract(
                    BlueMagpieOptions.ProviderName,
                    BlueMagpieMultiVoiceNarrationProvider.PinnedProviderVersion),
            ]);

    private static NarrationTurn NeutralTurn() =>
        new(
            "你好",
            BlueMagpieOptions.FemaleVoice,
            "+0%",
            "+0Hz",
            "+0%",
            0);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storyvoice-bluemagpie-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingClient : IBlueMagpieTtsClient
    {
        public List<(string Text, string Voice)> Requests { get; } = [];
        public Exception? Failure { get; init; }
        public string ReturnedRevision { get; init; } = BlueMagpieOptions.PinnedModelRevision;
        public string ReturnedProviderVersion { get; init; } = BlueMagpieOptions.PinnedProviderVersion;

        public Task<BlueMagpieSynthesisResult> SynthesizeAsync(
            string text,
            string voice,
            CancellationToken cancellationToken)
        {
            Requests.Add((text, voice));
            if (Failure is not null)
            {
                return Task.FromException<BlueMagpieSynthesisResult>(Failure);
            }

            return Task.FromResult(new BlueMagpieSynthesisResult(
                Encoding.ASCII.GetBytes("RIFF-mock"),
                "audio/wav",
                ReturnedRevision,
                ReturnedProviderVersion,
                voice));
        }
    }

    private sealed class BlockingClient : IBlueMagpieTtsClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BlueMagpieSynthesisResult> SynthesizeAsync(
            string text,
            string voice,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking test client unexpectedly resumed.");
        }
    }

    private sealed class RecordingComposer : IFfmpegAudioComposer
    {
        public IReadOnlyList<FfmpegAudioSegment> Segments { get; private set; } = [];
        public int OutputSampleRate { get; private set; }

        public async Task ComposeAsync(
            IReadOnlyList<FfmpegAudioSegment> segments,
            string outputPath,
            int outputSampleRate,
            CancellationToken cancellationToken)
        {
            Segments = segments.ToArray();
            OutputSampleRate = outputSampleRate;
            Assert.All(Segments, segment => Assert.True(File.Exists(segment.InputWavPath)));
            await File.WriteAllBytesAsync(
                outputPath,
                Encoding.ASCII.GetBytes("ID3-mock"),
                cancellationToken);
        }
    }
}
