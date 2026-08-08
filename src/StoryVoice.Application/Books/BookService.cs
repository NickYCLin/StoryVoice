using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Books;

public sealed class BookService(IBookRepository repository) : IBookService
{
    public async Task<BookDetailsResponse> CreateAsync(
        CreateBookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await CreateCoreAsync(
            request.Title,
            request.Author,
            request.Language,
            request.OriginalFileName,
            storagePath: null,
            request.Chapters,
            cancellationToken);
    }

    public async Task<BookDetailsResponse> CreateImportedAsync(
        CreateImportedBookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await CreateCoreAsync(
            request.Title,
            request.Author,
            request.Language,
            request.OriginalFileName,
            request.StoragePath,
            request.Chapters,
            cancellationToken);
    }

    private async Task<BookDetailsResponse> CreateCoreAsync(
        string title,
        string author,
        string language,
        string originalFileName,
        string? storagePath,
        IReadOnlyList<CreateChapterRequest>? chapters,
        CancellationToken cancellationToken)
    {
        var book = Book.Create(title, author, language, originalFileName);
        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            book.SetStoragePath(storagePath);
        }

        foreach (var chapter in chapters ?? [])
        {
            book.AddChapter(chapter.ChapterNumber, chapter.Title, chapter.OriginalText);
        }

        await repository.AddAsync(book, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return ToDetails(book);
    }

    public async Task<BookDetailsResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var book = await repository.GetAsync(id, cancellationToken);
        return book is null ? null : ToDetails(book);
    }

    public async Task<IReadOnlyList<BookSummaryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var books = await repository.ListAsync(cancellationToken);
        return books.Select(ToSummary).ToArray();
    }

    private static BookSummaryResponse ToSummary(Book book) => new(
        book.Id,
        book.Title,
        book.Author,
        book.Language,
        book.FileType,
        book.Status.ToString(),
        book.Chapters.Count,
        book.CreatedAt);

    private static BookDetailsResponse ToDetails(Book book) => new(
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
            .ToArray());
}
