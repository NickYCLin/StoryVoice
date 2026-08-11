using StoryVoice.Domain.Narrations;

namespace StoryVoice.Application.Narrations.SpeechPlanning;

public interface IChapterSpeechPlanRepository
{
    Task AddAsync(ChapterSpeechPlanDraft draft, CancellationToken cancellationToken);

    Task<ChapterSpeechPlanDraft?> GetForMutationByChapterAsync(
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        CancellationToken cancellationToken);

    Task<ChapterSpeechPlanDraft?> GetForMutationByIdAsync(
        Guid seriesId,
        Guid draftId,
        CancellationToken cancellationToken);

    Task AddConfirmedRevisionAsync(
        ConfirmedSpeechPlanRevision revision,
        CancellationToken cancellationToken);

    Task<int> GetNextRevisionNumberAsync(
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
