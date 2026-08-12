namespace StoryVoice.Infrastructure.Narrations;

public sealed record ThreeWaSynthesisRequest(
    string Text,
    string Mode,
    string? VoiceProfileTaskId,
    string? VoicePromptText);

public sealed record ThreeWaSynthesisTaskHandle(
    string TaskId,
    string StatusUrl,
    string ResultUrl,
    string? ArtifactUrlTemplate,
    string? AckUrlTemplate);

public sealed record ThreeWaSynthesisArtifact(string Id, string? MimeType);

/// <summary>
/// The Worker-side half of talking to the 3wa Cluster API's <c>voice_generate</c> mode: submitting
/// one turn's text for synthesis against an already-<see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfileStatus.Ready"/>
/// voice, then following the submit response's own status/result/artifact URLs (treated as opaque
/// — never reconstructed from a hardcoded path) until the rendered audio can be downloaded.
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

    Task AcknowledgeArtifactAsync(string? ackUrlTemplate, string artifactId, CancellationToken cancellationToken);
}
