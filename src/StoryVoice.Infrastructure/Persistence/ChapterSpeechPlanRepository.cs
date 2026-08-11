using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class ChapterSpeechPlanRepository(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : IChapterSpeechPlanRepository
{
    public Task AddAsync(ChapterSpeechPlanDraft draft, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.OwnerId != ownerId)
        {
            throw new InvalidOperationException("Cannot add a speech plan draft owned by another user.");
        }

        return dbContext.ChapterSpeechPlanDrafts.AddAsync(draft, cancellationToken).AsTask();
    }

    public async Task<ChapterSpeechPlanDraft?> GetForMutationByChapterAsync(
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        return await dbContext.ChapterSpeechPlanDrafts
            .AsSplitQuery()
            .Include(draft => draft.Segments)
            .SingleOrDefaultAsync(
                draft => draft.OwnerId == ownerId
                    && draft.SeriesId == seriesId
                    && draft.BookId == bookId
                    && draft.ChapterId == chapterId,
                cancellationToken);
    }

    public async Task<ChapterSpeechPlanDraft?> GetForMutationByIdAsync(
        Guid seriesId,
        Guid draftId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        return await dbContext.ChapterSpeechPlanDrafts
            .AsSplitQuery()
            .Include(draft => draft.Segments)
            .SingleOrDefaultAsync(
                draft => draft.OwnerId == ownerId && draft.SeriesId == seriesId && draft.Id == draftId,
                cancellationToken);
    }

    public Task AddConfirmedRevisionAsync(
        ConfirmedSpeechPlanRevision revision,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.OwnerId != ownerId)
        {
            throw new InvalidOperationException("Cannot add a confirmed speech plan owned by another user.");
        }

        return dbContext.ConfirmedSpeechPlanRevisions.AddAsync(revision, cancellationToken).AsTask();
    }

    public async Task<int> GetNextRevisionNumberAsync(
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var maxRevisionNumber = await dbContext.ConfirmedSpeechPlanRevisions
            .AsNoTracking()
            .Where(revision => revision.OwnerId == ownerId
                && revision.SeriesId == seriesId
                && revision.BookId == bookId
                && revision.ChapterId == chapterId)
            .Select(revision => (int?)revision.RevisionNumber)
            .MaxAsync(cancellationToken);
        return (maxRevisionNumber ?? 0) + 1;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        EnsureCurrentOwnerId();
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("A current user is required for speech plan persistence.");
        }

        return currentUser.UserId;
    }
}
