namespace StoryVoice.Application.Narrations;

public sealed record CreateNarrationRequest(bool RightsAttested);

public sealed record NarrationJobResponse(
    Guid Id,
    Guid BookId,
    Guid ContentBookId,
    string SourceHash,
    string Voice,
    string Rate,
    string Status,
    int ProgressPercent,
    int Attempts,
    bool CancellationRequested,
    string? ErrorCode,
    long? AudioBytes,
    DateTimeOffset RightsAttestedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record NarrationAudioDescriptor(string AbsolutePath, string ContentType);
