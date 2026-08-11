using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Collections;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Collections;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class CollectionService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser,
    IBookCollectionRepository repository,
    UserManager<ApplicationUser> userManager) : ICollectionService
{
    public async Task<IReadOnlyList<BookCollectionSummaryResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        return await dbContext.BookCollections
            .AsNoTracking()
            .Where(collection => collection.OwnerId == ownerId)
            .OrderBy(collection => collection.Name)
            .Select(collection => new BookCollectionSummaryResponse(
                collection.Id,
                collection.Name,
                collection.Description,
                collection.Books.Count,
                collection.Shares.Count,
                collection.CreatedAt,
                collection.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse?> GetAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        var ownerId = EnsureCurrentOwnerId();
        var collection = await dbContext.BookCollections
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Books)
            .Include(candidate => candidate.Shares)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == collectionId && candidate.OwnerId == ownerId,
                cancellationToken);
        return collection is null ? null : await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse> CreateAsync(
        CreateBookCollectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var collection = BookCollection.Create(ownerId, request.Name, request.Description);
        if (await dbContext.BookCollections.AsNoTracking().AnyAsync(
                candidate => candidate.OwnerId == ownerId
                    && candidate.NormalizedName == collection.NormalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException("同一位使用者不可建立名稱相同的書冊。");
        }

        await repository.AddAsync(collection, cancellationToken);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse?> UpdateAsync(
        Guid collectionId,
        UpdateBookCollectionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        ArgumentNullException.ThrowIfNull(request);
        var ownerId = EnsureCurrentOwnerId();
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        collection.Rename(request.Name);
        if (await dbContext.BookCollections.AsNoTracking().AnyAsync(
                candidate => candidate.Id != collectionId
                    && candidate.OwnerId == ownerId
                    && candidate.NormalizedName == collection.NormalizedName,
                cancellationToken))
        {
            throw new InvalidOperationException("同一位使用者不可建立名稱相同的書冊。");
        }

        collection.UpdateDescription(request.Description);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return false;
        }

        repository.Remove(collection);
        await SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<BookCollectionDetailsResponse?> AddBookAsync(
        Guid collectionId,
        AddCollectionBookRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        ArgumentNullException.ThrowIfNull(request);
        EnsureId(request.BookId, nameof(request.BookId));
        var ownerId = EnsureCurrentOwnerId();
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        var book = await dbContext.Books.SingleOrDefaultAsync(
            candidate => candidate.Id == request.BookId && candidate.OwnerId == ownerId,
            cancellationToken);
        if (book is null || book.Status == BookStatus.Linked)
        {
            return null;
        }

        collection.AddBook(book, request.VolumeLabel, request.SortOrder);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse?> UpdateBookAsync(
        Guid collectionId,
        Guid bookId,
        UpdateCollectionBookRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        EnsureId(bookId, nameof(bookId));
        ArgumentNullException.ThrowIfNull(request);
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null || collection.Books.All(book => book.BookId != bookId))
        {
            return null;
        }

        collection.UpdateBook(bookId, request.VolumeLabel, request.SortOrder);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse?> RemoveBookAsync(
        Guid collectionId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        EnsureId(bookId, nameof(bookId));
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null || collection.Books.All(book => book.BookId != bookId))
        {
            return null;
        }

        collection.RemoveBook(bookId);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse?> AddShareAsync(
        Guid collectionId,
        AddCollectionShareRequest request,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        ArgumentNullException.ThrowIfNull(request);
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        var email = request.Email?.Trim();
        var grantee = string.IsNullOrWhiteSpace(email)
            ? null
            : await userManager.FindByEmailAsync(email);
        if (grantee is null)
        {
            throw new InvalidOperationException("找不到這個電子郵件對應的使用者。");
        }

        if (grantee.Id == collection.OwnerId)
        {
            throw new InvalidOperationException("不可以把書冊分享給自己。");
        }

        collection.AddShare(grantee.Id, grantee.Email ?? email!);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    public async Task<BookCollectionDetailsResponse?> RevokeShareAsync(
        Guid collectionId,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        EnsureId(collectionId, nameof(collectionId));
        EnsureId(shareId, nameof(shareId));
        var collection = await repository.GetForMutationAsync(collectionId, cancellationToken);
        if (collection is null || collection.Shares.All(share => share.Id != shareId))
        {
            return null;
        }

        collection.RevokeShare(shareId);
        await SaveChangesAsync(cancellationToken);
        return await ToDetailsAsync(collection, cancellationToken);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("書冊資料與既有資料衝突，請重新整理後再試。", exception);
        }
    }

    private async Task<BookCollectionDetailsResponse> ToDetailsAsync(
        BookCollection collection,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        var bookIds = collection.Books.Select(book => book.BookId).ToArray();
        var bookInfo = await dbContext.Books
            .AsNoTracking()
            .Where(book => book.OwnerId == ownerId && bookIds.Contains(book.Id))
            .Select(book => new { book.Id, book.Title, book.Author, book.CoverImageUrl })
            .ToDictionaryAsync(book => book.Id, cancellationToken);

        return new BookCollectionDetailsResponse(
            collection.Id,
            collection.Name,
            collection.Description,
            collection.Books
                .OrderBy(book => book.SortOrder)
                .Select(book =>
                {
                    var info = bookInfo.GetValueOrDefault(book.BookId);
                    return new BookCollectionBookResponse(
                        book.Id,
                        book.BookId,
                        info?.Title ?? "未知書籍",
                        info?.Author ?? string.Empty,
                        info?.CoverImageUrl,
                        book.VolumeLabel,
                        book.SortOrder);
                })
                .ToArray(),
            collection.Shares
                .OrderBy(share => share.GranteeEmail, StringComparer.Ordinal)
                .Select(share => new CollectionShareResponse(share.Id, share.GranteeEmail, share.CreatedAt))
                .ToArray(),
            collection.CreatedAt,
            collection.UpdatedAt);
    }

    private Guid EnsureCurrentOwnerId()
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("書冊管理需要已驗證的使用者。");
        }

        return currentUser.UserId;
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
