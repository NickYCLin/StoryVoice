namespace StoryVoice.Worker;

public sealed record BlueMagpieChunkCacheRequest(
    int Ordinal,
    string Text,
    string Voice,
    string Rate,
    string Pitch,
    string Volume,
    int PauseBeforeMs,
    string ProviderVersion,
    string ModelRevision);

public sealed record BlueMagpieChunkCacheEntry(
    string InputWavPath,
    long AudioBytes,
    bool CacheHit);

public interface IBlueMagpieChunkCacheScope : IAsyncDisposable
{
    Task<BlueMagpieChunkCacheEntry> GetOrCreateAsync(
        BlueMagpieChunkCacheRequest request,
        Func<CancellationToken, Task<byte[]>> createAudio,
        CancellationToken cancellationToken);
}

public interface IBlueMagpieChunkCache
{
    Task<IBlueMagpieChunkCacheScope> OpenScopeAsync(
        NarrationSynthesisCacheContext context,
        CancellationToken cancellationToken);

    Task CleanupAsync(CancellationToken cancellationToken);
}

public sealed class BlueMagpieChunkCacheCapacityException(string message) : IOException(message);
