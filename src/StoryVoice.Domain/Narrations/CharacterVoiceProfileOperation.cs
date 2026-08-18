using StoryVoice.Domain.Series;

namespace StoryVoice.Domain.Narrations;

public enum CharacterVoiceProfileOperationType
{
    Create,
    Replace,
}

public enum CharacterVoiceProfileOperationState
{
    Staged,
    RemotePrepared,
    Activated,
    NeedsAttention,
    Rejected,
}

/// <summary>
/// Durable evidence for the non-atomic local/3wa portion of creating a cloned voice. The row is
/// intentionally independent from character/profile foreign-key cascades: if either side of the
/// remote operation becomes uncertain, StoryVoice must retain the task handle, consent metadata,
/// and private WAV path for an operator instead of attempting a destructive best-effort cleanup.
/// </summary>
public sealed class CharacterVoiceProfileOperation
{
    public const int MaximumCanonicalRemoteTaskIdLength = 18;
    public const int MaximumStoredRemoteTaskIdLength = 191;
    public const int MaximumCredentialKeyIdLength = 80;
    public const int MaximumSafeErrorCodeLength = 80;

    private CharacterVoiceProfileOperation()
    {
    }

    private CharacterVoiceProfileOperation(
        Guid id,
        Guid ownerId,
        Guid characterProfileId,
        Guid? oldProfileId,
        Guid newProfileId,
        CharacterVoiceProfileOperationType type,
        CharacterVoiceProfileKind kind,
        string? sceneCode,
        CharacterVoiceConsentEvidence consentEvidence,
        string expectedTranscript,
        string referenceAudioRelativePath,
        string referenceAudioSha256,
        double referenceAudioDurationSeconds,
        Guid rightsConfirmedByUserId,
        Guid? oldProfileConcurrencyStamp,
        string credentialKeyId,
        DateTimeOffset now)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(characterProfileId, nameof(characterProfileId));
        EnsureId(newProfileId, nameof(newProfileId));
        EnsureId(rightsConfirmedByUserId, nameof(rightsConfirmedByUserId));
        ArgumentNullException.ThrowIfNull(consentEvidence);
        ValidateKindAndScene(kind, sceneCode);
        ValidateType(type, oldProfileId, oldProfileConcurrencyStamp);
        if (referenceAudioDurationSeconds is < 10 or > 45)
        {
            throw new ArgumentOutOfRangeException(
                nameof(referenceAudioDurationSeconds),
                "克隆參考錄音長度必須介於 10 到 45 秒之間。");
        }

        Id = id;
        OwnerId = ownerId;
        CharacterProfileId = characterProfileId;
        OldProfileId = oldProfileId;
        NewProfileId = newProfileId;
        Type = type;
        State = CharacterVoiceProfileOperationState.Staged;
        Kind = kind;
        SceneCode = sceneCode;
        SlotKey = kind == CharacterVoiceProfileKind.Base ? "base" : $"scene:{sceneCode}";
        ConsentType = consentEvidence.ConsentType;
        ExpectedTranscript = CharacterVoiceTranscriptCanonicalizer.Normalize(expectedTranscript);
        var expectedTranscriptSha256 = CharacterVoiceTranscriptCanonicalizer.ComputeSha256Hex(
            ExpectedTranscript);
        if (!string.Equals(
                expectedTranscriptSha256,
                consentEvidence.ExpectedTranscriptSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "授權證據與 canonical 預期逐字稿不一致。",
                nameof(consentEvidence));
        }

