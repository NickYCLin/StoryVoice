using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure.Insights;

namespace StoryVoice.UnitTests;

public sealed class LocalGpuExecutionGateTests
{
    [Fact]
    public async Task In_process_gate_serializes_waiters_and_abandon_does_not_poison_tests()
    {
        var gate = new InProcessLocalGpuExecutionGate();
        var first = await gate.AcquireAsync(TestContext.Current.CancellationToken);

        var timedOut = await gate.TryAcquireAsync(
            TimeSpan.FromMilliseconds(20),
            TestContext.Current.CancellationToken);

        Assert.Null(timedOut);
        first.Abandon();
        await first.DisposeAsync();

        await using var next = await gate.TryAcquireAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task Redis_gate_uses_one_owner_and_a_ttl_covering_the_maximum_ollama_execution()
    {
        var store = new FakeRedisLockStore();
        var gate = CreateRedisGate(store);
        await using var first = await gate.AcquireAsync(TestContext.Current.CancellationToken);

        var timedOut = await gate.TryAcquireAsync(
            TimeSpan.FromMilliseconds(25),
            TestContext.Current.CancellationToken);

        Assert.Null(timedOut);
        Assert.Equal(TimeSpan.FromSeconds(2_100), store.LastExpiry);
        Assert.False(string.IsNullOrWhiteSpace(store.Owner));
    }

    [Fact]
    public async Task Redis_release_is_owner_safe_when_a_stale_lease_disposes()
    {
        var store = new FakeRedisLockStore();
        var gate = CreateRedisGate(store);
        var staleLease = await gate.AcquireAsync(TestContext.Current.CancellationToken);
        store.ReplaceOwner("successor-owner");

        await staleLease.DisposeAsync();

        Assert.Equal("successor-owner", store.Owner);
        Assert.Equal(1, store.ReleaseAttempts);
        Assert.Contains("redis.call('GET', KEYS[1]) == ARGV[1]", RedisLocalGpuLockStore.CompareDeleteScript, StringComparison.Ordinal);
        Assert.Contains("redis.call('DEL', KEYS[1])", RedisLocalGpuLockStore.CompareDeleteScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redis_abandon_keeps_the_expiring_lock_and_canceled_waiters_stop_polling()
    {
        var store = new FakeRedisLockStore();
        var gate = CreateRedisGate(store);
        var lease = await gate.AcquireAsync(TestContext.Current.CancellationToken);
        var owner = store.Owner;
        lease.Abandon();
        await lease.DisposeAsync();

        Assert.Equal(owner, store.Owner);
        Assert.Equal(0, store.ReleaseAttempts);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.AcquireAsync(cancellation.Token));
    }

    [Fact]
    public async Task Redis_lease_renews_repeatedly_while_the_owner_is_healthy()
    {
        var store = new FakeRedisLockStore();
        var gate = CreateRedisGate(store, TimeSpan.FromMilliseconds(5));
        var lease = await gate.AcquireAsync(TestContext.Current.CancellationToken);

        await store.TwoRenewals.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.True(store.RenewAttempts >= 2);
        Assert.True(store.RenewSuccesses >= 2);
        Assert.False(lease.OwnershipLost.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(2_100), store.LastExpiry);
        await lease.DisposeAsync();
        Assert.Null(store.Owner);
    }

    [Fact]
    public async Task Stale_lease_cannot_renew_a_successor_and_signals_ownership_loss()
    {
        var store = new FakeRedisLockStore();
        var gate = CreateRedisGate(store, TimeSpan.FromMilliseconds(5));
        var staleLease = await gate.AcquireAsync(TestContext.Current.CancellationToken);
        store.ReplaceOwner("successor-owner");

        await WaitForCancellationAsync(staleLease.OwnershipLost)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await staleLease.DisposeAsync();

        Assert.Equal("successor-owner", store.Owner);
        Assert.Equal(0, store.RenewSuccesses);
        Assert.Equal(1, store.ReleaseAttempts);
        Assert.Contains("redis.call('PEXPIRE', KEYS[1], ARGV[2])", RedisLocalGpuLockStore.CompareRenewScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_renewal_failure_releases_own_lock_after_executor_stops()
    {
        var store = new FakeRedisLockStore { FailRenewal = true };
        var gate = CreateRedisGate(store, TimeSpan.FromMilliseconds(5));
        var lease = await gate.AcquireAsync(TestContext.Current.CancellationToken);

        await WaitForCancellationAsync(lease.OwnershipLost)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.NotNull(store.Owner);

        await lease.DisposeAsync();

        Assert.Null(store.Owner);
        Assert.Equal(1, store.ReleaseAttempts);
    }

    [Fact]
    public async Task Timed_out_late_successful_set_is_owner_safely_deleted_before_returning()
    {
        var store = new DelayedAcquireRedisLockStore();
        var gate = new RedisLocalGpuExecutionGate(
            store,
            Options.Create(new LocalGpuExecutionGateOptions
            {
                LeaseSeconds = 2_100,
                PollIntervalMilliseconds = 5,
                RenewIntervalSeconds = 30,
            }),
            TimeSpan.FromSeconds(30));

        var acquisition = gate.TryAcquireAsync(
            TimeSpan.FromMilliseconds(20),
            TestContext.Current.CancellationToken);
        await store.AcquireAttempted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(75), TestContext.Current.CancellationToken);

        // The caller timeout has fired, but the gate must keep observing the already-sent SET.
        Assert.False(acquisition.IsCompleted);
        store.CompleteSuccessfulAcquire();

        Assert.Null(await acquisition);
        Assert.Null(store.Owner);
        Assert.Equal(1, store.ReleaseAttempts);
    }

    private static RedisLocalGpuExecutionGate CreateRedisGate(
        FakeRedisLockStore store,
        TimeSpan? renewIntervalOverride = null) =>
        new(
            store,
            Options.Create(new LocalGpuExecutionGateOptions
            {
                LeaseSeconds = 2_100,
                PollIntervalMilliseconds = 5,
                RenewIntervalSeconds = 30,
            }),
            renewIntervalOverride);

    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), completion);
        return completion.Task;
    }

