namespace StoryVoice.Application.Insights;

public sealed record SetBookContentLinkRequest(Guid ContentBookId);

public sealed record BookContentLinkResponse(
    Guid BookId,
    Guid ContentBookId,
    string ContentTitle,
    int ChapterCount);

public sealed record SummaryExcerptResponse(
    Guid ChapterId,
    string ChapterTitle,
    int StartOffset,
    int Length,
    string Text);

public sealed record ExtractiveBookSummaryResponse(
    Guid BookId,
    Guid ContentBookId,
    string Kind,
    string Generator,
    string Version,
    string SourceHash,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SummaryExcerptResponse> Excerpts);

public sealed record CharacterCandidateResponse(
    string Name,
    int OccurrenceCount,
    string SampleChapterTitle,
    string? SampleDialogue);

public sealed record CreateReadingNoteRequest(string Body, Guid? ChapterId);

public sealed record UpdateReadingNoteRequest(string Body);

public sealed record ReadingNoteResponse(
    Guid Id,
    Guid BookId,
    Guid? ChapterId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IBookInsightsService
{
    Task<BookContentLinkResponse?> GetContentLinkAsync(Guid bookId, CancellationToken cancellationToken);
    Task<BookContentLinkResponse?> SetContentLinkAsync(Guid bookId, Guid contentBookId, CancellationToken cancellationToken);
    Task<bool> RemoveContentLinkAsync(Guid bookId, CancellationToken cancellationToken);
    Task<ExtractiveBookSummaryResponse?> GetSummaryAsync(Guid bookId, CancellationToken cancellationToken);
    Task<ExtractiveBookSummaryResponse?> GenerateSummaryAsync(Guid bookId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CharacterCandidateResponse>?> ListCharacterCandidatesAsync(Guid bookId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReadingNoteResponse>?> ListNotesAsync(Guid bookId, CancellationToken cancellationToken);
    Task<ReadingNoteResponse?> CreateNoteAsync(Guid bookId, CreateReadingNoteRequest request, CancellationToken cancellationToken);
    Task<ReadingNoteResponse?> UpdateNoteAsync(Guid bookId, Guid noteId, UpdateReadingNoteRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteNoteAsync(Guid bookId, Guid noteId, CancellationToken cancellationToken);
}

public sealed class BookTextUnavailableException(string message) : InvalidOperationException(message)
{
    public const string StableCode = "book_text_unavailable";
}
