using StoryVoice.Domain.Narrations;

namespace StoryVoice.UnitTests;

public sealed class NarrationJobTests
{
    [Fact]
    public void Job_lifecycle_is_idempotent_lease_aware_and_cancellable()
    {
        var ownerId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var contentBookId = Guid.NewGuid();
        var attestedAt = DateTimeOffset.UtcNow;
        var job = NarrationJob.Create(
            ownerId,
            bookId,
            contentBookId,
            "source-hash",
            "zh-TW-YunJheNeural",
            "-5%",
            attestedAt);

        Assert.Equal(NarrationJobStatus.Queued, job.Status);
        Assert.Equal(NarrationMode.SingleVoice, job.Mode);
        Assert.Equal(attestedAt, job.RightsAttestedAt);
        Assert.Equal(0, job.Attempts);

        var leaseUntil = DateTimeOffset.UtcNow.AddMinutes(20);
        job.Claim("worker-a", leaseUntil);
        Assert.Equal(NarrationJobStatus.Running, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(10, job.ProgressPercent);
        Assert.Throws<InvalidOperationException>(() => job.Claim("worker-b", leaseUntil));

        job.RequestCancellation();
        Assert.True(job.CancellationRequested);
        job.Cancel();
        Assert.Equal(NarrationJobStatus.Cancelled, job.Status);
        Assert.Equal(0, job.ProgressPercent);
    }

    [Fact]
    public void Failed_job_requeues_within_retry_budget_then_stops()
    {
        var job = NarrationJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "source-hash",
            "zh-TW-YunJheNeural",
            "-5%",
            DateTimeOffset.UtcNow);

        var now = DateTimeOffset.UtcNow;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            job.Claim($"worker-{attempt}", now.AddMinutes(20), now);
            job.FailOrRequeue("provider_failed", maxAttempts: 3, failedAt: now);
            Assert.Equal(attempt < 3 ? NarrationJobStatus.Queued : NarrationJobStatus.Failed, job.Status);
            if (attempt < 3)
            {
                Assert.NotNull(job.NextAttemptAt);
                Assert.True(job.NextAttemptAt > now);
                now = job.NextAttemptAt.Value;
            }
        }
    }

    [Fact]
    public void Permanent_source_failure_does_not_retry()
    {
        var job = NarrationJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('b', 64),
            "zh-TW-YunJheNeural",
            "-5%",
            DateTimeOffset.UtcNow);

        job.Claim("worker-1", DateTimeOffset.UtcNow.AddMinutes(20));
        job.FailPermanently("narration_source_changed");

        Assert.Equal(NarrationJobStatus.Failed, job.Status);
        Assert.Equal("narration_source_changed", job.ErrorCode);
        Assert.Null(job.NextAttemptAt);
    }

    [Fact]
    public void Completion_records_only_private_relative_audio_metadata()
    {
        var job = NarrationJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "source-hash",
            "zh-TW-YunJheNeural",
            "-5%",
            DateTimeOffset.UtcNow);
        job.Claim("worker-a", DateTimeOffset.UtcNow.AddMinutes(20));

        job.Complete("owner/job.mp3", 1234);

        Assert.Equal(NarrationJobStatus.Completed, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Equal("owner/job.mp3", job.AudioRelativePath);
        Assert.Equal(1234, job.AudioBytes);
        Assert.Null(job.LeaseOwner);
    }
}
