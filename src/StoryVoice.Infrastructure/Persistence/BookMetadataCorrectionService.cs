using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Books;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookMetadataCorrectionService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : IBookMetadataCorrectionService
{
    public async Task<BookDetailsResponse?> UpdateAsync(
        Guid bookId,
        UpdateBookMetadataCorrectionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var book = await dbContext.Books
            .Include(candidate => candidate.Chapters)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookId && candidate.OwnerId == currentUser.UserId,
                cancellationToken);
        if (book is null)
        {
            return null;
        }

        book.SetMetadataCorrections(request.Title, request.Author, request.CoverImageUrl);
        await dbContext.SaveChangesAsync(cancellationToken);
        return BookResponseMapper.ToDetails(book);
    }
}
