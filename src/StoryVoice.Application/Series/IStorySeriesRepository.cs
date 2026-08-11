using StoryVoice.Domain.Series;

namespace StoryVoice.Application.Series;

public interface IStorySeriesRepository
{
    Task AddAsync(StorySeries series, CancellationToken cancellationToken);

    Task<StorySeries?> GetForMutationAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
