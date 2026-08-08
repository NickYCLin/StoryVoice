using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StoryVoice.Application.Books;
using StoryVoice.Application.Narrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Worker;

public sealed class StoryPipelineWorker(
    IServiceScopeFactory scopeFactory,
    INarrationProvider provider,
    IOptions<NarrationOptions> options,
    ILogger<StoryPipelineWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"{Environment.ProcessId}:{Guid.NewGuid():N}";
    private DateTimeOffset _nextRecoveryAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("StoryVoice narration worker {WorkerId} is ready", _workerId);
        Directory.CreateDirectory(options.Value.AudioRootPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claim = await ClaimNextAsync(stoppingToken);
                if (claim is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await ProcessAsync(claim, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Narration worker loop failed");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task<ClaimedJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (now >= _nextRecoveryAt)
        {
            await db.NarrationJobs
                .Where(job => job.Status == NarrationJobStatus.Running
                    && job.LeaseExpiresAt != null
                    && job.LeaseExpiresAt <= now
                    && job.CancellationRequested)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.Status, NarrationJobStatus.Cancelled)
                    .SetProperty(job => job.ProgressPercent, 0)
                    .SetProperty(job => job.LeaseOwner, (string?)null)
                    .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.ErrorCode, (string?)null)
                    .SetProperty(job => job.UpdatedAt, now)
                    .SetProperty(job => job.ConcurrencyStamp, Guid.NewGuid()),
                    cancellationToken);
            await db.NarrationJobs
                .Where(job => job.Status == NarrationJobStatus.Running
                    && job.LeaseExpiresAt != null
                    && job.LeaseExpiresAt <= now
                    && !job.CancellationRequested
                    && job.Attempts >= options.Value.MaxAttempts)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.Status, NarrationJobStatus.Failed)
                    .SetProperty(job => job.ProgressPercent, 0)
                    .SetProperty(job => job.LeaseOwner, (string?)null)
                    .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(job => job.ErrorCode, "worker_lease_expired")
                    .SetProperty(job => job.UpdatedAt, now)
                    .SetProperty(job => job.ConcurrencyStamp, Guid.NewGuid()),
                    cancellationToken);
            _nextRecoveryAt = now.AddSeconds(30);
        }

        var candidateId = await db.NarrationJobs
            .AsNoTracking()
            .Where(job => !job.CancellationRequested
                && job.Attempts < options.Value.MaxAttempts
                && ((job.Status == NarrationJobStatus.Queued
                        && (job.NextAttemptAt == null || job.NextAttemptAt <= now))
                    || (job.Status == NarrationJobStatus.Running
                        && job.LeaseExpiresAt != null
                        && job.LeaseExpiresAt <= now)))
            .OrderBy(job => job.NextAttemptAt)
            .ThenBy(job => job.CreatedAt)
            .Select(job => (Guid?)job.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidateId is null)
        {
            return null;
        }

        var artifactToken = Guid.NewGuid().ToString("N");
        var leaseOwner = $"{_workerId}:{artifactToken}";
        var leaseExpiresAt = now.AddMinutes(options.Value.LeaseMinutes);
        var affected = await db.NarrationJobs
            .Where(job => job.Id == candidateId
                && !job.CancellationRequested
                && job.Attempts < options.Value.MaxAttempts
                && ((job.Status == NarrationJobStatus.Queued
                        && (job.NextAttemptAt == null || job.NextAttemptAt <= now))
                    || (job.Status == NarrationJobStatus.Running
                        && job.LeaseExpiresAt != null
                        && job.LeaseExpiresAt <= now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, NarrationJobStatus.Running)
                .SetProperty(job => job.ProgressPercent, 10)
                .SetProperty(job => job.Attempts, job => job.Attempts + 1)
                .SetProperty(job => job.LeaseOwner, leaseOwner)
                .SetProperty(job => job.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(job => job.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(job => job.ErrorCode, (string?)null)
                .SetProperty(job => job.UpdatedAt, now)
                .SetProperty(job => job.ConcurrencyStamp, Guid.NewGuid()),
                cancellationToken);
        return affected == 1
            ? new ClaimedJob(candidateId.Value, leaseOwner, artifactToken)
            : null;
    }

    private async Task ProcessAsync(ClaimedJob claim, CancellationToken stoppingToken)
    {
        string? temporaryPath = null;
        string? uncommittedAudioPath = null;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var job = await db.NarrationJobs
                .AsNoTracking()
                .Where(item => item.Id == claim.JobId
                    && item.Status == NarrationJobStatus.Running
                    && item.LeaseOwner == claim.LeaseOwner)
                .Select(item => new
                {
                    item.Id,
                    item.OwnerId,
                    item.ContentBookId,
                    item.SourceHash,
                    item.Voice,
                    item.Rate,
                    item.CancellationRequested
                })
                .SingleOrDefaultAsync(stoppingToken);
            if (job is null)
            {
                return;
            }

            if (job.CancellationRequested)
            {
                await RecordFailureAsync(claim, "cancelled", CancellationToken.None);
                return;
            }

            var content = await db.Books
                .AsNoTracking()
                .Include(book => book.Chapters)
                .SingleOrDefaultAsync(
                    book => book.Id == job.ContentBookId && book.OwnerId == job.OwnerId,
                    stoppingToken);
            if (!AuthorizedTextPolicy.IsProcessable(content))
            {
                throw new InvalidOperationException("narration_source_unavailable");
            }

            var source = NarrationSource.Create(content!.Chapters.Select(chapter =>
                new NarrationChapterSource(chapter.Id, chapter.SortOrder, chapter.Title, chapter.OriginalText)));
            if (!string.Equals(source.SourceHash, job.SourceHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("narration_source_changed");
            }

            var relativePath = Path.Combine(
                job.OwnerId.ToString("N"),
                job.Id.ToString("N"),
                $"{claim.ArtifactToken}.mp3");
            var absolutePath = Path.Combine(options.Value.AudioRootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            temporaryPath = absolutePath + $".tmp-{Guid.NewGuid():N}";

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromMinutes(options.Value.ProviderTimeoutMinutes));
            using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                timeout.Token);
            var cancellationMonitor = MonitorCancellationAsync(claim, providerCancellation, stoppingToken);
            try
            {
                await provider.SynthesizeAsync(
                    source.Text,
                    temporaryPath,
                    job.Voice,
                    job.Rate,
                    providerCancellation.Token);
            }
            finally
            {
                providerCancellation.Cancel();
                await IgnoreCancellationAsync(cancellationMonitor);
            }

            if (!File.Exists(temporaryPath))
            {
                throw new InvalidOperationException("provider_empty_audio");
            }

            var audioBytes = new FileInfo(temporaryPath).Length;
            if (audioBytes < 1)
            {
                throw new InvalidOperationException("provider_empty_audio");
            }

            File.Move(temporaryPath, absolutePath, overwrite: false);
            temporaryPath = null;
            uncommittedAudioPath = absolutePath;
            var completedAt = DateTimeOffset.UtcNow;
            var persistedRelativePath = relativePath.Replace('\\', '/');
            int affected;
            try
            {
                affected = await db.NarrationJobs
                    .Where(item => item.Id == claim.JobId
                        && item.Status == NarrationJobStatus.Running
                        && item.LeaseOwner == claim.LeaseOwner
                        && !item.CancellationRequested)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, NarrationJobStatus.Completed)
                        .SetProperty(item => item.ProgressPercent, 100)
                        .SetProperty(item => item.AudioRelativePath, persistedRelativePath)
                        .SetProperty(item => item.AudioBytes, audioBytes)
                        .SetProperty(item => item.LeaseOwner, (string?)null)
                        .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                        .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                        .SetProperty(item => item.CompletedAt, completedAt)
                        .SetProperty(item => item.UpdatedAt, completedAt)
                        .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid()),
                        stoppingToken);
            }
            catch (Exception completionException)
            {
                var completionConfirmed = await VerifyCompletedArtifactAsync(
                    claim.JobId,
                    persistedRelativePath,
                    audioBytes);
                var outcome = NarrationCompletionOutcomeResolver.ResolveAfterAmbiguousCommand(completionConfirmed);
                if (outcome.AcceptCompletion)
                {
                    uncommittedAudioPath = null;
                    logger.LogWarning(
                        completionException,
                        "Narration job {JobId} completion was committed despite an ambiguous database response",
                        claim.JobId);
                    return;
                }

                if (!outcome.DeleteCandidateArtifact)
                {
                    // Preserve the uniquely named artifact when durable state cannot be re-read.
                    // Deleting it here could leave a committed Completed row pointing at a missing file.
                    uncommittedAudioPath = null;
                }

                throw;
            }

            if (affected != 1)
            {
                await RecordFailureAsync(claim, "cancelled", CancellationToken.None);
                return;
            }

            uncommittedAudioPath = null;
            logger.LogInformation(
                "Narration job {JobId} completed with {AudioBytes} bytes",
                claim.JobId,
                audioBytes);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await RecordFailureAsync(claim, "provider_timeout", CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Narration job {JobId} failed", claim.JobId);
            var errorCode = exception.Message is "narration_source_unavailable" or "narration_source_changed"
                ? exception.Message
                : "provider_failed";
            await RecordFailureAsync(
                claim,
                errorCode,
                CancellationToken.None,
                permanent: errorCode is "narration_source_unavailable" or "narration_source_changed");
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(uncommittedAudioPath);
        }
    }

    private async Task MonitorCancellationAsync(
        ClaimedJob claim,
        CancellationTokenSource providerCancellation,
        CancellationToken stoppingToken)
    {
        var renewEvery = TimeSpan.FromMinutes(Math.Max(1, options.Value.LeaseMinutes / 3d));
        var nextRenewalAt = DateTimeOffset.UtcNow.Add(renewEvery);
        while (!providerCancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var state = await db.NarrationJobs
                .AsNoTracking()
                .Where(job => job.Id == claim.JobId)
                .Select(job => new { job.Status, job.CancellationRequested, job.LeaseOwner })
                .SingleOrDefaultAsync(stoppingToken);
            if (state is null
                || state.Status != NarrationJobStatus.Running
                || state.CancellationRequested
                || !string.Equals(state.LeaseOwner, claim.LeaseOwner, StringComparison.Ordinal))
            {
                providerCancellation.Cancel();
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= nextRenewalAt)
            {
                var renewed = await db.NarrationJobs
                    .Where(job => job.Id == claim.JobId
                        && job.Status == NarrationJobStatus.Running
                        && job.LeaseOwner == claim.LeaseOwner
                        && !job.CancellationRequested)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(job => job.LeaseExpiresAt, now.AddMinutes(options.Value.LeaseMinutes))
                        .SetProperty(job => job.UpdatedAt, now)
                        .SetProperty(job => job.ConcurrencyStamp, Guid.NewGuid()),
                        stoppingToken);
                if (renewed != 1)
                {
                    providerCancellation.Cancel();
                    return;
                }

                nextRenewalAt = now.Add(renewEvery);
            }
        }
    }

    private async Task<bool?> VerifyCompletedArtifactAsync(
        Guid jobId,
        string relativePath,
        long audioBytes)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            return await db.NarrationJobs
                .AsNoTracking()
                .AnyAsync(job => job.Id == jobId
                    && job.Status == NarrationJobStatus.Completed
                    && job.AudioRelativePath == relativePath
                    && job.AudioBytes == audioBytes,
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not resolve ambiguous completion state for narration job {JobId}; preserving artifact",
                jobId);
            return null;
        }
    }

    private async Task RecordFailureAsync(
        ClaimedJob claim,
        string errorCode,
        CancellationToken cancellationToken,
        bool permanent = false)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
        var state = await db.NarrationJobs
            .AsNoTracking()
            .Where(job => job.Id == claim.JobId
                && job.Status == NarrationJobStatus.Running
                && job.LeaseOwner == claim.LeaseOwner)
            .Select(job => new { job.Attempts, job.CancellationRequested })
            .SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (state.CancellationRequested)
        {
            await FinalizeCancellationAsync(db, claim, now, cancellationToken);
            return;
        }

        var finalFailure = permanent || state.Attempts >= options.Value.MaxAttempts;
        var nextAttemptAt = finalFailure
            ? (DateTimeOffset?)null
            : now.AddSeconds(Math.Min(30, Math.Pow(2, state.Attempts)));
        var failureUpdated = await db.NarrationJobs
            .Where(job => job.Id == claim.JobId
                && job.Status == NarrationJobStatus.Running
                && job.LeaseOwner == claim.LeaseOwner
                && !job.CancellationRequested)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    job => job.Status,
                    finalFailure ? NarrationJobStatus.Failed : NarrationJobStatus.Queued)
                .SetProperty(job => job.ProgressPercent, 0)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(job => job.NextAttemptAt, nextAttemptAt)
                .SetProperty(job => job.ErrorCode, errorCode)
                .SetProperty(job => job.UpdatedAt, now)
                .SetProperty(job => job.ConcurrencyStamp, Guid.NewGuid()),
                cancellationToken);
        if (failureUpdated != 1)
        {
            // Cancellation can commit after the state read above. Finalize it immediately
            // rather than waiting for the lease-recovery sweep.
            await FinalizeCancellationAsync(db, claim, DateTimeOffset.UtcNow, cancellationToken);
        }
    }

    private static Task<int> FinalizeCancellationAsync(
        StoryVoiceDbContext db,
        ClaimedJob claim,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.NarrationJobs
            .Where(job => job.Id == claim.JobId
                && job.Status == NarrationJobStatus.Running
                && job.LeaseOwner == claim.LeaseOwner
                && job.CancellationRequested)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, NarrationJobStatus.Cancelled)
                .SetProperty(job => job.ProgressPercent, 0)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(job => job.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(job => job.ErrorCode, (string?)null)
                .SetProperty(job => job.UpdatedAt, now)
                .SetProperty(job => job.ConcurrencyStamp, Guid.NewGuid()),
                cancellationToken);

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ClaimedJob(Guid JobId, string LeaseOwner, string ArtifactToken);
}
