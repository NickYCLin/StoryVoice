using System.IO.Compression;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StoryVoice.Application.BookImports;
using VersOne.Epub;

namespace StoryVoice.Infrastructure.BookImports;

public sealed partial class EpubBookParser : IBookImportParser
{
    private const long MaximumExpandedBytes = 100L * 1024 * 1024;
    private const int MaximumEntries = 5000;
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".epub" };

    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public async Task<ParsedBook> ParseAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        await using var archiveStream = new MemoryStream();
        await content.CopyToAsync(archiveStream, cancellationToken);
        EpubBook book;
        try
        {
            ValidateArchive(archiveStream);
            archiveStream.Position = 0;
            book = await EpubReader.ReadBookAsync(archiveStream);
        }
        catch (Exception exception) when (
            exception is InvalidDataException ||
            exception is IOException ||
            exception is FormatException ||
            exception is System.Xml.XmlException ||
            exception.GetType().Namespace?.StartsWith("VersOne.Epub", StringComparison.Ordinal) == true)
        {
            throw new ArgumentException("EPUB 內容無法解析。", nameof(content), exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var navigationTitles = BuildNavigationTitles(book.Navigation);
        var chapters = new List<ParsedChapter>();

        foreach (var file in book.ReadingOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = new HtmlParser().ParseDocument(file.Content);
            var text = ExtractText(document);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var chapterTitle = navigationTitles.GetValueOrDefault(file.Key)
                ?? ExtractTitle(document)
                ?? $"Chapter {chapters.Count + 1}";
            chapters.Add(new ParsedChapter(chapters.Count + 1, chapterTitle, text));
        }

        if (chapters.Count == 0)
        {
            throw new ArgumentException("EPUB 沒有可匯入的文字章節。", nameof(content));
        }

        var fallbackTitle = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
        var title = string.IsNullOrWhiteSpace(book.Title) ? fallbackTitle : book.Title.Trim();
        var author = string.IsNullOrWhiteSpace(book.Author) ? null : book.Author.Trim();
        var language = book.Schema.Package.Metadata.Languages.FirstOrDefault()?.Language;
        return new ParsedBook(title, author, language, chapters);
    }

    private static Dictionary<string, string> BuildNavigationTitles(
        IReadOnlyCollection<EpubNavigationItem>? navigation)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (navigation is null)
        {
            return result;
        }

        foreach (var item in navigation)
        {
            AddNavigationItem(result, item);
        }

        return result;
    }

    private static void AddNavigationItem(
        Dictionary<string, string> result,
        EpubNavigationItem item)
    {
        if (item.HtmlContentFile is not null && !string.IsNullOrWhiteSpace(item.Title))
        {
            result.TryAdd(item.HtmlContentFile.Key, item.Title.Trim());
        }

        foreach (var child in item.NestedItems)
        {
            AddNavigationItem(result, child);
        }
    }

    private static string ExtractText(IDocument document)
    {
        foreach (var ignored in document.QuerySelectorAll("script, style, nav"))
        {
            ignored.Remove();
        }

        var blocks = document.Body?
            .QuerySelectorAll("h1, h2, h3, h4, h5, h6, p, blockquote, li")
            .Select(element => NormalizeWhitespace(element.TextContent))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray() ?? [];
        return blocks.Length > 0
            ? string.Join(Environment.NewLine, blocks)
            : NormalizeWhitespace(document.Body?.TextContent ?? string.Empty);
    }

    private static string? ExtractTitle(IDocument document)
    {
        var title = document.QuerySelector("h1, h2, title")?.TextContent;
        return string.IsNullOrWhiteSpace(title) ? null : NormalizeWhitespace(title);
    }

    private static string NormalizeWhitespace(string value) =>
        Whitespace().Replace(value, " ").Trim();

    private static void ValidateArchive(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
        {
            throw new ArgumentException("EPUB 檔案項目數量不合理。", nameof(content));
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumExpandedBytes - expandedBytes)
            {
                throw new ArgumentException("EPUB 解壓後內容不可超過 100 MiB。", nameof(content));
            }

            expandedBytes += entry.Length;
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
