using StoryVoice.Domain.Series;

namespace StoryVoice.Domain.Narrations;

public enum CharacterVoiceProfileKind
{
    Base,
    Scene,
}

public enum CharacterVoiceProfileMode
{
    /// <summary>Synthesized purely from a text description (<see cref="CharacterVoiceProfile.VoicePromptText"/>) —
    /// no reference audio, no consent tracking, ready to use immediately.</summary>
    Design,

    /// <summary>Cloned from an uploaded reference recording. Requires <see cref="CharacterVoiceProfile.ConsentType"/>
    /// and goes through the 3wa profile_prepare → ASR draft → profile_confirm lifecycle before it's usable.</summary>
    Clone,
}

public enum CharacterVoiceProfileStatus
{
    Pending,
    AwaitingTranscriptConfirmation,
    Ready,
    Failed,
}

/// <summary>
/// The consent categories the 3wa Cluster API itself accepts for a cloned reference voice. Kept
/// in the domain (not just validated at the API boundary) so no code path can ever persist a
/// clone profile without one of these.
/// </summary>
public static class CharacterVoiceConsentTypes
{
    public const string SelfRecorded = "self_recorded";
    public const string ExplicitPermission = "explicit_permission";
    public const string LicensedVoice = "licensed_voice";

    internal static readonly IReadOnlyCollection<string> All = [SelfRecorded, ExplicitPermission, LicensedVoice];
}

/// <summary>
/// The fixed scene vocabulary a character's situational voice profiles can be filed under. The
/// Worker's own <c>DialogueEmotion</c> enum (StoryVoice.Worker, a separate project with no
/// dependency on Domain) maps onto these same string codes when resolving a dialogue turn's voice.
/// </summary>
public static class CharacterVoiceSceneCodes
{
    public const string Neutral = "neutral";
    public const string Nervous = "nervous";
    public const string Happy = "happy";
    public const string Angry = "angry";
    public const string Sad = "sad";

    internal static readonly IReadOnlyCollection<string> All = [Neutral, Nervous, Happy, Angry, Sad];
}

/// <summary>
/// One character's base or situational (scene) cloned/designed voice. The canonical, re-creatable
/// asset is the trio of reference audio + confirmed transcript + consent record — <see cref="VoiceProfileTaskId"/>
/// is only a cache of whatever the 3wa Cluster station currently has, since profile operations are
/// pinned to a single station with no failover and can need rebuilding.
/// </summary>
public sealed class CharacterVoiceProfile
{
    private CharacterVoiceProfile()
    {
    }

    private CharacterVoiceProfile(
        Guid id,
        Guid ownerId,
        Guid characterProfileId,
        CharacterVoiceProfileKind kind,
        string? sceneCode,
        CharacterVoiceProfileMode mode,
        string? consentType,
        string? referenceAudioRelativePath,
        string? referenceAudioSha256,
        string? voicePromptText,
        Guid? rightsConfirmedByUserId,
        DateTimeOffset now)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(characterProfileId, nameof(characterProfileId));

        Id = id;
        OwnerId = ownerId;
        CharacterProfileId = characterProfileId;
        Kind = kind;
        SceneCode = sceneCode;
        Mode = mode;
        ConsentType = consentType;
        ReferenceAudioRelativePath = referenceAudioRelativePath;
        ReferenceAudioSha256 = referenceAudioSha256;
        VoicePromptText = voicePromptText;
        RightsConfirmedByUserId = rightsConfirmedByUserId;
        RightsConfirmedAt = rightsConfirmedByUserId is not null ? now : null;
        Status = CharacterVoiceProfileStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CharacterProfileId { get; private set; }
    public CharacterVoiceProfileKind Kind { get; private set; }
    public string? SceneCode { get; private set; }
    public CharacterVoiceProfileMode Mode { get; private set; }
    public string? ConsentType { get; private set; }
    public string? ReferenceAudioRelativePath { get; private set; }
    public string? ReferenceAudioSha256 { get; private set; }
    public string? VoicePromptText { get; private set; }
    public string? Transcript { get; private set; }
    public DateTimeOffset? TranscriptConfirmedAt { get; private set; }
    public string? VoiceProfileTaskId { get; private set; }
    public CharacterVoiceProfileStatus Status { get; private set; }
    public DateTimeOffset? RightsConfirmedAt { get; private set; }
    public Guid? RightsConfirmedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    public static CharacterVoiceProfile CreateClone(
        Guid id,
        Guid ownerId,
        Guid characterProfileId,
        CharacterVoiceProfileKind kind,
        string? sceneCode,
        string consentType,
        string referenceAudioRelativePath,
        string referenceAudioSha256,
        Guid rightsConfirmedByUserId,
        DateTimeOffset now)
    {
        ValidateSceneCode(kind, sceneCode);
        EnsureId(rightsConfirmedByUserId, nameof(rightsConfirmedByUserId));
        var normalizedConsent = ValidateConsentType(consentType);
        var normalizedPath = SeriesValueValidator.NormalizePrintable(
            referenceAudioRelativePath,
            500,
            nameof(referenceAudioRelativePath));
        var normalizedSha256 = ValidateSha256(referenceAudioSha256);

        return new CharacterVoiceProfile(
            id,
            ownerId,
            characterProfileId,
            kind,
            sceneCode,
            CharacterVoiceProfileMode.Clone,
            normalizedConsent,
            normalizedPath,
            normalizedSha256,
            voicePromptText: null,
            rightsConfirmedByUserId,
            now);
    }

