namespace StoryVoice.Application.Books;

public sealed record CreateChapterRequest(int ChapterNumber, string Title, string OriginalText);

public sealed record CreateBookRequest(
    string Title,
    string Author,
    string Language,
    string OriginalFileName,
    IReadOnlyList<CreateChapterRequest> Chapters);

public sealed record CreateImportedBookRequest(
    string Title,
    string Author,
    string Language,
    string OriginalFileName,
    string StoragePath,
    IReadOnlyList<CreateChapterRequest> Chapters);

public sealed record ChapterResponse(
    Guid Id,
    int ChapterNumber,
    int SortOrder,
    string Title,
    string OriginalText);

public sealed record BookSummaryResponse(
    Guid Id,
    string Title,
    string Author,
    string Language,
    string FileType,
    string Status,
    int ChapterCount,
    DateTimeOffset CreatedAt,
    string? SourceProvider,
    string? ExternalSourceId,
    string? SourceUrl,
    string? CoverImageUrl,
    DateTimeOffset? SourceSyncedAt);

public sealed record BookDetailsResponse(
    Guid Id,
    string Title,
    string Author,
    string Language,
    string OriginalFileName,
    string FileType,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ChapterResponse> Chapters,
    string? SourceProvider,
    string? ExternalSourceId,
    string? SourceUrl,
    string? CoverImageUrl,
    DateTimeOffset? SourceSyncedAt);
