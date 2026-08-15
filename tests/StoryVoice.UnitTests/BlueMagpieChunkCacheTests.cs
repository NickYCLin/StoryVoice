using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure.Narrations;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class BlueMagpieChunkCacheTests
{
    [Fact]
    public void Options_default_to_the_bounded_private_cache_contract()
    {
        var options = new BlueMagpieChunkCacheOptions();

        Assert.Equal("/data/bluemagpie-chunk-cache", options.RootPath);
        Assert.Equal(32L * 1024 * 1024 * 1024, options.MaximumBytes);
        Assert.Equal(24L * 1024 * 1024 * 1024, options.LowWatermarkBytes);
        Assert.Equal(64L * 1024 * 1024 * 1024, options.MinimumFreeBytes);
        Assert.Equal(168, options.RetentionHours);
        Assert.Equal(30, options.CleanupIntervalMinutes);
        Assert.Equal(60, options.TemporaryEntryRetentionMinutes);
    }

    [Fact]
    public void Fingerprints_change_for_every_durable_identity_and_chunk_policy_field()
    {
        var context = CreateContext();
        var scope = BlueMagpieChunkCache.ComputeScopeFingerprint(context);
        var request = CreateRequest("不得寫入路徑的小說文字");
        var entry = BlueMagpieChunkCache.ComputeEntryFingerprint(scope, request);

        Assert.NotEqual(scope, BlueMagpieChunkCache.ComputeScopeFingerprint(
            context with { SourceHash = new string('d', 64) }));
        Assert.NotEqual(scope, BlueMagpieChunkCache.ComputeScopeFingerprint(
            context with { CastFingerprint = new string('e', 64) }));
        Assert.NotEqual(scope, BlueMagpieChunkCache.ComputeScopeFingerprint(
            context with { SpeechPlanFingerprint = new string('f', 64) }));
        Assert.NotEqual(entry, BlueMagpieChunkCache.ComputeEntryFingerprint(
            scope,
            request with { Voice = BlueMagpieOptions.MaleVoice }));
        Assert.NotEqual(entry, BlueMagpieChunkCache.ComputeEntryFingerprint(
            scope,
            request with { Volume = "-5%" }));
        Assert.NotEqual(entry, BlueMagpieChunkCache.ComputeEntryFingerprint(
            scope,
            request with { PauseBeforeMs = 250 }));
        Assert.NotEqual(entry, BlueMagpieChunkCache.ComputeEntryFingerprint(
            scope,
            request with { Text = request.Text + "。" }));
    }

    [Fact]
    public async Task Committed_entry_survives_a_new_cache_instance_without_plaintext_or_HTTP_replay()
    {
        var root = CreateRoot();
        const string secretText = "這段小說原文絕對不可出現在快取檔名或中繼資料";
        var context = CreateContext();
        var request = CreateRequest(secretText);
        var factoryCalls = 0;
        try
        {
            var firstCache = CreateCache(root);
            BlueMagpieChunkCacheEntry first;
            await using (var scope = await firstCache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken))
            {
                first = await scope.GetOrCreateAsync(
                    request,
                    _ =>
                    {
                        factoryCalls++;
                        return Task.FromResult(CreateWavBytes(1));
                    },
                    TestContext.Current.CancellationToken);
            }

            var secondCache = CreateCache(root);
            BlueMagpieChunkCacheEntry second;
            await using (var scope = await secondCache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken))
            {
                second = await scope.GetOrCreateAsync(
                    request,
                    _ => throw new InvalidOperationException("A cache hit must not call the gateway."),
                    TestContext.Current.CancellationToken);
            }

            Assert.False(first.CacheHit);
            Assert.True(second.CacheHit);
            Assert.Equal(1, factoryCalls);
            Assert.Equal(first.InputWavPath, second.InputWavPath);
            Assert.True(BlueMagpiePcmWaveValidator.IsValid(await File.ReadAllBytesAsync(
                second.InputWavPath,
                TestContext.Current.CancellationToken)));
            Assert.DoesNotContain(secretText, second.InputWavPath, StringComparison.Ordinal);
            var manifest = await File.ReadAllTextAsync(
                Path.Combine(Path.GetDirectoryName(second.InputWavPath)!, "manifest.json"),
                TestContext.Current.CancellationToken);
            Assert.DoesNotContain(secretText, manifest, StringComparison.Ordinal);

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(root));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(second.InputWavPath));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Corrupted_PCM_is_discarded_and_only_that_entry_is_regenerated()
    {
        var root = CreateRoot();
        var context = CreateContext();
        var request = CreateRequest("損壞測試");
        var factoryCalls = 0;
        try
        {
            var cache = CreateCache(root);
            BlueMagpieChunkCacheEntry first;
            await using (var scope = await cache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken))
            {
                first = await scope.GetOrCreateAsync(
                    request,
                    _ =>
                    {
                        factoryCalls++;
                        return Task.FromResult(CreateWavBytes(1));
                    },
                    TestContext.Current.CancellationToken);
            }

            var corrupt = await File.ReadAllBytesAsync(
                first.InputWavPath,
                TestContext.Current.CancellationToken);
            corrupt[^1] ^= 0x7f;
            await File.WriteAllBytesAsync(
                first.InputWavPath,
                corrupt,
                TestContext.Current.CancellationToken);

            await using (var scope = await cache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken))
            {
                var repaired = await scope.GetOrCreateAsync(
                    request,
                    _ =>
                    {
                        factoryCalls++;
                        return Task.FromResult(CreateWavBytes(2));
                    },
                    TestContext.Current.CancellationToken);
                Assert.False(repaired.CacheHit);
                Assert.Equal(
                    CreateWavBytes(2),
                    await File.ReadAllBytesAsync(
                        repaired.InputWavPath,
                        TestContext.Current.CancellationToken));
            }

            Assert.Equal(2, factoryCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Job_scope_lock_serializes_two_process_like_cache_instances()
    {
        var root = CreateRoot();
        var context = CreateContext();
        try
        {
            var firstCache = CreateCache(root);
            var secondCache = CreateCache(root);
            var firstScope = await firstCache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken);
            var waiting = secondCache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken);

            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.False(waiting.IsCompleted);

            await firstScope.DisposeAsync();
            await using var secondScope = await waiting.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var entry = await secondScope.GetOrCreateAsync(
                CreateRequest("鎖定後合成"),
                _ => Task.FromResult(CreateWavBytes()),
                TestContext.Current.CancellationToken);
            Assert.False(entry.CacheHit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Job_lock_serializes_the_same_job_even_when_the_cache_context_changes()
    {
        var root = CreateRoot();
        var context = CreateContext();
        var changedContext = context with { SourceHash = new string('d', 64) };
        try
        {
            var firstCache = CreateCache(root);
            var secondCache = CreateCache(root);
            var firstScope = await firstCache.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken);
            var waiting = secondCache.OpenScopeAsync(
                changedContext,
                TestContext.Current.CancellationToken);

            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.False(waiting.IsCompleted);

            await firstScope.DisposeAsync();
            await using var secondScope = await waiting.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Job_lock_does_not_serialize_different_jobs()
    {
        var root = CreateRoot();
        var firstContext = CreateContext();
        var secondContext = firstContext with { JobId = Guid.NewGuid() };
        try
        {
            var firstCache = CreateCache(root);
            var secondCache = CreateCache(root);
            await using var firstScope = await firstCache.OpenScopeAsync(
                firstContext,
                TestContext.Current.CancellationToken);
            await using var secondScope = await secondCache.OpenScopeAsync(
                secondContext,
                TestContext.Current.CancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_skips_a_locked_scope_then_removes_it_after_retention()
    {
        var root = CreateRoot();
        var context = CreateContext();
        try
        {
            var cache = CreateCache(root, retentionHours: 1);
            var scope = await cache.OpenScopeAsync(context, TestContext.Current.CancellationToken);
            _ = await scope.GetOrCreateAsync(
                CreateRequest("仍在使用"),
                _ => Task.FromResult(CreateWavBytes()),
                TestContext.Current.CancellationToken);
            var scopePath = Assert.Single(Directory.EnumerateDirectories(root, "job-*"));
            Directory.SetLastWriteTimeUtc(scopePath, DateTime.UtcNow.AddHours(-2));

            await cache.CleanupAsync(TestContext.Current.CancellationToken);
            Assert.True(Directory.Exists(scopePath));

            await scope.DisposeAsync();
            Directory.SetLastWriteTimeUtc(scopePath, DateTime.UtcNow.AddHours(-2));
            await cache.CleanupAsync(TestContext.Current.CancellationToken);
            Assert.False(Directory.Exists(scopePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Capacity_pressure_evicts_a_recent_unlocked_scope_before_synthesis()
    {
        var root = CreateRoot();
        var firstContext = CreateContext();
        var secondContext = firstContext with { JobId = Guid.NewGuid() };
        try
        {
            var cache = CreateCache(root, maximumBytes: 5_200, lowWatermarkBytes: 0);
            string firstEntryPath;
            await using (var firstScope = await cache.OpenScopeAsync(
                firstContext,
                TestContext.Current.CancellationToken))
            {
                var firstEntry = await firstScope.GetOrCreateAsync(
                    CreateRequest("最近完成的舊工作"),
                    _ => Task.FromResult(CreateWavBytes(1)),
                    TestContext.Current.CancellationToken);
                firstEntryPath = firstEntry.InputWavPath;
            }

            var firstScopePath = Assert.Single(Directory.EnumerateDirectories(root, "job-*"));
            Directory.SetLastWriteTimeUtc(firstScopePath, DateTime.UtcNow.AddMinutes(-1));

            var gatewayCalls = 0;
            await using (var secondScope = await cache.OpenScopeAsync(
                secondContext,
                TestContext.Current.CancellationToken))
            {
                var secondEntry = await secondScope.GetOrCreateAsync(
                    CreateRequest("需要新空間的工作"),
                    _ =>
                    {
                        gatewayCalls++;
                        return Task.FromResult(CreateWavBytes(2));
                    },
                    TestContext.Current.CancellationToken);
                Assert.True(File.Exists(secondEntry.InputWavPath));
            }

            Assert.Equal(1, gatewayCalls);
            Assert.False(File.Exists(firstEntryPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_deleting_directory_remains_accounted_and_blocks_gateway_before_synthesis()
    {
        var root = CreateRoot();
        var context = CreateContext();
        try
        {
            var initializer = CreateCache(root);
            await using (await initializer.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken))
            {
            }

            var leftover = Path.Combine(root, $".deleting-{Guid.NewGuid():N}");
            Directory.CreateDirectory(leftover);
            await File.WriteAllBytesAsync(
                Path.Combine(leftover, "payload.bin"),
                new byte[4_096],
                TestContext.Current.CancellationToken);
            var cache = CreateCache(
                root,
                maximumBytes: 6_000,
                lowWatermarkBytes: 1_000,
                deleteOwnedDirectory: _ => false);
            var gatewayCalled = false;
            await using var scope = await cache.OpenScopeAsync(
                context with { JobId = Guid.NewGuid() },
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<BlueMagpieChunkCacheCapacityException>(() =>
                scope.GetOrCreateAsync(
                    CreateRequest("容量不足"),
                    _ =>
                    {
                        gatewayCalled = true;
                        return Task.FromResult(CreateWavBytes());
                    },
                    TestContext.Current.CancellationToken));
            Assert.False(gatewayCalled);
            Assert.True(Directory.Exists(leftover));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Byte_accounting_does_not_follow_reparse_directories()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        var outside = CreateRoot();
        var context = CreateContext();
        try
        {
            var initializer = CreateCache(root);
            await using (await initializer.OpenScopeAsync(
                context,
                TestContext.Current.CancellationToken))
            {
            }

            await File.WriteAllBytesAsync(
                Path.Combine(outside, "large.bin"),
                new byte[1_000_000],
                TestContext.Current.CancellationToken);
            var deleting = Path.Combine(root, $".deleting-{Guid.NewGuid():N}");
            Directory.CreateDirectory(deleting);
            Directory.CreateSymbolicLink(
                Path.Combine(deleting, $".tmp-{Guid.NewGuid():N}"),
                outside);
            var cache = CreateCache(root, maximumBytes: 8_000, lowWatermarkBytes: 4_000);
            await using var scope = await cache.OpenScopeAsync(
                context with { JobId = Guid.NewGuid() },
                TestContext.Current.CancellationToken);

            var entry = await scope.GetOrCreateAsync(
                CreateRequest("不可追連結"),
                _ => Task.FromResult(CreateWavBytes()),
                TestContext.Current.CancellationToken);
            Assert.False(entry.CacheHit);
            Assert.True(File.Exists(Path.Combine(outside, "large.bin")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_service_contains_non_cancellation_failures()
    {
        var service = new BlueMagpieChunkCacheCleanupService(
            new ThrowingCache(),
            Options.Create(new BlueMagpieChunkCacheOptions()),
            NullLogger<BlueMagpieChunkCacheCleanupService>.Instance);

        Assert.True(await service.RunCleanupOnceAsync(TestContext.Current.CancellationToken));
    }

    private static BlueMagpieChunkCache CreateCache(
        string root,
        long maximumBytes = 64 * 1024,
        long lowWatermarkBytes = 32 * 1024,
        int retentionHours = 168,
        Func<string, bool>? deleteOwnedDirectory = null)
    {
        var cacheOptions = Options.Create(new BlueMagpieChunkCacheOptions
        {
            RootPath = root,
            MaximumBytes = maximumBytes,
            LowWatermarkBytes = lowWatermarkBytes,
            MinimumFreeBytes = 0,
            RetentionHours = retentionHours,
            CleanupIntervalMinutes = 30,
            TemporaryEntryRetentionMinutes = 60,
            LockRetryMilliseconds = 25,
        });
        var blueOptions = Options.Create(new BlueMagpieOptions { MaximumResponseBytes = 1024 });
        return deleteOwnedDirectory is null
            ? new BlueMagpieChunkCache(
                cacheOptions,
                blueOptions,
                NullLogger<BlueMagpieChunkCache>.Instance)
            : new BlueMagpieChunkCache(
                cacheOptions,
                blueOptions,
                NullLogger<BlueMagpieChunkCache>.Instance,
                deleteOwnedDirectory);
    }

    private static NarrationSynthesisCacheContext CreateContext() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            Guid.NewGuid(),
            new string('b', 64),
            new string('c', 64),
            "bluemagpie-pcm16-concat-v1",
            "wav-48khz-mono-to-mp3-concat-v1");

    private static BlueMagpieChunkCacheRequest CreateRequest(string text) =>
        new(
            0,
            text,
            BlueMagpieOptions.FemaleVoice,
            "+0%",
            "+0Hz",
            "+0%",
            0,
            BlueMagpieOptions.PinnedProviderVersion,
            BlueMagpieOptions.PinnedModelRevision);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storyvoice-blue-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static byte[] CreateWavBytes(short sample = 0)
    {
        var bytes = new byte[46];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 38);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), 48_000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), 96_000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34, 2), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44, 2), sample);
        return bytes;
    }

    private sealed class ThrowingCache : IBlueMagpieChunkCache
    {
        public Task<IBlueMagpieChunkCacheScope> OpenScopeAsync(
            NarrationSynthesisCacheContext context,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CleanupAsync(CancellationToken cancellationToken) =>
            Task.FromException(new OverflowException("synthetic cleanup failure"));
    }
}
