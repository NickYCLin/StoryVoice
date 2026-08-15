using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Globalization;

namespace StoryVoice.Infrastructure.Insights;

/// <summary>
/// Serializes work that can occupy the host-local GPU. Production uses a Redis-backed lease so
/// API replicas agree on the same owner; tests use an in-process semaphore.
/// </summary>
public interface ILocalGpuExecutionGate
{
    Task<ILocalGpuExecutionLease> AcquireAsync(CancellationToken cancellationToken);

    Task<ILocalGpuExecutionLease?> TryAcquireAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents ownership of the local GPU. Call <see cref="Abandon"/> when the caller cannot prove
/// that the GPU executor stopped; Redis then keeps the lock until its safety TTL instead of deleting it.
/// </summary>
public interface ILocalGpuExecutionLease : IAsyncDisposable
{
    CancellationToken OwnershipLost { get; }

    void Abandon();
}

public sealed class LocalGpuExecutionGateOptions
{
    public const string SectionName = "LocalGpuExecutionGate";
    public const int MinimumLeaseSeconds = 1_900;

    // Covers the maximum allowed 1,800-second Ollama request, the maximum 60-second unload,
    // and an additional safety margin for response processing and scheduling delays.
    public int LeaseSeconds { get; set; } = 2_100;

    public int PollIntervalMilliseconds { get; set; } = 200;

    public int RenewIntervalSeconds { get; set; } = 30;
}

public sealed class InProcessLocalGpuExecutionGate : ILocalGpuExecutionGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<ILocalGpuExecutionLease> AcquireAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    public async Task<ILocalGpuExecutionLease?> TryAcquireAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        if (waitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTimeout));
        }

        return await semaphore.WaitAsync(waitTimeout, cancellationToken)
            ? new Lease(semaphore)
            : null;
    }

    private sealed class Lease(SemaphoreSlim semaphore) : ILocalGpuExecutionLease
    {
        private int disposed;

        public CancellationToken OwnershipLost => CancellationToken.None;

        public void Abandon()
        {
            // Testing has no external executor. Always releasing the in-process semaphore on dispose
            // prevents a failed unit/integration test from permanently poisoning the test host.
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class RedisLocalGpuExecutionGate : ILocalGpuExecutionGate
{
    public const string LockKey = "storyvoice:local-gpu:exclusive:v1";

    private readonly IRedisLocalGpuLockStore store;
    private readonly TimeSpan leaseTtl;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan renewInterval;

    public RedisLocalGpuExecutionGate(
        IConnectionMultiplexer redis,
        IOptions<LocalGpuExecutionGateOptions> options)
        : this(new RedisLocalGpuLockStore(redis), options)
    {
    }

    internal RedisLocalGpuExecutionGate(
        IRedisLocalGpuLockStore store,
        IOptions<LocalGpuExecutionGateOptions> options,
        TimeSpan? renewIntervalOverride = null)
    {
        this.store = store;
        leaseTtl = TimeSpan.FromSeconds(options.Value.LeaseSeconds);
        pollInterval = TimeSpan.FromMilliseconds(options.Value.PollIntervalMilliseconds);
        renewInterval = renewIntervalOverride
            ?? TimeSpan.FromSeconds(options.Value.RenewIntervalSeconds);
    }

    public async Task<ILocalGpuExecutionLease> AcquireAsync(CancellationToken cancellationToken) =>
        await AcquireCoreAsync(waitTimeout: null, cancellationToken)
            ?? throw new InvalidOperationException("An infinite GPU lock wait cannot time out.");

    public Task<ILocalGpuExecutionLease?> TryAcquireAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        if (waitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTimeout));
        }

        return AcquireCoreAsync(waitTimeout, cancellationToken);
    }

    private async Task<ILocalGpuExecutionLease?> AcquireCoreAsync(
        TimeSpan? waitTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = waitTimeout is null
            ? null
            : new CancellationTokenSource(waitTimeout.Value);
        using var linked = timeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var acquisitionToken = linked?.Token ?? cancellationToken;
        var owner = Guid.NewGuid().ToString("N");

        try
        {
            while (true)
            {
                acquisitionToken.ThrowIfCancellationRequested();
                var pendingAcquisition = store.TryAcquireAsync(LockKey, owner, leaseTtl);
                bool acquired;
                try
                {
                    acquired = await pendingAcquisition.WaitAsync(acquisitionToken);
                }
                catch (OperationCanceledException) when (acquisitionToken.IsCancellationRequested)
                {
                    await CleanupLateAcquisitionAsync(pendingAcquisition, owner);
                    acquisitionToken.ThrowIfCancellationRequested();
                    throw;
                }
                catch (Exception)
                {
                    // A client-side Redis fault is not proof that the server rejected SET. An
                    // owner-safe delete issued after observing the task also cleans up an SET whose
                    // response was lost.
                    await CleanupLateAcquisitionAsync(pendingAcquisition, owner);
                    throw;
                }

                // Close the small race where cancellation arrives as SET completes. Returning a
                // timeout without cleaning a successful SET would leave an ownerless 35-minute key.
                if (acquisitionToken.IsCancellationRequested)
                {
                    if (acquired)
                    {
                        await TryReleaseOwnedAsync(owner);
                    }

                    acquisitionToken.ThrowIfCancellationRequested();
                }

                if (acquired)
                {
                    return new RedisLease(store, owner, leaseTtl, renewInterval);
                }

                await Task.Delay(pollInterval, acquisitionToken);
            }
        }
        catch (OperationCanceledException) when (
            timeout?.IsCancellationRequested == true
            && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task CleanupLateAcquisitionAsync(Task<bool> pendingAcquisition, string owner)
    {
        try
        {
            await pendingAcquisition;
        }
        catch (Exception)
        {
            // Observe the terminal failure, then still compare-delete because a client-side error
            // is not proof that Redis did not execute the already-sent SET.
        }

        await TryReleaseOwnedAsync(owner);
    }

    private async Task TryReleaseOwnedAsync(string owner)
    {
        try
        {
            await store.ReleaseIfOwnedAsync(LockKey, owner);
        }
        catch (Exception)
        {
            // Owner-safe TTL fallback; never issue an unconditional delete.
        }
    }

    private sealed class RedisLease : ILocalGpuExecutionLease
    {
        private readonly CancellationTokenSource heartbeatStop = new();
        private readonly CancellationTokenSource ownershipLost = new();
        private readonly IRedisLocalGpuLockStore store;
        private readonly string owner;
        private readonly Task heartbeat;
        private int abandoned;
        private int disposed;

        public RedisLease(
            IRedisLocalGpuLockStore store,
            string owner,
            TimeSpan leaseTtl,
            TimeSpan renewInterval)
        {
            this.store = store;
            this.owner = owner;
            heartbeat = RunHeartbeatAsync(
                store,
                owner,
                leaseTtl,
                renewInterval,
                heartbeatStop,
                ownershipLost);
        }

        public CancellationToken OwnershipLost => ownershipLost.Token;

        public void Abandon()
        {
            Interlocked.Exchange(ref abandoned, 1);
            heartbeatStop.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            heartbeatStop.Cancel();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested)
            {
            }

            if (Volatile.Read(ref abandoned) == 0)
            {
                try
                {
                    await store.ReleaseIfOwnedAsync(LockKey, owner);
                }
                catch (Exception)
                {
                    // Fail closed: a Redis failure leaves the expiring key in place. Never issue an
                    // unconditional delete, because the TTL may have elapsed and a new owner may exist.
                }
            }

            heartbeatStop.Dispose();
            ownershipLost.Dispose();
        }

        private static async Task RunHeartbeatAsync(
            IRedisLocalGpuLockStore store,
            string owner,
            TimeSpan leaseTtl,
            TimeSpan renewInterval,
            CancellationTokenSource heartbeatStop,
            CancellationTokenSource ownershipLost)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(renewInterval, heartbeatStop.Token);
                    if (!await store.RenewIfOwnedAsync(
                            LockKey,
                            owner,
                            leaseTtl,
                            heartbeatStop.Token))
                    {
                        ownershipLost.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                ownershipLost.Cancel();
            }
        }
    }
}

