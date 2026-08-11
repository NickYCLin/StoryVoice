namespace StoryVoice.Application.Narrations.SpeechPlanning;

public sealed record SpeechPlanSegmentResponse(
    Guid Id,
    int SortOrder,
    string SourceKind,
    string Kind,
    int StartOffset,
    int Length,
    Guid? CharacterId,
    string? CharacterName,
    int Confidence,
    string DecisionSource,
    string ReviewStatus);

public sealed record ChapterSpeechPlanDraftResponse(
    Guid Id,
    Guid SeriesId,
    Guid BookId,
    Guid ChapterId,
    int PlanVersion,
    string Status,
    Guid? ConfirmedRevisionId,
    IReadOnlyList<SpeechPlanSegmentResponse> Segments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConfirmedSpeechPlanRevisionResponse(
    Guid Id,
    Guid SeriesId,
    Guid BookId,
    Guid ChapterId,
    int RevisionNumber,
    string Fingerprint,
    int SegmentCount,
    DateTimeOffset CreatedAt);

public sealed record ConfirmSpeechSegmentRequest(Guid? CharacterId);
