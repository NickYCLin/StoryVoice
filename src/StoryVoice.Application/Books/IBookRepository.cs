using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Books;

public interface IBookRepository
{
    Task AddAsync(Book book, CancellationToken cancellationToken);

    Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
