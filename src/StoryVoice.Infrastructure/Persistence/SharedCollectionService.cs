using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Collections;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SharedCollectionService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : ISharedCollectionService
{
    public async Task<IReadOnlyList<SharedCollectionSummaryResponse>> ListSharedWithMeAsync(
        CancellationToken cancellationToken)
    {
        var granteeId = EnsureCurrentUserId();
        var shares = await dbContext.CollectionShares
            .AsNoTracking()
            .Where(share => share.GranteeUserId == granteeId)
            .Select(share => new { share.CollectionId, share.CreatedAt })
            .ToListAsync(cancellationToken);
        if (shares.Count == 0)
        {
            return [];
        }

        var collectionIds = shares.Select(share => share.CollectionId).ToArray();
        var collections = await dbContext.BookCollections
            .AsNoTracking()
            .Where(collection => collectionIds.Contains(collection.Id))
            .Select(collection => new
            {
                collection.Id,
                collection.Name,
                collection.Description,
                collection.OwnerId,
                BookCount = collection.Books.Count
            })
            .ToDictionaryAsync(collection => collection.Id, cancellationToken);
        var ownerEmails = await ResolveOwnerEmailsAsync(
            collections.Values.Select(collection => collection.OwnerId),
            cancellationToken);

        return shares
            .Where(share => collections.ContainsKey(share.CollectionId))
            .Select(share =>
            {
                var collection = collections[share.CollectionId];
                return new SharedCollectionSummaryResponse(
                    collection.Id,
                    collection.Name,
                    collection.Description,
                    ownerEmails.GetValueOrDefault(collection.OwnerId, "未知使用者"),
                    collection.BookCount,
                    share.CreatedAt);
            })
            .OrderByDescending(summary => summary.SharedAt)
            .ToArray();
    }

    public async Task<SharedCollectionDetailsResponse?> GetSharedAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var granteeId = EnsureCurrentUserId();
        var share = await dbContext.CollectionShares
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CollectionId == collectionId && candidate.GranteeUserId == granteeId,
                cancellationToken);
        if (share is null)
        {
            return null;
        }

        var collection = await dbContext.BookCollections
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Books)
            .SingleOrDefaultAsync(candidate => candidate.Id == collectionId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        var bookIds = collection.Books.Select(book => book.BookId).ToArray();
        var bookInfo = await dbContext.Books
            .AsNoTracking()
            .Where(book => bookIds.Contains(book.Id))
            .Select(book => new { book.Id, book.Title, book.Author, book.CoverImageUrl })
            .ToDictionaryAsync(book => book.Id, cancellationToken);
        var ownerEmails = await ResolveOwnerEmailsAsync([collection.OwnerId], cancellationToken);

        return new SharedCollectionDetailsResponse(
            collection.Id,
            collection.Name,
            collection.Description,
            ownerEmails.GetValueOrDefault(collection.OwnerId, "未知使用者"),
            collection.Books
                .OrderBy(book => book.SortOrder)
                .Select(book =>
                {
                    var info = bookInfo.GetValueOrDefault(book.BookId);
                    return new SharedCollectionBookResponse(
                        book.Id,
                        info?.Title ?? "未知書籍",
                        info?.Author ?? string.Empty,
                        info?.CoverImageUrl,
                        book.VolumeLabel,
                        book.SortOrder);
                })
                .ToArray(),
            share.CreatedAt);
    }

    public async Task<SharedCollectionBookContentResponse?> GetSharedBookContentAsync(
        Guid collectionId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var granteeId = EnsureCurrentUserId();
        var isShared = await dbContext.CollectionShares.AsNoTracking().AnyAsync(
            share => share.CollectionId == collectionId && share.GranteeUserId == granteeId,
            cancellationToken);
        if (!isShared)
        {
            return null;
        }

        var membership = await dbContext.BookCollectionBooks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                book => book.CollectionId == collectionId && book.BookId == bookId,
                cancellationToken);
        if (membership is null)
        {
            return null;
        }

        var book = await dbContext.Books
            .AsNoTracking()
            .Include(candidate => candidate.Chapters)
            .SingleOrDefaultAsync(candidate => candidate.Id == bookId, cancellationToken);
        if (book is null)
        {
            return null;
        }

        return new SharedCollectionBookContentResponse(
            book.Id,
            book.Title,
            book.Author,
            membership.VolumeLabel,
            book.Chapters
                .OrderBy(chapter => chapter.SortOrder)
                .Select(chapter => new SharedChapterResponse(
                    chapter.Id,
                    chapter.ChapterNumber,
                    chapter.Title,
                    chapter.OriginalText))
                .ToArray());
    }

    private async Task<Dictionary<Guid, string>> ResolveOwnerEmailsAsync(
        IEnumerable<Guid> ownerIds,
        CancellationToken cancellationToken)
    {
        var distinctOwnerIds = ownerIds.Distinct().ToArray();
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => distinctOwnerIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email })
            .ToDictionaryAsync(
                user => user.Id,
                user => user.Email ?? "未知使用者",
                cancellationToken);
    }

    private Guid EnsureCurrentUserId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("查看分享書冊需要已驗證的使用者。");
        }

        return currentUser.UserId;
    }
}
