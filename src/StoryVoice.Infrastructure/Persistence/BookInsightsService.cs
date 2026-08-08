using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Insights;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Insights;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookInsightsService(
    StoryVoiceDbContext dbContext,
    ICurrentUser currentUser) : IBookInsightsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BookContentLinkResponse?> GetContentLinkAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var target = await OwnedBooks()
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
        if (target?.ContentBookId is not Guid contentBookId)
        {
            return null;
        }

        var content = await OwnedBooks()
            .Include(book => book.Chapters)
            .SingleOrDefaultAsync(book => book.Id == contentBookId, cancellationToken);
        return content is null ? null : ToContentLink(target, content);
    }

    public async Task<BookContentLinkResponse?> SetContentLinkAsync(
        Guid bookId,
        Guid contentBookId,
        CancellationToken cancellationToken)
    {
        var target = await OwnedBooks(tracking: true)
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var content = await OwnedBooks()
            .Include(book => book.Chapters)
            .SingleOrDefaultAsync(book => book.Id == contentBookId, cancellationToken);
        EnsureProcessable(content);

        if (target.ContentBookId != contentBookId)
        {
            target.LinkAuthorizedContent(contentBookId);
            await RemoveExistingSummaryAsync(bookId, cancellationToken);
            await DetachChapterNotesAsync(bookId, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToContentLink(target, content!);
    }

    public async Task<bool> RemoveContentLinkAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var target = await OwnedBooks(tracking: true)
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
        if (target is null)
        {
            return false;
        }

        target.UnlinkAuthorizedContent();
        await RemoveExistingSummaryAsync(bookId, cancellationToken);
        await DetachChapterNotesAsync(bookId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ExtractiveBookSummaryResponse?> GetSummaryAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var targetExists = await OwnedBooks().AnyAsync(book => book.Id == bookId, cancellationToken);
        if (!targetExists)
        {
            return null;
        }

        var summary = await dbContext.BookExtractiveSummaries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.BookId == bookId && item.OwnerId == currentUser.UserId,
                cancellationToken);
        return summary is null ? null : ToResponse(summary);
    }

    public async Task<ExtractiveBookSummaryResponse?> GenerateSummaryAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var target = await OwnedBooks()
            .Include(book => book.Chapters)
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var content = target.ContentBookId is Guid contentBookId
            ? await OwnedBooks().Include(book => book.Chapters)
                .SingleOrDefaultAsync(book => book.Id == contentBookId, cancellationToken)
            : target;
        EnsureProcessable(content);

        var draft = ExtractiveSummaryGenerator.Generate(content!.Chapters);
        var excerptsJson = JsonSerializer.Serialize(draft.Excerpts, JsonOptions);
        var summary = await dbContext.BookExtractiveSummaries
            .SingleOrDefaultAsync(
                item => item.BookId == bookId && item.OwnerId == currentUser.UserId,
                cancellationToken);

        if (summary is null)
        {
            summary = BookExtractiveSummary.Create(
                currentUser.UserId,
                target.Id,
                content.Id,
                draft.SourceHash,
                excerptsJson);
            dbContext.BookExtractiveSummaries.Add(summary);
        }
        else if (!string.Equals(summary.SourceHash, draft.SourceHash, StringComparison.Ordinal)
            || summary.ContentBookId != content.Id
            || !string.Equals(summary.Version, ExtractiveSummaryGenerator.Version, StringComparison.Ordinal))
        {
            summary.Replace(content.Id, draft.SourceHash, excerptsJson);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(summary);
    }

    public async Task<IReadOnlyList<ReadingNoteResponse>?> ListNotesAsync(
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var targetExists = await OwnedBooks().AnyAsync(book => book.Id == bookId, cancellationToken);
        if (!targetExists)
        {
            return null;
        }

        var notes = await dbContext.ReadingNotes
            .AsNoTracking()
            .Where(note => note.OwnerId == currentUser.UserId && note.BookId == bookId)
            .OrderByDescending(note => note.UpdatedAt)
            .ToListAsync(cancellationToken);
        return notes.Select(ToResponse).ToArray();
    }

    public async Task<ReadingNoteResponse?> CreateNoteAsync(
        Guid bookId,
        CreateReadingNoteRequest request,
        CancellationToken cancellationToken)
    {
        var target = await OwnedBooks()
            .SingleOrDefaultAsync(book => book.Id == bookId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        await ValidateChapterAsync(target, request.ChapterId, cancellationToken);
        var note = ReadingNote.Create(currentUser.UserId, bookId, request.ChapterId, request.Body);
        dbContext.ReadingNotes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(note);
    }

    public async Task<ReadingNoteResponse?> UpdateNoteAsync(
        Guid bookId,
        Guid noteId,
        UpdateReadingNoteRequest request,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.ReadingNotes.SingleOrDefaultAsync(
            item => item.Id == noteId
                && item.BookId == bookId
                && item.OwnerId == currentUser.UserId,
            cancellationToken);
        if (note is null)
        {
            return null;
        }

        note.Update(request.Body);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(note);
    }

    public async Task<bool> DeleteNoteAsync(
        Guid bookId,
        Guid noteId,
        CancellationToken cancellationToken)
    {
        var note = await dbContext.ReadingNotes.SingleOrDefaultAsync(
            item => item.Id == noteId
                && item.BookId == bookId
                && item.OwnerId == currentUser.UserId,
            cancellationToken);
        if (note is null)
        {
            return false;
        }

        dbContext.ReadingNotes.Remove(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<Book> OwnedBooks(bool tracking = false)
    {
        var query = dbContext.Books.Where(book => book.OwnerId == currentUser.UserId);
        return tracking ? query : query.AsNoTracking();
    }

    private static void EnsureProcessable(Book? book)
    {
        var supportedFileType = string.Equals(book?.FileType, "epub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(book?.FileType, "txt", StringComparison.OrdinalIgnoreCase);
        if (book is null
            || book.Status != BookStatus.Uploaded
            || book.SourceProvider is not null
            || string.IsNullOrWhiteSpace(book.StoragePath)
            || !supportedFileType
            || !book.Chapters.Any(chapter => !string.IsNullOrWhiteSpace(chapter.OriginalText)))
        {
            throw new BookTextUnavailableException(
                "這本書目前沒有可處理的合法正文；請先上傳你合法持有、無 DRM 的 EPUB 或 TXT，並明確連結後再使用正文功能。");
        }
    }

    private async Task ValidateChapterAsync(
        Book target,
        Guid? chapterId,
        CancellationToken cancellationToken)
    {
        if (chapterId is null)
        {
            return;
        }

        var contentBookId = target.ContentBookId ?? target.Id;
        var content = await OwnedBooks()
            .Include(book => book.Chapters)
            .SingleOrDefaultAsync(book => book.Id == contentBookId, cancellationToken);
        EnsureProcessable(content);
        var belongsToAuthorizedContent = content!.Chapters.Any(chapter => chapter.Id == chapterId);
        if (!belongsToAuthorizedContent)
        {
            throw new ArgumentException("章節不屬於這本書已連結的合法正文。", nameof(chapterId));
        }
    }

    private async Task RemoveExistingSummaryAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var summary = await dbContext.BookExtractiveSummaries
            .SingleOrDefaultAsync(item => item.BookId == bookId, cancellationToken);
        if (summary is not null)
        {
            dbContext.BookExtractiveSummaries.Remove(summary);
        }
    }

    private async Task DetachChapterNotesAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var notes = await dbContext.ReadingNotes
            .Where(note => note.OwnerId == currentUser.UserId
                && note.BookId == bookId
                && note.ChapterId != null)
            .ToListAsync(cancellationToken);
        foreach (var note in notes)
        {
            note.DetachChapter();
        }
    }

    private static BookContentLinkResponse ToContentLink(Book target, Book content) =>
        new(target.Id, content.Id, content.Title, content.Chapters.Count);

    private static ReadingNoteResponse ToResponse(ReadingNote note) =>
        new(note.Id, note.BookId, note.ChapterId, note.Body, note.CreatedAt, note.UpdatedAt);

    private static ExtractiveBookSummaryResponse ToResponse(BookExtractiveSummary summary) =>
        new(
            summary.BookId,
            summary.ContentBookId,
            summary.Kind,
            summary.Generator,
            summary.Version,
            summary.SourceHash,
            summary.GeneratedAt,
            JsonSerializer.Deserialize<SummaryExcerptResponse[]>(summary.ExcerptsJson, JsonOptions) ?? []);
}
