namespace StoryVoice.Domain.Narrations;

/// <summary>
/// Immutable copy of a <see cref="SpeechSegmentDraft"/>, frozen at the moment its parent draft
/// was confirmed. Never mutated after construction — a new <see cref="ConfirmedSpeechPlanRevision"/>
/// is required for any further change.
/// </summary>
public sealed class ConfirmedSpeechSegment
{
    private ConfirmedSpeechSegment()
    {
    }

    private ConfirmedSpeechSegment(
        Guid ownerId,
        Guid seriesId,
        Guid planRevisionId,
        int sortOrder,
        SpeechSegmentSourceKind sourceKind,
        int startOffset,
        int length,
        string textHash,
        SpeechSegmentTurnKind kind,
        Guid? characterId,
        int confidence,
        SpeechSegmentDecisionSource decisionSource)
    {
        Id = Guid.NewGuid();
        OwnerId = ownerId;
        SeriesId = seriesId;
        PlanRevisionId = planRevisionId;
        SortOrder = sortOrder;
        SourceKind = sourceKind;
        StartOffset = startOffset;
        Length = length;
        TextHash = textHash;
        Kind = kind;
        CharacterId = characterId;
        Confidence = confidence;
        DecisionSource = decisionSource;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public int SortOrder { get; private set; }
    public SpeechSegmentSourceKind SourceKind { get; private set; }
    public int StartOffset { get; private set; }
    public int Length { get; private set; }
    public string TextHash { get; private set; } = string.Empty;
    public SpeechSegmentTurnKind Kind { get; private set; }
    public Guid? CharacterId { get; private set; }
    public int Confidence { get; private set; }
    public SpeechSegmentDecisionSource DecisionSource { get; private set; }

    internal static ConfirmedSpeechSegment FromConfirmedDraft(
        Guid ownerId,
        Guid seriesId,
        Guid planRevisionId,
        SpeechSegmentDraft draft) =>
        new(
            ownerId,
            seriesId,
            planRevisionId,
            draft.SortOrder,
            draft.SourceKind,
            draft.StartOffset,
            draft.Length,
            draft.TextHash,
            draft.Kind,
            draft.CharacterId,
            draft.Confidence,
            draft.DecisionSource);
}