    public static CharacterVoiceProfile CreateDesign(
        Guid id,
        Guid ownerId,
        Guid characterProfileId,
        CharacterVoiceProfileKind kind,
        string? sceneCode,
        string voicePromptText,
        DateTimeOffset now)
    {
        ValidateSceneCode(kind, sceneCode);
        var normalizedPrompt = SeriesValueValidator.NormalizePrintable(voicePromptText, 2_000, nameof(voicePromptText));

        var profile = new CharacterVoiceProfile(
            id,
            ownerId,
            characterProfileId,
            kind,
            sceneCode,
            CharacterVoiceProfileMode.Design,
            consentType: null,
            referenceAudioRelativePath: null,
            referenceAudioSha256: null,
            normalizedPrompt,
            rightsConfirmedByUserId: null,
            now);

        // Design mode has no ASR/consent lifecycle at all — the 3wa `synthesize(mode=design)`
        // call takes the voice_prompt text directly on every request, so there's nothing to
        // prepare or confirm ahead of time.
        profile.Status = CharacterVoiceProfileStatus.Ready;
        return profile;
    }

    /// <summary>Records the opaque task id a `profile_prepare` call returned, and that the ASR
    /// draft transcript has come back and is waiting on human confirmation.</summary>
    public void AttachDraftTranscript(string taskId, string draftTranscript, DateTimeOffset now)
    {
        EnsureCloneMode();
        if (TranscriptConfirmedAt is not null)
        {
            throw new InvalidOperationException("這個聲線的文字稿已經確認過，不可再覆寫草稿。");
        }

        VoiceProfileTaskId = SeriesValueValidator.NormalizePrintable(taskId, 191, nameof(taskId));
        Transcript = SeriesValueValidator.NormalizePrintable(draftTranscript, 2_000, nameof(draftTranscript));
        Status = CharacterVoiceProfileStatus.AwaitingTranscriptConfirmation;
        Touch(now);
    }

    /// <summary>Just records the task id while the reference audio is still being transcribed —
    /// no draft text yet to review.</summary>
    public void AttachPendingTask(string taskId, DateTimeOffset now)
    {
        EnsureCloneMode();
        VoiceProfileTaskId = SeriesValueValidator.NormalizePrintable(taskId, 191, nameof(taskId));
        Touch(now);
    }

    public void ConfirmTranscript(string confirmedTranscript, DateTimeOffset now)
    {
        EnsureCloneMode();
        if (Status != CharacterVoiceProfileStatus.AwaitingTranscriptConfirmation)
        {
            throw new InvalidOperationException("只有等待確認文字稿的聲線可以確認。");
        }

        Transcript = SeriesValueValidator.NormalizePrintable(confirmedTranscript, 2_000, nameof(confirmedTranscript));
        TranscriptConfirmedAt = now;
        Status = CharacterVoiceProfileStatus.Ready;
        Touch(now);
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Status = CharacterVoiceProfileStatus.Failed;
        Touch(now);
    }

    /// <summary>Re-points this profile at a freshly rebuilt 3wa task after the pinned station
    /// lost the old one — only possible once a transcript has already been confirmed, since that
    /// (plus the stored reference audio) is the canonical asset being replayed.</summary>
    public void ReattachRebuiltTask(string newTaskId, DateTimeOffset now)
    {
        EnsureCloneMode();
        if (TranscriptConfirmedAt is null)
        {
            throw new InvalidOperationException("只有已確認文字稿的聲線可以重建。");
        }

        VoiceProfileTaskId = SeriesValueValidator.NormalizePrintable(newTaskId, 191, nameof(newTaskId));
        Status = CharacterVoiceProfileStatus.Ready;
        Touch(now);
    }

    private void EnsureCloneMode()
    {
        if (Mode != CharacterVoiceProfileMode.Clone)
        {
            throw new InvalidOperationException("這個操作只適用於克隆聲線。");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private static void ValidateSceneCode(CharacterVoiceProfileKind kind, string? sceneCode)
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

    private static string ValidateConsentType(string consentType)
    {
        var normalized = SeriesValueValidator.NormalizePrintable(consentType, 32, nameof(consentType));
        if (!CharacterVoiceConsentTypes.All.Contains(normalized))
        {
            throw new ArgumentException("授權同意類型無效。", nameof(consentType));
        }

        return normalized;
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
