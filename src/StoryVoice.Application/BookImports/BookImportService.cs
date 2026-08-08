using StoryVoice.Application.Books;

namespace StoryVoice.Application.BookImports;

public sealed class BookImportService(
    IEnumerable<IBookImportParser> parsers,
    IBookFileStorage fileStorage,
    IBookService bookService) : IBookImportService
{
    private const int MaximumFileBytes = 10 * 1024 * 1024;

    public async Task<BookDetailsResponse> ImportAsync(
        Stream content,
        string fileName,
        string? title,
        string? author,
        string? language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException("檔案名稱不可為空白。", nameof(fileName));
        }

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var parser = parsers.FirstOrDefault(candidate =>
            candidate.SupportedExtensions.Contains(extension));
        if (parser is null)
        {
            throw new UnsupportedBookFormatException(
                string.IsNullOrEmpty(extension) ? "未知" : extension);
        }

        await using var bufferedContent = await BufferAsync(content, cancellationToken);
        var parsed = await parser.ParseAsync(
            bufferedContent,
            safeFileName,
            cancellationToken);

        bufferedContent.Position = 0;
        var stored = await fileStorage.SaveAsync(
            bufferedContent,
            safeFileName,
            cancellationToken);
        try
        {
            var request = new CreateImportedBookRequest(
                Choose(title, parsed.Title),
                Choose(author, parsed.Author ?? "未知作者"),
                Choose(language, parsed.Language ?? "zh-TW"),
                safeFileName,
                stored.RelativePath,
                parsed.Chapters
                    .Select(chapter => new CreateChapterRequest(
                        chapter.ChapterNumber,
                        chapter.Title,
                        chapter.OriginalText))
                    .ToArray());

            return await bookService.CreateImportedAsync(request, cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(stored.RelativePath, CancellationToken.None);
            throw;
        }
    }

    private static async Task<MemoryStream> BufferAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > MaximumFileBytes)
            {
                await destination.DisposeAsync();
                throw new ArgumentException("書籍檔案不可超過 10 MiB。", nameof(source));
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        destination.Position = 0;
        return destination;
    }

    private static string Choose(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback.Trim() : candidate.Trim();
}
