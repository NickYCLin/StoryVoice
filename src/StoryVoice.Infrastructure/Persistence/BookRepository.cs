using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Books;
using StoryVoice.Domain.Books;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookRepository(StoryVoiceDbContext dbContext) : IBookRepository
{
    public Task AddAsync(Book book, CancellationToken cancellationToken) =>
        dbContext.Books.AddAsync(book, cancellationToken).AsTask();

    public Task<Book?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Books
            .AsNoTracking()
            .Include(book => book.Chapters)
            .SingleOrDefaultAsync(book => book.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Book>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Books
            .AsNoTracking()
            .Include(book => book.Chapters)
            .OrderByDescending(book => book.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
