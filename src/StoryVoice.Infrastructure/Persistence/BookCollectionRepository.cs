using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Collections;
using StoryVoice.Domain.Collections;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookCollectionRepository(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : IBookCollectionRepository
{
    public Task AddAsync(BookCollection collection, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        ArgumentNullException.ThrowIfNull(collection);
        if (collection.OwnerId != ownerId)
        {
            throw new InvalidOperationException("Cannot add a book collection owned by another user.");
        }

        return dbContext.BookCollections.AddAsync(collection, cancellationToken).AsTask();
    }

    public async Task<BookCollection?> GetForMutationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Book collection id cannot be empty.", nameof(id));
        }

        return await dbContext.BookCollections
            .AsSplitQuery()
            .Include(candidate => candidate.Books)
            .Include(candidate => candidate.Shares)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.OwnerId == ownerId,
                cancellationToken);
    }

    public void Remove(BookCollection collection)
    {
        EnsureCurrentOwnerId();
        ArgumentNullException.ThrowIfNull(collection);
        dbContext.BookCollections.Remove(collection);
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
            throw new InvalidOperationException("A current user is required for book collection persistence.");
        }

        return currentUser.UserId;
    }
}
