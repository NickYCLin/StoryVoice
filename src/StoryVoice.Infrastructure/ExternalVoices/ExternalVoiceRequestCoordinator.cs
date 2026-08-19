using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using StoryVoice.Application.ExternalVoices;

namespace StoryVoice.Infrastructure.ExternalVoices;

internal sealed class ExternalVoiceIdempotencyCoordinator(
    IOptions<ExternalVoiceApiOptions> options,
    TimeProvider timeProvider)
{
    private readonly object sync = new();
    private readonly Dictionary<CacheKey, CacheEntry> entries = [];

    public Task<ExternalVoiceAudio> ExecuteAsync(
        string consumerKeyId,
        string idempotencyKey,
        byte[] requestHash,
        Func<Task<ExternalVoiceAudio>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(requestHash);
        ArgumentNullException.ThrowIfNull(factory);

        CacheEntry entry;
        CacheKey key;
        TaskCompletionSource<ExternalVoiceAudio>? completion = null;
        var now = timeProvider.GetUtcNow();
        lock (sync)
        {
            RemoveExpired(now);
            key = new CacheKey(consumerKeyId, idempotencyKey);
            if (entries.TryGetValue(key, out entry!))
            {
                if (!CryptographicOperations.FixedTimeEquals(entry.RequestHash, requestHash))
                {
                    throw new ExternalVoiceSynthesisException(
                        ExternalVoiceSynthesisFailureKind.IdempotencyConflict);
                }
            }
            else
            {
                if (entries.Count >= options.Value.IdempotencyCacheEntries)
                {
                    throw new ExternalVoiceSynthesisException(
                        ExternalVoiceSynthesisFailureKind.RateLimited,
                        CalculateRetryAfterSeconds(now));
                }

                completion = new TaskCompletionSource<ExternalVoiceAudio>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                entry = new CacheEntry(
                    requestHash.ToArray(),
                    completion.Task,
                    DateTimeOffset.MaxValue);
                entries.Add(key, entry);
            }
        }

        if (completion is not null)
        {
            _ = CompleteAsync(key, entry, completion, factory);
        }

        return CloneResultAsync(entry.Task);
    }

    private async Task CompleteAsync(
        CacheKey key,
        CacheEntry entry,
        TaskCompletionSource<ExternalVoiceAudio> completion,
        Func<Task<ExternalVoiceAudio>> factory)
    {
        var removeImmediately = false;
        try
        {
            var audio = await factory().ConfigureAwait(false);
            completion.TrySetResult(Clone(audio));
        }
        catch (ExternalVoiceSynthesisException exception)
            when (exception.FailureKind == ExternalVoiceSynthesisFailureKind.RateLimited)
        {
            // The global single-flight gate rejected this request before any GPU
            // submission. It is safe to retry the same logical request after the
            // advertised delay, so do not pin this pre-commit 429 for the full TTL.
            removeImmediately = true;
            completion.TrySetException(exception);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (sync)
            {
                if (removeImmediately)
                {
                    if (entries.TryGetValue(key, out var current)
                        && ReferenceEquals(current, entry))
                    {
                        entries.Remove(key);
                    }
                }
                else
                {
                    entry.ExpiresAtUtc = timeProvider.GetUtcNow().AddMinutes(
                        options.Value.IdempotencyTtlMinutes);
                }
            }
        }
    }

    private static async Task<ExternalVoiceAudio> CloneResultAsync(
        Task<ExternalVoiceAudio> task) =>
        Clone(await task.ConfigureAwait(false));

    private static ExternalVoiceAudio Clone(ExternalVoiceAudio audio) =>
        new(audio.Content.ToArray(), audio.ContentType);

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var key in entries
                     .Where(candidate => candidate.Value.ExpiresAtUtc <= now)
                     .Select(candidate => candidate.Key)
                     .ToArray())
        {
            entries.Remove(key);
        }
    }

    private int CalculateRetryAfterSeconds(DateTimeOffset now)
    {
        var earliestExpiry = entries.Values
            .Where(entry => entry.ExpiresAtUtc != DateTimeOffset.MaxValue)
            .Select(entry => entry.ExpiresAtUtc)
            .DefaultIfEmpty(now.AddMinutes(1))
            .Min();
        var seconds = Math.Ceiling((earliestExpiry - now).TotalSeconds);
        return (int)Math.Clamp(seconds, 1, 600);
    }

    private readonly record struct CacheKey(
        string ConsumerKeyId,
        string IdempotencyKey);

    private sealed class CacheEntry(
        byte[] requestHash,
        Task<ExternalVoiceAudio> task,
        DateTimeOffset expiresAtUtc)
    {
        public byte[] RequestHash { get; } = requestHash;

        public Task<ExternalVoiceAudio> Task { get; } = task;

        public DateTimeOffset ExpiresAtUtc { get; set; } = expiresAtUtc;
    }
}

internal sealed class ExternalVoiceConcurrencyGate
{
    private int active;

    public IDisposable? TryEnter() =>
        Interlocked.CompareExchange(ref active, 1, 0) == 0
            ? new Lease(this)
            : null;

    private sealed class Lease(ExternalVoiceConcurrencyGate owner) : IDisposable
    {
        private ExternalVoiceConcurrencyGate? current = owner;

        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref current, null);
            if (gate is not null)
            {
                Volatile.Write(ref gate.active, 0);
            }
        }
    }
}
