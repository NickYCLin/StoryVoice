namespace StoryVoice.Infrastructure.Narrations;

public sealed record ThreeWaSynthesisRequest(
    string Text,
    string Mode,
    string? VoiceProfileTaskId,
    string? VoicePromptText,
    int Seed = 0);

public sealed record ThreeWaSynthesisTaskHandle(
    string TaskId,
    string StatusUrl,
    string ResultUrl,
    string? ArtifactUrlTemplate);

public sealed record ThreeWaSynthesisArtifact(string Id, string? MimeType);

/// <summary>
/// The Worker-side half of talking to the 3wa AIHub canonical
/// <c>api.php?mode=voice_generate</c> endpoint: submitting
/// one turn's text for synthesis against an already-<see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfileStatus.Ready"/>
/// voice, then following the submit response's same-origin status/result/artifact URLs until the
/// rendered audio can be downloaded.
/// </summary>
public interface IThreeWaSynthesisClient
{
    Task<ThreeWaSynthesisTaskHandle> SubmitAsync(ThreeWaSynthesisRequest request, CancellationToken cancellationToken);

    Task<string> GetTaskStatusAsync(string statusUrl, CancellationToken cancellationToken);

    Task<IReadOnlyList<ThreeWaSynthesisArtifact>> GetResultArtifactsAsync(string resultUrl, CancellationToken cancellationToken);

    Task DownloadArtifactAsync(
        string artifactUrlTemplate,
        string artifactId,
        Stream destination,
        CancellationToken cancellationToken);
}
