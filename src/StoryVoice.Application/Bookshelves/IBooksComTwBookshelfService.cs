namespace StoryVoice.Application.Bookshelves;

public interface IBooksComTwBookshelfService
{
    Task<BooksComTwBookshelfImportResponse> ImportAsync(
        BooksComTwBookshelfImportRequest request,
        CancellationToken cancellationToken);
}
