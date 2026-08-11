using System.Runtime.CompilerServices;
using StoryVoice.Domain.Narrations;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class StoryPipelineWorkerModeTests
{
    [Fact]
    public void Compatibility_worker_supports_only_single_voice_jobs()
    {
        var singleVoice = CreateJob();
        var multiCharacter = CreateJob();
        typeof(NarrationJob)
            .GetProperty(nameof(NarrationJob.Mode))!
            .SetValue(multiCharacter, NarrationMode.MultiCharacter);

        var supported = StoryPipelineWorker
            .SupportedJobs(new[] { singleVoice, multiCharacter }.AsQueryable())
            .ToArray();

        Assert.Single(supported);
        Assert.Same(singleVoice, supported[0]);
        Assert.Equal(NarrationMode.MultiCharacter, multiCharacter.Mode);
    }

    [Fact]
    public void Every_worker_job_query_is_routed_through_the_supported_mode_filter()
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
                "jobs.Where(job => job.Mode == NarrationMode.SingleVoice)",
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