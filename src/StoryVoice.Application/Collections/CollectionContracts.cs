namespace StoryVoice.Application.Collections;

public sealed record CreateBookCollectionRequest(string Name, string? Description);

public sealed record UpdateBookCollectionRequest(string Name, string? Description);

public sealed record AddCollectionBookRequest(Guid BookId, string? VolumeLabel, int SortOrder);

public sealed record UpdateCollectionBookRequest(string? VolumeLabel, int SortOrder);

public sealed record AddCollectionShareRequest(string Email);

public sealed record BookCollectionSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    int BookCount,
    int ShareCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BookCollectionBookResponse(
    Guid Id,
    Guid BookId,
    string BookTitle,
    string BookAuthor,
    string? CoverImageUrl,
    string? VolumeLabel,
    int SortOrder);

public sealed record CollectionShareResponse(
    Guid Id,
    string GranteeEmail,
    DateTimeOffset CreatedAt);

public sealed record BookCollectionDetailsResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<BookCollectionBookResponse> Books,
    IReadOnlyList<CollectionShareResponse> Shares,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SharedCollectionSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    string OwnerEmail,
    int BookCount,
    DateTimeOffset SharedAt);

public sealed record SharedCollectionBookResponse(
    Guid Id,
    string BookTitle,
    string BookAuthor,
    string? CoverImageUrl,
    string? VolumeLabel,
    int SortOrder);

public sealed record SharedCollectionDetailsResponse(
    Guid Id,
    string Name,
    string? Description,
    string OwnerEmail,
    IReadOnlyList<SharedCollectionBookResponse> Books,
    DateTimeOffset SharedAt);

public sealed record SharedChapterResponse(
    Guid Id,
    int ChapterNumber,
    string Title,
    string OriginalText);

public sealed record SharedCollectionBookContentResponse(
    Guid Id,
    string BookTitle,
    string BookAuthor,
    string? VolumeLabel,
    IReadOnlyList<SharedChapterResponse> Chapters);
