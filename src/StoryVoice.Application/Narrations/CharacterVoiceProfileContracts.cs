namespace StoryVoice.Application.Narrations;

public sealed record CreateDesignedVoiceProfileRequest(string VoicePrompt);

public sealed record ConfirmVoiceProfileTranscriptRequest(string Transcript);

public sealed record CharacterVoiceProfileResponse(
    Guid Id,
    Guid CharacterId,
    string Kind,
    string? SceneCode,
    string Mode,
    string? ConsentType,
    string? VoicePromptText,
    string? Transcript,
    bool TranscriptConfirmed,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterVoiceProfileAudio(string AbsolutePath, string ContentType);

public interface ICharacterVoiceProfileService
{
    Task<IReadOnlyList<CharacterVoiceProfileResponse>?> ListAsync(
        Guid seriesId,
        Guid characterId,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> CreateClonedAsync(
        Guid seriesId,
        Guid characterId,
        string kind,
        string? sceneCode,
        string consentType,
        Stream referenceAudio,
        string referenceAudioFileName,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> CreateDesignedAsync(
        Guid seriesId,
        Guid characterId,
        string kind,
        string? sceneCode,
        CreateDesignedVoiceProfileRequest request,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> RefreshStatusAsync(
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> ConfirmTranscriptAsync(
        Guid seriesId,
        Guid profileId,
        ConfirmVoiceProfileTranscriptRequest request,
        CancellationToken cancellationToken);

    Task<CharacterVoiceProfileResponse?> RebuildAsync(
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid seriesId, Guid profileId, CancellationToken cancellationToken);

    Task<CharacterVoiceProfileAudio?> GetReferenceAudioAsync(
        Guid seriesId,
        Guid profileId,
        CancellationToken cancellationToken);
}