        RecorderName = consentEvidence.RecorderName;
        RecordingDate = consentEvidence.RecordingDate;
        ConsentSignedDate = consentEvidence.ConsentSignedDate;
        PrivateEvaluationAllowed = consentEvidence.PrivateEvaluationAllowed;
        FormalNarrationAllowed = consentEvidence.FormalNarrationAllowed;
        PublicDistributionAllowed = consentEvidence.PublicDistributionAllowed;
        CommercialUseAllowed = consentEvidence.CommercialUseAllowed;
        ConsentRecordSha256 = consentEvidence.ConsentRecordSha256;
        ConsentReceiptSha256 = consentEvidence.ConsentReceiptSha256;
        ExpectedTranscriptSha256 = consentEvidence.ExpectedTranscriptSha256;
        EvidenceVersion = consentEvidence.EvidenceVersion;
        AttestationVersion = consentEvidence.AttestationVersion;
        ReferenceAudioRelativePath = SeriesValueValidator.NormalizePrintable(
            referenceAudioRelativePath,
            500,
            nameof(referenceAudioRelativePath));
        ReferenceAudioSha256 = ValidateSha256(referenceAudioSha256);
        ReferenceAudioDurationSeconds = referenceAudioDurationSeconds;
        RightsConfirmedByUserId = rightsConfirmedByUserId;
        RightsConfirmedAt = now;
        OldProfileConcurrencyStamp = oldProfileConcurrencyStamp;
        CredentialKeyId = SeriesValueValidator.NormalizePrintable(
            credentialKeyId,
            MaximumCredentialKeyIdLength,
            nameof(credentialKeyId));
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CharacterProfileId { get; private set; }
    public Guid? OldProfileId { get; private set; }
    public Guid NewProfileId { get; private set; }
    public CharacterVoiceProfileOperationType Type { get; private set; }
    public CharacterVoiceProfileOperationState State { get; private set; }
    public CharacterVoiceProfileKind Kind { get; private set; }
    public string? SceneCode { get; private set; }
    public string SlotKey { get; private set; } = string.Empty;
    public string ConsentType { get; private set; } = string.Empty;
    public string RecorderName { get; private set; } = string.Empty;
    public DateOnly RecordingDate { get; private set; }
    public DateOnly ConsentSignedDate { get; private set; }
    public bool PrivateEvaluationAllowed { get; private set; }
    public bool FormalNarrationAllowed { get; private set; }
    public bool PublicDistributionAllowed { get; private set; }
    public bool CommercialUseAllowed { get; private set; }
    public string ConsentRecordSha256 { get; private set; } = string.Empty;
    public string ConsentReceiptSha256 { get; private set; } = string.Empty;
    public string ExpectedTranscriptSha256 { get; private set; } = string.Empty;
    public string EvidenceVersion { get; private set; } = string.Empty;
    public string AttestationVersion { get; private set; } = string.Empty;
    public string ExpectedTranscript { get; private set; } = string.Empty;
    public string? AsrDraftTranscript { get; private set; }
    public string ReferenceAudioRelativePath { get; private set; } = string.Empty;
    public string ReferenceAudioSha256 { get; private set; } = string.Empty;
    public double ReferenceAudioDurationSeconds { get; private set; }
    public Guid RightsConfirmedByUserId { get; private set; }
    public DateTimeOffset RightsConfirmedAt { get; private set; }
    public Guid? OldProfileConcurrencyStamp { get; private set; }
    public string CredentialKeyId { get; private set; } = string.Empty;
    public string? RemoteTaskId { get; private set; }
    public string? SafeErrorCode { get; private set; }
    public DateTimeOffset? RemotePreparedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    public IReadOnlyList<string> UsageScopes
    {
        get
        {
            var scopes = new List<string>(4);
            if (PrivateEvaluationAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.PrivateEvaluation);
            }

            if (FormalNarrationAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.FormalNarration);
            }

