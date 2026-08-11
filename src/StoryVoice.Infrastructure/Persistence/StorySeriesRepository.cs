using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class StorySeriesRepository(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : IStorySeriesRepository
{
    public Task AddAsync(StorySeries series, CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        ArgumentNullException.ThrowIfNull(series);
        if (series.OwnerId != ownerId)
        {
            throw new InvalidOperationException("Cannot add a story series owned by another user.");
        }

        return dbContext.StorySeries.AddAsync(series, cancellationToken).AsTask();
    }

    public async Task<StorySeries?> GetForMutationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ownerId = EnsureCurrentOwnerId();
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Story series id cannot be empty.", nameof(id));
        }

        var series = await dbContext.StorySeries
            .AsSplitQuery()
            .Include(candidate => candidate.Books)
            .Include(candidate => candidate.Characters)
            .Include(candidate => candidate.IdentityKeys)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.OwnerId == ownerId,
                cancellationToken);
        series?.MarkMutationAggregateComplete();
        return series;
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
            throw new InvalidOperationException("A current user is required for story series persistence.");
        }

        return currentUser.UserId;
    }
}
