using System.Reflection;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.IntegrationTests;

internal static class NarrationArtifactTestData
{
    private static readonly MethodInfo CreateMultiCharacterStaged =
        typeof(NarrationJob).GetMethod(
            "CreateMultiCharacterStaged",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The staged narration factory is unavailable.");

    private static readonly MethodInfo MarkHistoricalMethod =
        typeof(NarrationJob).GetMethod(
            "MarkHistorical",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The historical narration transition is unavailable.");

    public static NarrationJob CompletedStaged(Guid ownerId, Guid bookId, Guid contentBookId)
    {
        var job = QueuedStaged(ownerId, bookId, contentBookId);
        Complete(job, ownerId, job.NextAttemptAt ?? DateTimeOffset.UtcNow);
        return job;
    }

    public static NarrationJob QueuedStaged(Guid ownerId, Guid bookId, Guid contentBookId)
    {
        var now = DateTimeOffset.UtcNow;
        return (NarrationJob)(CreateMultiCharacterStaged.Invoke(
            null,
            [
                ownerId,
                bookId,
                contentBookId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                $"staged-{Guid.NewGuid():N}",
                now
            ]) ?? throw new InvalidOperationException("The staged narration factory returned null."));
    }

    public static NarrationJob CompletedHistorical(Guid ownerId, Guid bookId, Guid contentBookId)
    {
        var now = DateTimeOffset.UtcNow;
        var job = NarrationJob.Create(
            ownerId,
            bookId,
            contentBookId,
            $"historical-{Guid.NewGuid():N}",
            "historical-test-voice",
            "historical-test-rate",
            now);
        Complete(job, ownerId, now);
        MarkHistorical(job);
        return job;
    }

    public static void MarkHistorical(NarrationJob job) =>
        MarkHistoricalMethod.Invoke(job, [job.UpdatedAt]);

    public static async Task WriteAudioAsync(
        string storageRoot,
        NarrationJob job,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.AudioRelativePath))
        {
            throw new InvalidOperationException("The narration artifact has no audio path.");
        }

        var absolutePath = Path.Combine(storageRoot, "audio", job.AudioRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, [1, 2, 3, 4], cancellationToken);
    }

    private static void Complete(NarrationJob job, Guid ownerId, DateTimeOffset now)
    {
        var claimedAt = job.NextAttemptAt ?? now;
        job.Claim($"visibility-test:{Guid.NewGuid():N}", claimedAt.AddMinutes(5), claimedAt);
        job.Complete(Path.Combine(ownerId.ToString("N"), $"{job.Id:N}.mp3"), 4);
    }
}
