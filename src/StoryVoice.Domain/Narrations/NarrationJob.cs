namespace StoryVoice.Domain.Narrations;

public sealed class NarrationJob
{
    private NarrationJob()
    {
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
        RightsAttestedAt = rightsAttestedAt;
        Status = NarrationJobStatus.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        NextAttemptAt = CreatedAt;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid BookId { get; private set; }
    public Guid ContentBookId { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public string Voice { get; private set; } = string.Empty;
    public string Rate { get; private set; } = string.Empty;
    public NarrationMode Mode { get; private set; }
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

    public static NarrationJob Create(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string sourceHash,
        string voice,
        string rate,
        DateTimeOffset rightsAttestedAt) =>
        new(ownerId, bookId, contentBookId, sourceHash, voice, rate, rightsAttestedAt);

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
        if (Path.IsPathRooted(normalizedPath)
            || normalizedPath.Split('/').Any(segment => segment is ".." or "."))
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
