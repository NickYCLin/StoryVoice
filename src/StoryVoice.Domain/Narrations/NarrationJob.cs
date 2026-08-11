namespace StoryVoice.Domain.Narrations;

public sealed class NarrationJob
{
    internal const string MultiCharacterVoiceCompatibilityMarker = "speech-plan";
    internal const string MultiCharacterRateCompatibilityMarker = "per-segment";

    private NarrationJob()
    {
        Visibility = NarrationArtifactVisibility.Published;
    }

    private NarrationJob(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string sourceHash,
        string voice,
        string rate,
        DateTimeOffset rightsAttestedAt)
    {
        if (ownerId == Guid.Empty || bookId == Guid.Empty || contentBookId == Guid.Empty)
        {
            throw new ArgumentException("朗讀工作的擁有者與書籍識別碼不可為空白。");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        BookId = bookId;
        ContentBookId = contentBookId;
        SourceHash = Require(sourceHash, nameof(sourceHash), 128);
        Voice = Require(voice, nameof(voice), 200);
        Rate = Require(rate, nameof(rate), 20);
        Mode = NarrationMode.SingleVoice;
        Visibility = NarrationArtifactVisibility.Published;
        RightsAttestedAt = rightsAttestedAt;
        Status = NarrationJobStatus.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        NextAttemptAt = CreatedAt;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private NarrationJob(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        Guid seriesId,
        Guid castRevisionId,
        Guid speechPlanRevisionId,
        Guid rebuildBatchId,
        Guid rebuildMemberId,
        string sourceHash,
        DateTimeOffset rightsAttestedAt)
        : this(
            ownerId,
            bookId,
            contentBookId,
            sourceHash,
            MultiCharacterVoiceCompatibilityMarker,
            MultiCharacterRateCompatibilityMarker,
            rightsAttestedAt)
    {
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(castRevisionId, nameof(castRevisionId));
        EnsureId(speechPlanRevisionId, nameof(speechPlanRevisionId));
        EnsureId(rebuildBatchId, nameof(rebuildBatchId));
        EnsureId(rebuildMemberId, nameof(rebuildMemberId));

        Mode = NarrationMode.MultiCharacter;
        Visibility = NarrationArtifactVisibility.Staged;
        SeriesId = seriesId;
        CastRevisionId = castRevisionId;
        SpeechPlanRevisionId = speechPlanRevisionId;
        RebuildBatchId = rebuildBatchId;
        RebuildMemberId = rebuildMemberId;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid BookId { get; private set; }
    public Guid ContentBookId { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public string Voice { get; private set; } = string.Empty;
    public string Rate { get; private set; } = string.Empty;
    public NarrationMode Mode { get; private set; }
    public NarrationArtifactVisibility Visibility { get; private set; }
    public Guid? SeriesId { get; }
    public Guid? CastRevisionId { get; }
    public Guid? SpeechPlanRevisionId { get; }
    public Guid? RebuildBatchId { get; }
    public Guid? RebuildMemberId { get; }
    public NarrationJobStatus Status { get; private set; }
    public int ProgressPercent { get; private set; }
    public int Attempts { get; private set; }
    public bool CancellationRequested { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? AudioRelativePath { get; private set; }
    public long? AudioBytes { get; private set; }
    public DateTimeOffset RightsAttestedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
    public bool IsAvailableForRegularPlayback =>
        Visibility == NarrationArtifactVisibility.Published
        && Status == NarrationJobStatus.Completed
        && HasSafeAudioMetadata(AudioRelativePath, AudioBytes);
    internal bool HasSafeCompletedAudio =>
        Status == NarrationJobStatus.Completed
        && HasSafeAudioMetadata(AudioRelativePath, AudioBytes);

    public static NarrationJob Create(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string sourceHash,
        string voice,
        string rate,
        DateTimeOffset rightsAttestedAt) =>
        new(ownerId, bookId, contentBookId, sourceHash, voice, rate, rightsAttestedAt);

    internal static NarrationJob CreateMultiCharacterStaged(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        Guid seriesId,
        Guid castRevisionId,
        Guid speechPlanRevisionId,
        Guid rebuildBatchId,
        Guid rebuildMemberId,
        string sourceHash,
        DateTimeOffset rightsAttestedAt) =>
        new(
            ownerId,
            bookId,
            contentBookId,
            seriesId,
            castRevisionId,
            speechPlanRevisionId,
            rebuildBatchId,
            rebuildMemberId,
            sourceHash,
            rightsAttestedAt);

    internal void Publish(DateTimeOffset now)
    {
        if (Mode != NarrationMode.MultiCharacter
            || Visibility != NarrationArtifactVisibility.Staged
            || Status != NarrationJobStatus.Completed
            || !HasSafeAudioMetadata(AudioRelativePath, AudioBytes))
        {
            throw new InvalidOperationException("只有已完成且具備音訊中繼資料的多角色 staged 成品可以發布。");
        }

        EnsureTransitionTime(now);

        Visibility = NarrationArtifactVisibility.Published;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    internal void MarkHistorical(DateTimeOffset now)
    {
        if (Visibility != NarrationArtifactVisibility.Published
            || Status != NarrationJobStatus.Completed
            || !HasSafeAudioMetadata(AudioRelativePath, AudioBytes))
        {
            throw new InvalidOperationException("只有已發布且具備音訊中繼資料的完成成品可以轉為歷史版本。");
        }

        EnsureTransitionTime(now);

        Visibility = NarrationArtifactVisibility.Historical;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void Claim(
        string leaseOwner,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset? claimedAt = null)
    {
        var now = claimedAt ?? DateTimeOffset.UtcNow;
        var reclaimable = Status == NarrationJobStatus.Running
            && LeaseExpiresAt is not null
            && LeaseExpiresAt <= now;
        if (Status != NarrationJobStatus.Queued && !reclaimable)
        {
            throw new InvalidOperationException("只有排隊中或租約已過期的朗讀工作可以領取。");
        }

        if (Status == NarrationJobStatus.Queued && NextAttemptAt is not null && NextAttemptAt > now)
        {
            throw new InvalidOperationException("朗讀工作仍在重試退避期間。");
        }

        if (CancellationRequested)
        {
            Cancel();
            return;
        }

        Status = NarrationJobStatus.Running;
        ProgressPercent = 10;
        Attempts++;
        LeaseOwner = Require(leaseOwner, nameof(leaseOwner), 200);
        LeaseExpiresAt = leaseExpiresAt;
        NextAttemptAt = null;
        ErrorCode = null;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void RequestCancellation()
    {
        if (Status is NarrationJobStatus.Completed or NarrationJobStatus.Failed or NarrationJobStatus.Cancelled)
        {
            return;
        }

        CancellationRequested = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
        if (Status == NarrationJobStatus.Queued)
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        if (Status is NarrationJobStatus.Completed or NarrationJobStatus.Failed)
        {
            throw new InvalidOperationException("已完成或已失敗的朗讀工作不可取消。");
        }

        Status = NarrationJobStatus.Cancelled;
        ProgressPercent = 0;
        CancellationRequested = true;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        NextAttemptAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void Complete(string audioRelativePath, long audioBytes)
    {
        if (Status != NarrationJobStatus.Running)
        {
            throw new InvalidOperationException("只有處理中的朗讀工作可以完成。");
        }

        if (CancellationRequested)
        {
            Cancel();
            return;
        }

        var normalizedPath = Require(audioRelativePath, nameof(audioRelativePath), 1_000)
            .Replace('\\', '/');
        if (!IsSafeRelativeAudioPath(normalizedPath))
        {
            throw new ArgumentException("音訊路徑必須是安全的相對路徑。", nameof(audioRelativePath));
        }

        if (audioBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(audioBytes), "音訊大小必須大於零。");
        }

        AudioRelativePath = normalizedPath;
        AudioBytes = audioBytes;
        Status = NarrationJobStatus.Completed;
        ProgressPercent = 100;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        NextAttemptAt = null;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CompletedAt.Value;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void FailOrRequeue(
        string errorCode,
        int maxAttempts,
        DateTimeOffset? failedAt = null)
    {
        if (Status != NarrationJobStatus.Running)
        {
            throw new InvalidOperationException("只有處理中的朗讀工作可以記錄失敗。");
        }

        if (CancellationRequested)
        {
            Cancel();
            return;
        }

        var now = failedAt ?? DateTimeOffset.UtcNow;
        ErrorCode = Require(errorCode, nameof(errorCode), 100);
        Status = Attempts < maxAttempts ? NarrationJobStatus.Queued : NarrationJobStatus.Failed;
        ProgressPercent = 0;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        NextAttemptAt = Status == NarrationJobStatus.Queued
            ? now.AddSeconds(Math.Min(30, Math.Pow(2, Attempts)))
            : null;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void FailPermanently(string errorCode)
    {
        if (Status != NarrationJobStatus.Running)
        {
            throw new InvalidOperationException("只有處理中的朗讀工作可以記錄失敗。");
        }

        ErrorCode = Require(errorCode, nameof(errorCode), 100);
        Status = NarrationJobStatus.Failed;
        ProgressPercent = 0;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        NextAttemptAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void Requeue(DateTimeOffset? rightsAttestedAt = null)
    {
        if (Visibility == NarrationArtifactVisibility.Historical)
        {
            throw new InvalidOperationException("歷史朗讀成品不可重新排隊。");
        }

        if (Status is not (NarrationJobStatus.Failed or NarrationJobStatus.Cancelled))
        {
            return;
        }

        Status = NarrationJobStatus.Queued;
        ProgressPercent = 0;
        Attempts = 0;
        CancellationRequested = false;
        ErrorCode = null;
        AudioRelativePath = null;
        AudioBytes = null;
        CompletedAt = null;
        LeaseOwner = null;
        LeaseExpiresAt = null;
        RightsAttestedAt = rightsAttestedAt ?? RightsAttestedAt;
        NextAttemptAt = DateTimeOffset.UtcNow;
        UpdatedAt = NextAttemptAt.Value;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private void EnsureTransitionTime(DateTimeOffset now)
    {
        if (now < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "轉換時間不可早於目前更新時間。");
        }
    }

    private static bool HasSafeAudioMetadata(string? audioRelativePath, long? audioBytes)
    {
        return audioBytes is >= 1 && IsSafeRelativeAudioPath(audioRelativePath);
    }

    private static bool IsSafeRelativeAudioPath(string? audioRelativePath)
    {
        if (string.IsNullOrWhiteSpace(audioRelativePath)
            || audioRelativePath.Length > 1_000)
        {
            return false;
        }

        var normalizedPath = audioRelativePath.Replace('\\', '/');
        return !Path.IsPathRooted(normalizedPath)
            && !(normalizedPath.Length >= 2
                && char.IsAsciiLetter(normalizedPath[0])
                && normalizedPath[1] == ':')
            && !normalizedPath.Split('/').Any(segment => segment is ".." or ".");
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }

    private static string Require(string? value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maxLength)
        {
            throw new ArgumentException($"欄位必須為 1 至 {maxLength} 個字元。", parameterName);
        }

        return normalized;
    }
}
