namespace StoryVoice.Domain.Narrations;

public enum ChapterSpeechPlanDraftStatus
{
    Draft,
    NeedsReview,
    ReadyToConfirm,
    Stale
}

public enum SpeechSegmentSourceKind
{
    ChapterTitle,
    Body
}

public enum SpeechSegmentTurnKind
{
    Narrator,
    Dialogue
}

public enum SpeechSegmentReviewStatus
{
    Suggested,
    Confirmed,
    Rejected
}

public enum SpeechSegmentDecisionSource
{
    Rule,
    LocalModel,
    User
}

/// <summary>
/// One segmenter/attribution output turned into a draft-plan input. Built by the caller (the
/// Application-layer speech-plan service) from <c>ChineseSpeechSegmenter</c> offsets plus
/// optional <c>ISpeakerAttributionProvider</c> suggestions — the domain never runs segmentation
/// or attribution itself, it only enforces the resulting invariants.
/// </summary>
public sealed record DraftSegmentInput(
    int SortOrder,
    SpeechSegmentSourceKind SourceKind,
    int StartOffset,
    int Length,
    string TextHash,
    SpeechSegmentTurnKind Kind,
    Guid? CharacterId,
    int Confidence,
    SpeechSegmentDecisionSource DecisionSource,
    SpeechSegmentReviewStatus ReviewStatus);
