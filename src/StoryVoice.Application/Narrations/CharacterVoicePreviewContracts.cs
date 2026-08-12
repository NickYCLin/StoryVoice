namespace StoryVoice.Application.Narrations;

public sealed record PreviewVoiceProfileRequest(string Text);

public sealed record VoiceProfilePreviewAudio(byte[] Content, string ContentType);

/// <summary>
/// On-demand "test speech" synthesis for one Ready <see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfile"/> —
/// a single short 3wa synthesize call run synchronously to completion and returned as bytes,
/// deliberately outside the Worker's staged-job pipeline (no narration job, no persisted audio
/// file) since this is a UI preview, not a narration artifact.
/// </summary>
public interface ICharacterVoicePreviewService
{
    Task<VoiceProfilePreviewAudio?> PreviewAsync(
        Guid characterProfileId,
        Guid profileId,
        PreviewVoiceProfileRequest request,
        CancellationToken cancellationToken);
}
