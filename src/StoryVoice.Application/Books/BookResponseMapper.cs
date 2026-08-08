using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Books;

internal static class BookResponseMapper
{
    public static BookSummaryResponse ToSummary(Book book) => new(
        book.Id,
        book.Title,
        book.Author,
        book.Language,
        book.FileType,
        book.Status.ToString(),
        book.Chapters.Count,
        book.CreatedAt,
        book.SourceProvider,
        book.ExternalSourceId,
        book.SourceUrl,
        book.CoverImageUrl,
        book.SourceSyncedAt);

    public static BookDetailsResponse ToDetails(Book book) => new(
        book.Id,
        book.Title,
        book.Author,
        book.Language,
        book.OriginalFileName,
        book.FileType,
        book.Status.ToString(),
        book.CreatedAt,
        book.Chapters
            .OrderBy(chapter => chapter.SortOrder)
            .Select(chapter => new ChapterResponse(
                chapter.Id,
                chapter.ChapterNumber,
                chapter.SortOrder,
                chapter.Title,
                chapter.OriginalText))
            .ToArray(),
        book.SourceProvider,
        book.ExternalSourceId,
        book.SourceUrl,
        book.CoverImageUrl,
        book.SourceSyncedAt);
}
