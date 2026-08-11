namespace StoryVoice.Domain.Narrations;

/// <summary>
/// Locks one chapter's <see cref="ConfirmedSpeechPlanRevision"/> onto a
/// <see cref="NarrationJob"/>, written in the same transaction as the job itself. A Worker only
/// ever loads plans through these rows — never "the latest confirmed revision" — so a later draft
/// edit or re-confirmation can never change what an already-queued job synthesizes.
/// </summary>
public sealed class NarrationJobSpeechPlan
{
    private NarrationJobSpeechPlan()
    {
    }

    private NarrationJobSpeechPlan(
        Guid ownerId,
        Guid seriesId,
        Guid narrationJobId,
        int chapterSortOrder,
        Guid confirmedSpeechPlanRevisionId)
    {
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(narrationJobId, nameof(narrationJobId));
        EnsureId(confirmedSpeechPlanRevisionId, nameof(confirmedSpeechPlanRevisionId));
        if (chapterSortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterSortOrder), "章節排序不可為負數。");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        SeriesId = seriesId;
        NarrationJobId = narrationJobId;
        ChapterSortOrder = chapterSortOrder;
        ConfirmedSpeechPlanRevisionId = confirmedSpeechPlanRevisionId;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid NarrationJobId { get; private set; }
    public int ChapterSortOrder { get; private set; }
    public Guid ConfirmedSpeechPlanRevisionId { get; private set; }

    public static NarrationJobSpeechPlan Create(
        Guid ownerId,
        Guid seriesId,
        Guid narrationJobId,
        int chapterSortOrder,
        Guid confirmedSpeechPlanRevisionId) =>
        new(ownerId, seriesId, narrationJobId, chapterSortOrder, confirmedSpeechPlanRevisionId);

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
