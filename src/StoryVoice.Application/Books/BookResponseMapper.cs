using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Books;

public static class BookResponseMapper
{
    public static BookSummaryResponse ToSummary(Book book) => new(
        book.Id,
        book.TitleCorrection ?? book.Title,
        book.AuthorCorrection ?? book.Author,
        book.Language,
        book.FileType,
        book.Status.ToString(),
        book.Chapters.Count,
        book.CreatedAt,
        book.SourceProvider,
        book.ExternalSourceId,
        book.SourceUrl,
        book.CoverImageUrlCorrection ?? book.CoverImageUrl,
        book.NativeTtsAvailable,
        book.EbookLayout?.ToString(),
        book.SourceSyncedAt,
        book.ContentBookId,
        HasAuthorizedText(book),
        book.TitleCorrection,
        book.AuthorCorrection,
        book.CoverImageUrlCorrection);

    public static BookDetailsResponse ToDetails(Book book) => new(
        book.Id,
        book.TitleCorrection ?? book.Title,
        book.AuthorCorrection ?? book.Author,
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
        book.CoverImageUrlCorrection ?? book.CoverImageUrl,
        book.NativeTtsAvailable,
        book.EbookLayout?.ToString(),
        book.SourceSyncedAt,
        book.ContentBookId,
        HasAuthorizedText(book),
        book.TitleCorrection,
        book.AuthorCorrection,
        book.CoverImageUrlCorrection);

    private static bool HasAuthorizedText(Book book) =>
        book.Status == BookStatus.Uploaded
        && book.SourceProvider is null
        && !string.IsNullOrWhiteSpace(book.StoragePath)
        && (string.Equals(book.FileType, "epub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(book.FileType, "txt", StringComparison.OrdinalIgnoreCase))
        && book.Chapters.Any(chapter => !string.IsNullOrWhiteSpace(chapter.OriginalText));
}
