using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Books;

public static class AuthorizedTextPolicy
{
    public static bool IsProcessable(Book? book) =>
        book is not null
        && book.Status == BookStatus.Uploaded
        && book.SourceProvider is null
        && !string.IsNullOrWhiteSpace(book.StoragePath)
        && (string.Equals(book.FileType, "epub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(book.FileType, "txt", StringComparison.OrdinalIgnoreCase))
        && book.Chapters.Any(chapter => !string.IsNullOrWhiteSpace(chapter.OriginalText));
}