            if (PublicDistributionAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.PublicDistribution);
            }

            if (CommercialUseAllowed)
            {
                scopes.Add(CharacterVoiceConsentScopes.CommercialUse);
            }

            return scopes;
        }
    }

    public static CharacterVoiceProfileOperation StageCreate(
        Guid id,
        Guid ownerId,
        Guid characterProfileId,
        Guid newProfileId,
        CharacterVoiceProfileKind kind,
        string? sceneCode,
        CharacterVoiceConsentEvidence consentEvidence,
        string expectedTranscript,
        string referenceAudioRelativePath,
        string referenceAudioSha256,
        double referenceAudioDurationSeconds,
        Guid rightsConfirmedByUserId,
        string credentialKeyId,
        DateTimeOffset now) =>
        new(
            id,
            ownerId,
            characterProfileId,
            oldProfileId: null,
            newProfileId,
            CharacterVoiceProfileOperationType.Create,
            kind,
            sceneCode,
            consentEvidence,
            expectedTranscript,
            referenceAudioRelativePath,
            referenceAudioSha256,
            referenceAudioDurationSeconds,
            rightsConfirmedByUserId,
            oldProfileConcurrencyStamp: null,
            credentialKeyId,
            now);

    public static CharacterVoiceProfileOperation StageReplacement(
        Guid id,
        Guid ownerId,
        Guid characterProfileId,
        Guid oldProfileId,
        Guid oldProfileConcurrencyStamp,
        Guid newProfileId,
        CharacterVoiceConsentEvidence consentEvidence,
        string expectedTranscript,
        string referenceAudioRelativePath,
        string referenceAudioSha256,
        double referenceAudioDurationSeconds,
        Guid rightsConfirmedByUserId,
        string credentialKeyId,
        DateTimeOffset now) =>
        new(
            id,
            ownerId,
            characterProfileId,
            oldProfileId,
            newProfileId,
            CharacterVoiceProfileOperationType.Replace,
            CharacterVoiceProfileKind.Base,
            sceneCode: null,
            consentEvidence,
            expectedTranscript,
            referenceAudioRelativePath,
            referenceAudioSha256,
            referenceAudioDurationSeconds,
            rightsConfirmedByUserId,
            oldProfileConcurrencyStamp,
            credentialKeyId,
            now);

    public void MarkRemotePrepared(string remoteTaskId, string? asrDraftTranscript, DateTimeOffset now)
    {
        if (State != CharacterVoiceProfileOperationState.Staged)
        {
            throw new InvalidOperationException("只有已暫存的克隆操作可以記錄遠端 task。");
        }

        RemoteTaskId = SeriesValueValidator.NormalizePrintable(
            remoteTaskId,
            MaximumStoredRemoteTaskIdLength,
            nameof(remoteTaskId));
        AsrDraftTranscript = SeriesValueValidator.NormalizeOptionalPrintable(
            asrDraftTranscript,
            2_000,
            nameof(asrDraftTranscript));
        RemotePreparedAt = now;
        State = CharacterVoiceProfileOperationState.RemotePrepared;
        SafeErrorCode = null;
        Touch(now);
    }

    public void MarkActivated(DateTimeOffset now)
    {
        if (State != CharacterVoiceProfileOperationState.RemotePrepared || RemoteTaskId is null)
        {
            throw new InvalidOperationException("只有已持久化遠端 task 的克隆操作可以啟用。");
        }

        State = CharacterVoiceProfileOperationState.Activated;
        ActivatedAt = now;
        SafeErrorCode = null;
        Touch(now);
    }

    public void ResumeActivation(DateTimeOffset now)
    {
        if (State != CharacterVoiceProfileOperationState.NeedsAttention
            || SafeErrorCode != "local_activation_uncertain"
            || RemoteTaskId is null)
        {
            throw new InvalidOperationException("只有本地啟用結果不明且已保存遠端 task 的操作可以重試啟用。");
        }

        State = CharacterVoiceProfileOperationState.RemotePrepared;
        SafeErrorCode = null;
        Touch(now);
    }

    public void MarkActivationResolved(DateTimeOffset now)
    {
        if (State != CharacterVoiceProfileOperationState.NeedsAttention
            || SafeErrorCode != "local_activation_uncertain"
            || RemoteTaskId is null)
        {
            throw new InvalidOperationException("只有本地啟用結果不明的操作可以標記為已對帳。");
        }

        State = CharacterVoiceProfileOperationState.Activated;
        ActivatedAt = now;
        SafeErrorCode = null;
        Touch(now);
    }

    public void MarkNeedsAttention(string safeErrorCode, DateTimeOffset now)
    {
        if (State == CharacterVoiceProfileOperationState.Activated)
        {
            throw new InvalidOperationException("已啟用的克隆操作不可改成待人工處理。");
        }

        State = CharacterVoiceProfileOperationState.NeedsAttention;
        SafeErrorCode = ValidateSafeErrorCode(safeErrorCode);
        Touch(now);
    }

    public void MarkRejected(string safeErrorCode, DateTimeOffset now)
    {
        if (State != CharacterVoiceProfileOperationState.Staged || RemoteTaskId is not null)
        {
            throw new InvalidOperationException("只有確認未建立遠端 task 的暫存操作可以標記為拒絕。");
        }

        SafeErrorCode = ValidateSafeErrorCode(safeErrorCode);
        State = CharacterVoiceProfileOperationState.Rejected;
        Touch(now);
    }

    private static string ValidateSafeErrorCode(string safeErrorCode)
    {
        var normalized = SeriesValueValidator.NormalizePrintable(
            safeErrorCode,
            MaximumSafeErrorCodeLength,
            nameof(safeErrorCode));
        if (normalized.Any(character =>
                !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
        {
            throw new ArgumentException("安全錯誤代碼只能包含小寫英數字與底線。", nameof(safeErrorCode));
        }

        return normalized;
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private static void ValidateType(
        CharacterVoiceProfileOperationType type,
        Guid? oldProfileId,
        Guid? oldProfileConcurrencyStamp)
    {
        if (type == CharacterVoiceProfileOperationType.Create)
        {
            if (oldProfileId is not null || oldProfileConcurrencyStamp is not null)
            {
                throw new ArgumentException("建立操作不可指定舊聲線。", nameof(oldProfileId));
            }

            return;
        }

        if (oldProfileId is null || oldProfileId == Guid.Empty
            || oldProfileConcurrencyStamp is null || oldProfileConcurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("取代操作必須保存舊聲線與並行版本。", nameof(oldProfileId));
        }
    }

    private static void ValidateKindAndScene(CharacterVoiceProfileKind kind, string? sceneCode)
    {
        if (kind == CharacterVoiceProfileKind.Base)
        {
            if (sceneCode is not null)
            {
                throw new ArgumentException("基礎聲線不可指定情境代碼。", nameof(sceneCode));
            }

            return;
        }

        if (sceneCode is null || !CharacterVoiceSceneCodes.All.Contains(sceneCode))
        {
            throw new ArgumentException("情境聲線必須指定有效的情境代碼。", nameof(sceneCode));
        }
    }

    private static string ValidateSha256(string value)
    {
        var normalized = SeriesValueValidator.NormalizePrintable(value, 64, nameof(value));
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("音檔雜湊必須是 64 字元的十六進位字串。", nameof(value));
        }

        return normalized.ToLowerInvariant();
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
