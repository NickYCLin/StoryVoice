using StoryVoice.Application.Books;

namespace StoryVoice.Application.Bookshelves;

public sealed record BooksComTwBookMetadata(
    string ExternalId,
    string Title,
    string? Author,
    string? Language,
    string SourceUrl,
    string? CoverImageUrl);

public sealed record BooksComTwBookshelfImportRequest(
    IReadOnlyList<BooksComTwBookMetadata> Books);

public sealed record BooksComTwBookshelfImportResponse(
    int CreatedCount,
    int UpdatedCount,
    IReadOnlyList<BookDetailsResponse> Books);
