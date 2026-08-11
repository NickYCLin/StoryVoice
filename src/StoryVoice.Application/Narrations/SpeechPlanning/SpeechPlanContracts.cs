namespace StoryVoice.Application.Narrations.SpeechPlanning;

public enum SpeechPlanStatus
{
    Ready,
    NeedsReview
}

public enum SpeechSegmentKind
{
    Narrator,
    Dialogue
}

public enum SpeechSegmentStatus
{
    Ready,
    NeedsReview
}

/// <summary>
/// Identifies the chapter-title narrator turn by UTF-16 offsets into the separately stored title.
/// </summary>
public sealed record ChapterTitleNarratorTurn(int StartOffset, int Length);

/// <summary>
/// Identifies a body turn by UTF-16 offsets into the separately stored chapter body.
/// Source text and speaker identity are intentionally not part of this contract.
/// </summary>
public sealed record SpeechSegment(
    int Index,
    SpeechSegmentKind Kind,
    int StartOffset,
    int Length,
    SpeechSegmentStatus Status)
{
    public int EndOffset => checked(StartOffset + Length);
}

public sealed record ChapterSpeechPlan(
    string SourceHash,
    string AlgorithmVersion,
    SpeechPlanStatus Status,
    ChapterTitleNarratorTurn? TitleTurn,
    IReadOnlyList<SpeechSegment> BodySegments);
