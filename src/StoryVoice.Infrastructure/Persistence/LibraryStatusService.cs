using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Library;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

public sealed class LibraryStatusService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : ILibraryStatusService
{
    public async Task<IReadOnlyList<LibraryBookStatusResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        var books = await dbContext.Books
            .AsNoTracking()
            .Where(book => book.OwnerId == currentUser.UserId)
            .ToListAsync(cancellationToken);
        var bookIds = books.Select(book => book.Id).ToArray();
        var processableIds = (await dbContext.Books
            .AsNoTracking()
            .Where(book => book.OwnerId == currentUser.UserId
                && book.Status == BookStatus.Uploaded
                && book.SourceProvider == null
                && book.StoragePath != null
                && book.StoragePath != string.Empty
                && (book.FileType == "epub" || book.FileType == "txt")
                && book.Chapters.Any(chapter => chapter.OriginalText.Trim() != string.Empty))
            .Select(book => book.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var summaryIds = (await dbContext.BookExtractiveSummaries
            .AsNoTracking()
            .Where(summary => summary.OwnerId == currentUser.UserId && bookIds.Contains(summary.BookId))
            .Select(summary => summary.BookId)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var noteBookIds = await dbContext.ReadingNotes
            .AsNoTracking()
            .Where(note => note.OwnerId == currentUser.UserId && bookIds.Contains(note.BookId))
            .Select(note => note.BookId)
            .ToListAsync(cancellationToken);
        var noteCounts = noteBookIds
            .GroupBy(bookId => bookId)
            .ToDictionary(group => group.Key, group => group.Count());
        var narrationJobs = await dbContext.NarrationJobs
            .AsNoTracking()
            .Where(job => job.OwnerId == currentUser.UserId && bookIds.Contains(job.BookId))
            .Select(job => new
            {
                job.BookId,
                job.ContentBookId,
                job.Status,
                job.CreatedAt,
                job.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        var narrationsByBook = narrationJobs
            .GroupBy(job => job.BookId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return books
            .Select(book =>
            {
                Guid? authorizedContentBookId = processableIds.Contains(book.Id)
                    ? book.Id
                    : book.ContentBookId is Guid contentBookId && processableIds.Contains(contentBookId)
                        ? contentBookId
                        : null;
                var authorizedText = authorizedContentBookId.HasValue;
                narrationsByBook.TryGetValue(book.Id, out var bookNarrations);
                var selectedNarration = authorizedContentBookId is Guid currentContentBookId
                    ? bookNarrations?
                        .Where(job => job.ContentBookId == currentContentBookId)
                        .OrderBy(job => job.Status is NarrationJobStatus.Queued or NarrationJobStatus.Running ? 0 : 1)
                        .ThenByDescending(job => job.UpdatedAt)
                        .ThenByDescending(job => job.CreatedAt)
                        .FirstOrDefault()
                    : null;
                selectedNarration ??= bookNarrations?
                    .OrderByDescending(job => job.UpdatedAt)
                    .ThenByDescending(job => job.CreatedAt)
                    .FirstOrDefault();
                var narrationStatus = selectedNarration?.Status;
                var narrationMatchesAuthorizedText = authorizedContentBookId.HasValue
                    && selectedNarration?.ContentBookId == authorizedContentBookId.Value;
                var state = narrationStatus switch
                {
                    NarrationJobStatus.Completed => "audio_ready",
                    NarrationJobStatus.Queued or NarrationJobStatus.Running => "processing",
                    NarrationJobStatus.Failed => "failed",
                    _ => authorizedText ? "ready" : "blocked"
                };
                var blockedReason = !authorizedText
                    ? "authorized_text_required"
                    : narrationStatus == NarrationJobStatus.Failed && narrationMatchesAuthorizedText
                        ? "narration_failed"
                        : null;

                return new LibraryBookStatusResponse(
                    book.Id,
                    book.TitleCorrection ?? book.Title,
                    book.SourceProvider ?? "uploaded",
                    !string.IsNullOrWhiteSpace(book.SourceUrl),
                    book.NativeTtsAvailable,
                    authorizedText,
                    summaryIds.Contains(book.Id),
                    noteCounts.GetValueOrDefault(book.Id),
                    narrationStatus?.ToString(),
                    narrationMatchesAuthorizedText,
                    state,
                    blockedReason);
            })
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
