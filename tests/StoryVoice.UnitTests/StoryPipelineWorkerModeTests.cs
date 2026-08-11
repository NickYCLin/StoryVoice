using System.Runtime.CompilerServices;
using StoryVoice.Domain.Narrations;
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