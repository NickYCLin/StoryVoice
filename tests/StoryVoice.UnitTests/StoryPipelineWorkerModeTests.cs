using System.Runtime.CompilerServices;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class StoryPipelineWorkerModeTests
{
    [Fact]
    public void Worker_supports_both_single_voice_and_multi_character_jobs()
    {
        // Task 0 shipped a compatibility worker that only claimed SingleVoice jobs so a rolling
        // deploy could never have an old worker claim a MultiCharacter row it didn't understand.
        // Task 9 is the deliberate, forward-only widening past that boundary — this test asserts
        // the new supported set, it does not preserve the old restriction.
        var singleVoice = CreateJob();
        var multiCharacter = CreateJob();
        typeof(NarrationJob)
            .GetProperty(nameof(NarrationJob.Mode))!
            .SetValue(multiCharacter, NarrationMode.MultiCharacter);

        var supported = StoryPipelineWorker
            .SupportedJobs(new[] { singleVoice, multiCharacter }.AsQueryable())
            .ToArray();

        Assert.Equal(2, supported.Length);
        Assert.Contains(singleVoice, supported);
        Assert.Contains(multiCharacter, supported);
    }

    [Fact]
    public void Every_worker_narration_job_query_is_routed_through_the_supported_mode_filter()
    {
        var source = File.ReadAllLines(GetWorkerSourcePath());
        var jobAccesses = source
            .Where(line => line.Contains("db.NarrationJobs", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(jobAccesses);
        Assert.All(
            jobAccesses,
            line => Assert.Contains("SupportedJobs(db.NarrationJobs)", line, StringComparison.Ordinal));
        Assert.Contains(
            source,
            line => line.Contains(
                "jobs.Where(job => job.Mode == NarrationMode.SingleVoice || job.Mode == NarrationMode.MultiCharacter)",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Worker_copies_narrator_and_every_character_provider_contract_into_the_dispatch_request()
    {
        var ownerId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var assignment = NarrationCastAssignment.Create(
            Guid.NewGuid(),
            ownerId,
            seriesId,
            revisionId,
            Guid.NewGuid(),
            "角色甲",
            "bluemagpie",
            BlueMagpieMultiVoiceNarrationProvider.PinnedProviderVersion,
            "female_voice",
            "+0%",
            "+0Hz",
            "+0%");
        var revision = NarrationCastRevision.Create(
            revisionId,
            ownerId,
            seriesId,
            1,
            "bluemagpie",
            BlueMagpieMultiVoiceNarrationProvider.PinnedProviderVersion,
            "hung_yi_lee",
            "+0%",
            "+0Hz",
            "+0%",
            200,
            800,
            "bluemagpie-pcm16-concat-v1",
            "wav-48khz-mono-to-mp3-concat-v1",
            DateTimeOffset.UtcNow,
            [assignment]);
        NarrationTurn[] turns =
        [
            new("你好", "female_voice", "+0%", "+0Hz", "+0%", 0),
        ];
        var cacheContext = new NarrationSynthesisCacheContext(
            ownerId,
            Guid.NewGuid(),
            "source-hash",
            revisionId,
            revision.Fingerprint,
            new string('a', 64),
            revision.CompositionVersion,
            revision.FfmpegProfile);

        var request = StoryPipelineWorker.CreateMultiVoiceNarrationRequest(
            revision,
            turns,
            cacheContext);

        Assert.Same(turns, request.Turns);
        Assert.Equal(
            new NarrationProviderContract(
                "bluemagpie",
                BlueMagpieMultiVoiceNarrationProvider.PinnedProviderVersion),
            request.NarratorProvider);
        Assert.Equal(
            new NarrationProviderContract(
                "bluemagpie",
                BlueMagpieMultiVoiceNarrationProvider.PinnedProviderVersion),
            Assert.Single(request.CharacterProviders!));
        Assert.Same(cacheContext, request.CacheContext);
    }

    [Theory]
    [InlineData("bluemagpie", true)]
    [InlineData("BLUEMAGPIE", true)]
    [InlineData("edge", false)]
    [InlineData("voai", false)]
    [InlineData(null, false)]
    public void Only_the_durable_local_provider_is_requeued_during_graceful_worker_stop(
        string? providerName,
        bool expected)
    {
        Assert.Equal(
            expected,
            StoryPipelineWorker.CanResumeAfterWorkerStop(providerName));
    }

    [Fact]
    public void Graceful_BlueMagpie_restart_waits_beyond_queue_and_the_longest_GPU_lease()
    {
        var blueOptions = new BlueMagpieOptions
        {
            QueueTimeoutSeconds = 15,
            SynthesisWatchdogSeconds = 120,
            ModelLifecycleWatchdogSeconds = 300,
        };
        var expectedCooldown = TimeSpan.FromSeconds(405);
        var now = DateTimeOffset.Parse("2026-08-15T12:00:00+00:00");

        Assert.Equal(expectedCooldown, blueOptions.RestartCooldown);
        Assert.Equal(
            now.Add(expectedCooldown),
            StoryPipelineWorker.CalculateNextAttemptAt(
                now,
                attempts: 1,
                finalFailure: false,
                blueOptions.RestartCooldown));
        Assert.Null(StoryPipelineWorker.CalculateNextAttemptAt(
            now,
            attempts: 3,
            finalFailure: true,
            blueOptions.RestartCooldown));
    }

    private static NarrationJob CreateJob() =>
        NarrationJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "source-hash",
            "zh-TW-YunJheNeural",
            "-5%",
            DateTimeOffset.UtcNow);

    private static string GetWorkerSourcePath([CallerFilePath] string testSourcePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testSourcePath)!,
            "..",
            "..",
            "src",
            "StoryVoice.Worker",
            "StoryPipelineWorker.cs"));
}
