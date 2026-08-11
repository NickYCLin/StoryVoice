namespace StoryVoice.Application.Narrations.SpeechPlanning;

public interface ISpeechPlanService
{
    /// <summary>
    /// Builds a chapter's first draft, or regenerates it (as a new <c>PlanVersion</c>) if the
    /// chapter's title/body no longer matches the draft's <c>SourceHash</c>. Idempotent when the
    /// chapter has not changed and a draft already exists.
    /// </summary>
    Task<ChapterSpeechPlanDraftResponse?> BuildOrRefreshDraftAsync(
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        CancellationToken cancellationToken);

    Task<ChapterSpeechPlanDraftResponse?> GetDraftAsync(
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        CancellationToken cancellationToken);

    Task<ChapterSpeechPlanDraftResponse?> ConfirmSegmentAsync(
        Guid seriesId,
        Guid draftId,
        Guid segmentId,
        ConfirmSpeechSegmentRequest request,
        CancellationToken cancellationToken);

    Task<ChapterSpeechPlanDraftResponse?> RejectSegmentAsync(
        Guid seriesId,
        Guid draftId,
        Guid segmentId,
        CancellationToken cancellationToken);

    Task<ConfirmedSpeechPlanRevisionResponse?> ConfirmPlanAsync(
        Guid seriesId,
        Guid draftId,
        CancellationToken cancellationToken);
}
