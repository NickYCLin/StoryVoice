namespace StoryVoice.Infrastructure.Narrations;

public sealed record VoiceProfilePrepareResult(string TaskId, string? DraftTranscript);

public sealed record VoiceProfileStatusResult(
    string TaskStatus,
    bool TranscriptConfirmed,
    string? DraftTranscript);

/// <summary>
/// The Application-layer half of talking to the 3wa Cluster API's <c>voice_generate</c> mode:
/// preparing a clone profile from an uploaded reference recording, polling until the ASR draft is
/// ready, and locking the transcript once a human confirms it. The Worker owns the separate
/// synth-time half (submit/poll/result/artifact for actually generating narration audio) — the two
/// halves never need to share a live HTTP connection, only the same opaque task ids persisted on
/// <see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfile"/>.
/// </summary>
public interface IThreeWaVoiceProfileClient
{
    Task<VoiceProfilePrepareResult> PrepareAsync(
        Stream referenceWav,
        string fileName,
        string profileName,
        string consentType,
        string? promptText,
        CancellationToken cancellationToken);

    Task<VoiceProfileStatusResult> GetStatusAsync(string taskId, CancellationToken cancellationToken);

    Task ConfirmAsync(string taskId, string transcript, CancellationToken cancellationToken);
}