internal interface IRedisLocalGpuLockStore
{
    Task<bool> TryAcquireAsync(
        string key,
        string owner,
        TimeSpan expiry);

    Task<bool> RenewIfOwnedAsync(
        string key,
        string owner,
        TimeSpan expiry,
        CancellationToken cancellationToken);

    Task<bool> ReleaseIfOwnedAsync(string key, string owner);
}

internal sealed class RedisLocalGpuLockStore(IConnectionMultiplexer redis) : IRedisLocalGpuLockStore
{
    // Atomic owner comparison prevents a stale lease from deleting a successor's lock.
    internal const string CompareDeleteScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
          return redis.call('DEL', KEYS[1])
        end
        return 0
        """;
    internal const string CompareRenewScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
          return redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 0
        """;

    private readonly IDatabase database = redis.GetDatabase();

    public Task<bool> TryAcquireAsync(
        string key,
        string owner,
        TimeSpan expiry)
    {
        // StringSet with When.NotExists is Redis SET key value NX PX <ttl>.
        return database.StringSetAsync(
            key,
            owner,
            expiry,
            When.NotExists,
            CommandFlags.DemandMaster);
    }

    public async Task<bool> ReleaseIfOwnedAsync(string key, string owner)
    {
        var result = await database.ScriptEvaluateAsync(
            CompareDeleteScript,
            [new RedisKey(key)],
            [new RedisValue(owner)],
            CommandFlags.DemandMaster);
        return (long)result == 1;
    }

    public async Task<bool> RenewIfOwnedAsync(
        string key,
        string owner,
        TimeSpan expiry,
        CancellationToken cancellationToken)
    {
        var expiryMilliseconds = checked((long)Math.Ceiling(expiry.TotalMilliseconds));
        var result = await database.ScriptEvaluateAsync(
                CompareRenewScript,
                [new RedisKey(key)],
                [new RedisValue(owner), new RedisValue(expiryMilliseconds.ToString(CultureInfo.InvariantCulture))],
                CommandFlags.DemandMaster)
            .WaitAsync(cancellationToken);
        return (long)result == 1;
    }
}