    private sealed class FakeRedisLockStore : IRedisLocalGpuLockStore
    {
        private readonly object sync = new();
        private string? owner;

        public string? Owner
        {
            get
            {
                lock (sync)
                {
                    return owner;
                }
            }
        }

        public TimeSpan LastExpiry { get; private set; }

        public int ReleaseAttempts { get; private set; }

        public int RenewAttempts { get; private set; }

        public int RenewSuccesses { get; private set; }

        public bool FailRenewal { get; init; }

        public TaskCompletionSource TwoRenewals { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> TryAcquireAsync(
            string key,
            string requestedOwner,
            TimeSpan expiry)
        {
            Assert.Equal(RedisLocalGpuExecutionGate.LockKey, key);
            lock (sync)
            {
                LastExpiry = expiry;
                if (owner is not null)
                {
                    return Task.FromResult(false);
                }

                owner = requestedOwner;
                return Task.FromResult(true);
            }
        }

        public Task<bool> ReleaseIfOwnedAsync(string key, string requestedOwner)
        {
            Assert.Equal(RedisLocalGpuExecutionGate.LockKey, key);
            lock (sync)
            {
                ReleaseAttempts++;
                if (!string.Equals(owner, requestedOwner, StringComparison.Ordinal))
                {
                    return Task.FromResult(false);
                }

                owner = null;
                return Task.FromResult(true);
            }
        }

        public Task<bool> RenewIfOwnedAsync(
            string key,
            string requestedOwner,
            TimeSpan expiry,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(RedisLocalGpuExecutionGate.LockKey, key);
            if (FailRenewal)
            {
                throw new InvalidOperationException("simulated Redis renewal failure");
            }

            lock (sync)
            {
                RenewAttempts++;
                if (RenewAttempts >= 2)
                {
                    TwoRenewals.TrySetResult();
                }

                if (!string.Equals(owner, requestedOwner, StringComparison.Ordinal))
                {
                    return Task.FromResult(false);
                }

                LastExpiry = expiry;
                RenewSuccesses++;
                return Task.FromResult(true);
            }
        }

        public void ReplaceOwner(string replacement)
        {
            lock (sync)
            {
                owner = replacement;
            }
        }
    }

    private sealed class DelayedAcquireRedisLockStore : IRedisLocalGpuLockStore
    {
        private readonly TaskCompletionSource<bool> pendingAcquire =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? requestedOwner;

        public TaskCompletionSource AcquireAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? Owner { get; private set; }

        public int ReleaseAttempts { get; private set; }

        public Task<bool> TryAcquireAsync(string key, string owner, TimeSpan expiry)
        {
            Assert.Equal(RedisLocalGpuExecutionGate.LockKey, key);
            requestedOwner = owner;
            AcquireAttempted.TrySetResult();
            return pendingAcquire.Task;
        }

        public Task<bool> RenewIfOwnedAsync(
            string key,
            string owner,
            TimeSpan expiry,
            CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(Owner, owner, StringComparison.Ordinal));

        public Task<bool> ReleaseIfOwnedAsync(string key, string owner)
        {
            ReleaseAttempts++;
            if (!string.Equals(Owner, owner, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            Owner = null;
            return Task.FromResult(true);
        }

        public void CompleteSuccessfulAcquire()
        {
            Owner = requestedOwner ?? throw new InvalidOperationException("SET was not attempted.");
            pendingAcquire.TrySetResult(true);
        }
    }
}
