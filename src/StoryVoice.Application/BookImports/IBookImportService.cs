using StoryVoice.Application.Books;

namespace StoryVoice.Application.BookImports;

public interface IBookImportService
{
    Task<BookDetailsResponse> ImportAsync(
        Stream content,
        string fileName,
        string? title,
        string? author,
        string? language,
        CancellationToken cancellationToken);
}
