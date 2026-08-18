namespace StoryVoice.Infrastructure.Persistence;

/// <summary>
/// Serializes character/voice mutations inside one process (including the EF InMemory test host).
/// PostgreSQL callers additionally take the character row lock, which extends the same ordering
/// across API instances.
/// </summary>
internal static class CharacterVoiceMutationCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<MutationKey, LockEntry> Entries = [];

    public static async ValueTask<IAsyncDisposable> AcquireAsync(
        Guid ownerId,
        Guid characterProfileId,
        CancellationToken cancellationToken)
    {
        var key = new MutationKey(ownerId, characterProfileId);
        LockEntry entry;
        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out entry!))
            {
                entry = new LockEntry();
                Entries.Add(key, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private static void Release(MutationKey key, LockEntry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private static void ReleaseReference(MutationKey key, LockEntry entry)
    {
        lock (Sync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                Entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private readonly record struct MutationKey(Guid OwnerId, Guid CharacterProfileId);

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(initialCount: 1, maxCount: 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class Releaser(MutationKey key, LockEntry entry) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                Release(key, entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
