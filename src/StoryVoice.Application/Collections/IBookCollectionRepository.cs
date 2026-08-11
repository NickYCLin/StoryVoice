using StoryVoice.Domain.Collections;

namespace StoryVoice.Application.Collections;

public interface IBookCollectionRepository
{
    Task AddAsync(BookCollection collection, CancellationToken cancellationToken);

    Task<BookCollection?> GetForMutationAsync(Guid id, CancellationToken cancellationToken);

    void Remove(BookCollection collection);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
