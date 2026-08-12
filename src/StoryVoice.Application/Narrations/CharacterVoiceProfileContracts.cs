namespace StoryVoice.Application.Narrations;

public sealed record CreateDesignedVoiceProfileRequest(string VoicePrompt);

public sealed record ConfirmVoiceProfileTranscriptRequest(string Transcript);

public sealed record CharacterVoiceProfileResponse(
    Guid Id,
    Guid CharacterProfileId,
    string Kind,
    string? SceneCode,
    string Mode,
    string? ConsentType,
    string? VoicePromptText,
    string? Transcript,
    bool TranscriptConfirmed,
    string Status,
    double? ReferenceAudioDurationSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterVoiceProfileAudio(string AbsolutePath, string ContentType);

/// <summary>
/// Manages the voice profiles (base + situational scenes) hanging off one
/// <see cref="StoryVoice.Domain.Characters.CharacterProfile"/> library entry — owner-scoped, not
/// tied to any one series, so the same character's voices can be reused across every series it's
/// cast into.
/// </summary>
public interface ICharacterVoiceProfileService
{
    Task<IReadOnlyList<CharacterVoiceProfileResponse>?> ListAsync(
        Guid characterProfileId,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> CreateClonedAsync(
        Guid characterProfileId,
        string kind,
        string? sceneCode,
        string consentType,
        Stream referenceAudio,
        string referenceAudioFileName,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> CreateDesignedAsync(
        Guid characterProfileId,
        string kind,
        string? sceneCode,
        CreateDesignedVoiceProfileRequest request,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> RefreshStatusAsync(
        Guid characterProfileId,
        Guid profileId,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> ConfirmTranscriptAsync(
        Guid characterProfileId,
        Guid profileId,
        ConfirmVoiceProfileTranscriptRequest request,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> RebuildAsync(
        Guid characterProfileId,
        Guid profileId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid characterProfileId, Guid profileId, CancellationToken cancellationToken);

    Task<CharacterVoiceProfileAudio?> GetReferenceAudioAsync(
        Guid characterProfileId,
        Guid profileId,
        CancellationToken cancellationToken);
}
