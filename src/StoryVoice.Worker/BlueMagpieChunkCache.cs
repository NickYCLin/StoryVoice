using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.Worker;

public sealed class BlueMagpieChunkCache(
    IOptions<BlueMagpieChunkCacheOptions> cacheOptions,
    IOptions<BlueMagpieOptions> blueMagpieOptions,
    ILogger<BlueMagpieChunkCache> logger) : IBlueMagpieChunkCache
{
    internal const string CacheSchema = "storyvoice:bluemagpie-chunk-cache:v1";
    internal const string ChunkingContract = "unicode-scalar-punctuation-v1:120";
    internal const string OutputContract = "pcm16le:48000:mono";
    private const string MarkerFileName = ".storyvoice-bluemagpie-chunk-cache-v1";
    private const string RootLockFileName = ".root.lock";
    private const string ScopeLockFileName = ".scope.lock";
    private const int MaximumManifestBytes = 16 * 1024;
    private const int ManifestCapacityReservationBytes = 4 * 1024;
    private static readonly Regex ScopeDirectoryPattern = new(
        "^job-[0-9a-f]{32}-[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex EntryDirectoryPattern = new(
        "^chunk-[0-9]{5}-[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DeletingDirectoryPattern = new(
        "^\\.deleting-[0-9a-f]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TemporaryDirectoryPattern = new(
        "^\\.tmp-[0-9a-f]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private readonly Func<string, bool> _deleteOwnedDirectory = DeleteOwnedDirectoryCore;
    private bool _initialized;
    private long _knownBytes;

    internal BlueMagpieChunkCache(
        IOptions<BlueMagpieChunkCacheOptions> cacheOptions,
        IOptions<BlueMagpieOptions> blueMagpieOptions,
        ILogger<BlueMagpieChunkCache> logger,
        Func<string, bool> deleteOwnedDirectory)
        : this(cacheOptions, blueMagpieOptions, logger)
    {
        _deleteOwnedDirectory = deleteOwnedDirectory;
    }

    public async Task<IBlueMagpieChunkCacheScope> OpenScopeAsync(
        NarrationSynthesisCacheContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context);
        await EnsureInitializedAsync(cancellationToken);

        var scopeFingerprint = ComputeScopeFingerprint(context);
        var scopeName = $"job-{context.JobId:N}-{scopeFingerprint}";
        var scopePath = Path.Combine(RootPath, scopeName);
        var jobLockPath = Path.Combine(RootPath, $".job-{context.JobId:N}.lock");
        FileStream? jobLock = null;
        FileStream? scopeLock = null;
        while (scopeLock is null)
        {
            await using (await AcquireFileLockAsync(RootLockPath, wait: true, cancellationToken))
            {
                var acquiredJobLock = TryAcquireFileLock(jobLockPath);
                if (acquiredJobLock is not null)
                {
                    try
                    {
                        if (Directory.Exists(scopePath) && IsReparsePoint(scopePath))
                        {
                            throw new IOException("BlueMagpie cache scope must not be a reparse point.");
                        }

                        Directory.CreateDirectory(scopePath);
                        SetDirectoryOwnerOnly(scopePath);
                        Directory.SetLastWriteTimeUtc(scopePath, DateTime.UtcNow);
                        var acquiredScopeLock = TryAcquireFileLock(
                            Path.Combine(scopePath, ScopeLockFileName));
                        if (acquiredScopeLock is not null)
                        {
                            jobLock = acquiredJobLock;
                            scopeLock = acquiredScopeLock;
                            acquiredJobLock = null;
                        }
                    }
                    finally
                    {
                        if (acquiredJobLock is not null)
                        {
                            await acquiredJobLock.DisposeAsync();
                        }
                    }
                }
            }

            if (scopeLock is null)
            {
                await Task.Delay(cacheOptions.Value.LockRetryMilliseconds, cancellationToken);
            }
        }

        return new CacheScope(
            this,
            context.JobId,
            scopePath,
            scopeFingerprint,
            jobLock!,
            scopeLock);
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _maintenanceGate.WaitAsync(cancellationToken);
        try
        {
            await CleanupCoreAsync(requiredReservationBytes: 0, scheduled: true, cancellationToken);
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    internal static string ComputeScopeFingerprint(NarrationSynthesisCacheContext context) =>
        HashFields(
        [
            CacheSchema,
            context.OwnerId.ToString("N"),
            context.JobId.ToString("N"),
            context.SourceHash,
            context.CastRevisionId.ToString("N"),
            context.CastFingerprint,
            context.SpeechPlanFingerprint,
            context.CompositionVersion,
            context.FfmpegProfile,
            BlueMagpieOptions.ProviderName,
            BlueMagpieOptions.PinnedProviderVersion,
            BlueMagpieOptions.PinnedModelRevision,
            ChunkingContract,
            OutputContract,
        ]);

    internal static string ComputeEntryFingerprint(
        string scopeFingerprint,
        BlueMagpieChunkCacheRequest request)
    {
        var textHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)))
            .ToLowerInvariant();
        return HashFields(
        [
            CacheSchema,
            scopeFingerprint,
            request.ProviderVersion,
            request.ModelRevision,
            ChunkingContract,
            OutputContract,
            request.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            textHash,
            request.Voice,
            request.Rate,
            request.Pitch,
            request.Volume,
            request.PauseBeforeMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]);
    }

    private string RootPath => Path.GetFullPath(cacheOptions.Value.RootPath);

    private string RootLockPath => Path.Combine(RootPath, RootLockFileName);

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var rootPath = RootPath;
            var volumeRoot = Path.GetPathRoot(rootPath);
            if (string.IsNullOrWhiteSpace(volumeRoot)
                || string.Equals(
                    rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("BlueMagpie cache root must not be a filesystem root.");
            }

            Directory.CreateDirectory(rootPath);
            if (IsReparsePoint(rootPath))
            {
                throw new IOException("BlueMagpie cache root must not be a reparse point.");
            }

            SetDirectoryOwnerOnly(rootPath);
            await using (await AcquireFileLockAsync(RootLockPath, wait: true, cancellationToken))
            {
                var markerPath = Path.Combine(rootPath, MarkerFileName);
                if (!File.Exists(markerPath))
                {
                    var unexpectedEntries = Directory.EnumerateFileSystemEntries(rootPath)
                        .Where(path => !string.Equals(
                            Path.GetFileName(path),
                            RootLockFileName,
                            StringComparison.Ordinal))
                        .Take(1)
                        .Any();
                    if (unexpectedEntries)
                    {
                        throw new IOException(
                            "BlueMagpie cache root is non-empty and has no ownership marker.");
                    }

                    await WriteDurableFileAsync(
                        markerPath,
                        Encoding.ASCII.GetBytes(CacheSchema),
                        createNew: true,
                        cancellationToken);
                }
                else
                {
                    var marker = await File.ReadAllTextAsync(markerPath, cancellationToken);
                    if (!string.Equals(marker, CacheSchema, StringComparison.Ordinal))
                    {
                        throw new IOException("BlueMagpie cache ownership marker is invalid.");
                    }

                    SetFileOwnerOnly(markerPath);
                }
            }

            Interlocked.Exchange(ref _knownBytes, CalculateCacheBytes());
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<BlueMagpieChunkCacheEntry> GetOrCreateAsync(
        Guid jobId,
        string scopePath,
        string scopeFingerprint,
        BlueMagpieChunkCacheRequest request,
        Func<CancellationToken, Task<byte[]>> createAudio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(createAudio);
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var entryFingerprint = ComputeEntryFingerprint(scopeFingerprint, request);
        var entryPath = Path.Combine(
            scopePath,
            $"chunk-{request.Ordinal:00000}-{entryFingerprint}");
        var cached = await TryReadEntryAsync(
            entryPath,
            scopeFingerprint,
            entryFingerprint,
            request,
            cancellationToken);
        if (cached is not null)
        {
            return cached with { CacheHit = true };
        }

        if (Directory.Exists(entryPath))
        {
            var invalidBytes = CalculateDirectoryBytes(entryPath);
            if (TryDeleteOwnedDirectory(entryPath))
            {
                Interlocked.Add(ref _knownBytes, -invalidBytes);
            }
            logger.LogWarning(
                "Discarded an invalid BlueMagpie cache entry for job {JobId} at ordinal {Ordinal}",
                jobId,
                request.Ordinal);
        }

        var reservationBytes = checked(
            (long)blueMagpieOptions.Value.MaximumResponseBytes + ManifestCapacityReservationBytes);
        await EnsureCapacityAsync(reservationBytes, cancellationToken);

        var audio = await createAudio(cancellationToken);
        if (audio.Length is < 1
            || audio.LongLength > blueMagpieOptions.Value.MaximumResponseBytes
            || !BlueMagpiePcmWaveValidator.IsValid(audio))
        {
            throw new IOException("BlueMagpie cache rejected an invalid PCM WAV payload.");
        }

        var audioHash = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();
        var manifest = new CacheManifest(
            CacheSchema,
            scopeFingerprint,
            entryFingerprint,
            request.ProviderVersion,
            request.ModelRevision,
            request.Voice,
            audio.LongLength,
            audioHash,
            DateTimeOffset.UtcNow);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        if (manifestBytes.Length > MaximumManifestBytes)
        {
            throw new IOException("BlueMagpie cache manifest exceeds its safe size.");
        }

        var temporaryPath = Path.Combine(scopePath, $".tmp-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryPath);
            SetDirectoryOwnerOnly(temporaryPath);
            await WriteDurableFileAsync(
                Path.Combine(temporaryPath, "audio.wav"),
                audio,
                createNew: true,
                cancellationToken);
            await WriteDurableFileAsync(
                Path.Combine(temporaryPath, "manifest.json"),
                manifestBytes,
                createNew: true,
                cancellationToken);

            Directory.Move(temporaryPath, entryPath);
            SetDirectoryOwnerOnly(entryPath);
            var committedBytes = CalculateDirectoryBytes(entryPath);
            Interlocked.Add(ref _knownBytes, committedBytes);
        }
        finally
        {
            if (Directory.Exists(temporaryPath) && !TryDeleteOwnedDirectory(temporaryPath))
            {
                Interlocked.Exchange(ref _knownBytes, CalculateCacheBytes());
            }
        }

        return new BlueMagpieChunkCacheEntry(
            Path.Combine(entryPath, "audio.wav"),
            audio.LongLength,
            CacheHit: false);
    }

    private async Task<BlueMagpieChunkCacheEntry?> TryReadEntryAsync(
        string entryPath,
        string scopeFingerprint,
        string entryFingerprint,
        BlueMagpieChunkCacheRequest request,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(entryPath) || IsReparsePoint(entryPath))
        {
            return null;
        }

        var audioPath = Path.Combine(entryPath, "audio.wav");
        var manifestPath = Path.Combine(entryPath, "manifest.json");
        if (!File.Exists(audioPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        var audioInfo = new FileInfo(audioPath);
        var manifestInfo = new FileInfo(manifestPath);
        if (audioInfo.Length is < 1
            || audioInfo.Length > blueMagpieOptions.Value.MaximumResponseBytes
            || manifestInfo.Length is < 1 or > MaximumManifestBytes)
        {
            return null;
        }

        try
        {
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<CacheManifest>(manifestBytes);
            if (manifest is null
                || !string.Equals(manifest.Schema, CacheSchema, StringComparison.Ordinal)
                || !string.Equals(manifest.ScopeFingerprint, scopeFingerprint, StringComparison.Ordinal)
                || !string.Equals(manifest.EntryFingerprint, entryFingerprint, StringComparison.Ordinal)
                || !string.Equals(manifest.ProviderVersion, request.ProviderVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.ModelRevision, request.ModelRevision, StringComparison.Ordinal)
                || !string.Equals(manifest.Voice, request.Voice, StringComparison.Ordinal)
                || manifest.AudioBytes != audioInfo.Length)
            {
                return null;
            }

            var audio = await File.ReadAllBytesAsync(audioPath, cancellationToken);
            var actualHash = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();
            if (!string.Equals(actualHash, manifest.AudioSha256, StringComparison.Ordinal)
                || !BlueMagpiePcmWaveValidator.IsValid(audio))
            {
                return null;
            }

            SetFileOwnerOnly(audioPath);
            SetFileOwnerOnly(manifestPath);
            return new BlueMagpieChunkCacheEntry(audioPath, audio.LongLength, CacheHit: true);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task EnsureCapacityAsync(long reservationBytes, CancellationToken cancellationToken)
    {
        if (HasCapacity(reservationBytes))
        {
            return;
        }

        await _maintenanceGate.WaitAsync(cancellationToken);
        try
        {
            if (!HasCapacity(reservationBytes))
            {
                await CleanupCoreAsync(reservationBytes, scheduled: false, cancellationToken);
            }

            if (!HasCapacity(reservationBytes))
            {
                throw new BlueMagpieChunkCacheCapacityException(
                    "BlueMagpie cache cannot reserve durable capacity before synthesis.");
            }
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    private bool HasCapacity(long reservationBytes)
    {
        var maximumBytes = cacheOptions.Value.MaximumBytes;
        var knownBytes = Math.Max(0, Interlocked.Read(ref _knownBytes));
        return reservationBytes <= maximumBytes - Math.Min(maximumBytes, knownBytes)
            && HasMinimumFreeSpace(reservationBytes);
    }

    private bool HasMinimumFreeSpace(long reservationBytes)
    {
        if (cacheOptions.Value.MinimumFreeBytes == 0)
        {
            return true;
        }

        try
        {
            var volumeRoot = Path.GetPathRoot(RootPath)!;
            var available = new DriveInfo(volumeRoot).AvailableFreeSpace;
            return available >= checked(cacheOptions.Value.MinimumFreeBytes + reservationBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Unable to inspect free space for the BlueMagpie cache");
            return false;
        }
    }

    private async Task CleanupCoreAsync(
        long requiredReservationBytes,
        bool scheduled,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var retentionCutoff = now.AddHours(-cacheOptions.Value.RetentionHours);
        var tempCutoff = now.AddMinutes(-cacheOptions.Value.TemporaryEntryRetentionMinutes);
        var candidates = EnumerateScopeDirectories()
            .OrderBy(scope => scope.LastWriteTimeUtc)
            .ToArray();

        foreach (var leftover in EnumerateDeletingDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = TryDeleteOwnedDirectory(leftover.FullName);
        }

        foreach (var scope in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TryCleanTemporaryEntriesAsync(scope.FullName, tempCutoff, cancellationToken);
        }

        var targetBytes = scheduled
            ? long.MinValue
            : Math.Min(
                cacheOptions.Value.LowWatermarkBytes,
                Math.Max(0, cacheOptions.Value.MaximumBytes - requiredReservationBytes));
        var evictionCandidates = scheduled
            ? candidates.Where(scope => scope.LastWriteTimeUtc <= retentionCutoff)
            : candidates;
        foreach (var scope in evictionCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!scheduled
                && Interlocked.Read(ref _knownBytes) <= targetBytes
                && HasMinimumFreeSpace(requiredReservationBytes))
            {
                break;
            }

            var removedBytes = await TryDeleteScopeAsync(scope.FullName, cancellationToken);
            if (removedBytes > 0)
            {
                Interlocked.Add(ref _knownBytes, -removedBytes);
            }
        }

        Interlocked.Exchange(ref _knownBytes, CalculateCacheBytes());
    }

    private async Task TryCleanTemporaryEntriesAsync(
        string scopePath,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        FileStream? scopeLock = null;
        await using (await AcquireFileLockAsync(RootLockPath, wait: true, cancellationToken))
        {
            if (!Directory.Exists(scopePath))
            {
                return;
            }

            scopeLock = TryAcquireFileLock(Path.Combine(scopePath, ScopeLockFileName));
        }

        if (scopeLock is null)
        {
            return;
        }

        await using (scopeLock)
        {
            foreach (var path in Directory.EnumerateDirectories(scopePath, ".tmp-*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new DirectoryInfo(path);
                if (info.LastWriteTimeUtc <= cutoff && !IsReparsePoint(path))
                {
                    _ = TryDeleteOwnedDirectory(path);
                }
            }
        }
    }

    private async Task<long> TryDeleteScopeAsync(
        string scopePath,
        CancellationToken cancellationToken)
    {
        string? deletingPath = null;
        await using (await AcquireFileLockAsync(RootLockPath, wait: true, cancellationToken))
        {
            if (!Directory.Exists(scopePath) || IsReparsePoint(scopePath))
            {
                return 0;
            }

            var scopeLock = TryAcquireFileLock(Path.Combine(scopePath, ScopeLockFileName));
            if (scopeLock is null)
            {
                return 0;
            }

            await scopeLock.DisposeAsync();
            deletingPath = Path.Combine(RootPath, $".deleting-{Guid.NewGuid():N}");
            Directory.Move(scopePath, deletingPath);
        }

        var bytes = CalculateDirectoryBytes(deletingPath);
        return TryDeleteOwnedDirectory(deletingPath) ? bytes : 0;
    }

    private IEnumerable<DirectoryInfo> EnumerateScopeDirectories() =>
        new DirectoryInfo(RootPath)
            .EnumerateDirectories("job-*", SearchOption.TopDirectoryOnly)
            .Where(directory => ScopeDirectoryPattern.IsMatch(directory.Name)
                && !IsReparsePoint(directory.FullName));

    private IEnumerable<DirectoryInfo> EnumerateDeletingDirectories() =>
        new DirectoryInfo(RootPath)
            .EnumerateDirectories(".deleting-*", SearchOption.TopDirectoryOnly)
            .Where(directory => DeletingDirectoryPattern.IsMatch(directory.Name)
                && !IsReparsePoint(directory.FullName));

    private long CalculateCacheBytes()
    {
        long total = 0;
        foreach (var scope in EnumerateScopeDirectories())
        {
            total = checked(total + CalculateDirectoryBytes(scope.FullName));
        }

        foreach (var deleting in EnumerateDeletingDirectories())
        {
            total = checked(total + CalculateDirectoryBytes(deleting.FullName));
        }

        return total;
    }

    private static long CalculateDirectoryBytes(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return 0;
        }

        long total = SumOwnedFiles(path);
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (!IsReparsePoint(directory)
                && (EntryDirectoryPattern.IsMatch(name)
                    || TemporaryDirectoryPattern.IsMatch(name)))
            {
                total = checked(total + SumOwnedFiles(directory));
            }
        }

        return total;
    }

    private static long SumOwnedFiles(string directory)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(file);
            if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                total = checked(total + info.Length);
            }
        }

        return total;
    }

    private async Task<FileStream> AcquireFileLockAsync(
        string path,
        bool wait,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = TryAcquireFileLock(path);
            if (stream is not null)
            {
                return stream;
            }

            if (!wait)
            {
                throw new IOException("BlueMagpie cache lock is already held.");
            }

            await Task.Delay(cacheOptions.Value.LockRetryMilliseconds, cancellationToken);
        }
    }

    private static FileStream? TryAcquireFileLock(string path)
    {
        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous);
            SetFileOwnerOnly(path);
            return stream;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task WriteDurableFileAsync(
        string path,
        ReadOnlyMemory<byte> content,
        bool createNew,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
        SetFileOwnerOnly(path);
    }

    private static void ValidateContext(NarrationSynthesisCacheContext context)
    {
        if (context.OwnerId == Guid.Empty
            || context.JobId == Guid.Empty
            || context.CastRevisionId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.SourceHash)
            || string.IsNullOrWhiteSpace(context.CastFingerprint)
            || string.IsNullOrWhiteSpace(context.SpeechPlanFingerprint)
            || string.IsNullOrWhiteSpace(context.CompositionVersion)
            || string.IsNullOrWhiteSpace(context.FfmpegProfile))
        {
            throw new ArgumentException("BlueMagpie cache context is incomplete.", nameof(context));
        }
    }

    private static void ValidateRequest(BlueMagpieChunkCacheRequest request)
    {
        if (request.Ordinal is < 0 or >= 10_000
            || string.IsNullOrWhiteSpace(request.Text)
            || string.IsNullOrWhiteSpace(request.Voice)
            || string.IsNullOrWhiteSpace(request.Rate)
            || string.IsNullOrWhiteSpace(request.Pitch)
            || string.IsNullOrWhiteSpace(request.Volume)
            || request.PauseBeforeMs is < 0 or > 60_000
            || string.IsNullOrWhiteSpace(request.ProviderVersion)
            || string.IsNullOrWhiteSpace(request.ModelRevision))
        {
            throw new ArgumentException("BlueMagpie cache request is invalid.", nameof(request));
        }
    }

    private static string HashFields(IEnumerable<string> fields)
    {
        using var canonical = new MemoryStream();
        Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
            canonical.Write(lengthPrefix);
            canonical.Write(bytes);
        }

        return Convert.ToHexString(
            SHA256.HashData(canonical.GetBuffer().AsSpan(0, checked((int)canonical.Length))))
            .ToLowerInvariant();
    }

    private static bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private bool TryDeleteOwnedDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !Directory.Exists(path)
            || IsReparsePoint(path))
        {
            return false;
        }

        try
        {
            return _deleteOwnedDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool DeleteOwnedDirectoryCore(string path)
    {
        Directory.Delete(path, recursive: true);
        return true;
    }

    private static void SetDirectoryOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void SetFileOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private void TouchScope(string scopePath)
    {
        try
        {
            if (Directory.Exists(scopePath))
            {
                Directory.SetLastWriteTimeUtc(scopePath, DateTime.UtcNow);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Unable to update BlueMagpie cache scope activity");
        }
    }

    private sealed record CacheManifest(
        string Schema,
        string ScopeFingerprint,
        string EntryFingerprint,
        string ProviderVersion,
        string ModelRevision,
        string Voice,
        long AudioBytes,
        string AudioSha256,
        DateTimeOffset CreatedAtUtc);

    private sealed class CacheScope(
        BlueMagpieChunkCache owner,
        Guid jobId,
        string scopePath,
        string scopeFingerprint,
        FileStream jobLock,
        FileStream scopeLock) : IBlueMagpieChunkCacheScope
    {
        private bool _disposed;

        public Task<BlueMagpieChunkCacheEntry> GetOrCreateAsync(
            BlueMagpieChunkCacheRequest request,
            Func<CancellationToken, Task<byte[]>> createAudio,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return owner.GetOrCreateAsync(
                jobId,
                scopePath,
                scopeFingerprint,
                request,
                createAudio,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.TouchScope(scopePath);
            await scopeLock.DisposeAsync();
            await jobLock.DisposeAsync();
        }
    }
}
