using StoryVoice.Application.Books;

namespace StoryVoice.Application.BookImports;

public sealed class BookImportService(
    IEnumerable<IBookImportParser> parsers,
    IBookService bookService) : IBookImportService
{
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

        var parsed = await parser.ParseAsync(
            content,
            safeFileName,
            cancellationToken);
        var request = new CreateBookRequest(
            Choose(title, parsed.Title),
            string.IsNullOrWhiteSpace(author) ? "未知作者" : author.Trim(),
            Choose(language, "zh-TW"),
            safeFileName,
            parsed.Chapters
                .Select(chapter => new CreateChapterRequest(
                    chapter.ChapterNumber,
                    chapter.Title,
                    chapter.OriginalText))
                .ToArray());

        return await bookService.CreateAsync(request, cancellationToken);
    }

    private static string Choose(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
}
