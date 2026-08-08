using System.Text;
using System.Text.RegularExpressions;
using StoryVoice.Application.BookImports;

namespace StoryVoice.Infrastructure.BookImports;

public sealed partial class PlainTextBookParser : IBookImportParser
{
    private const int MaximumCharacterCount = 10 * 1024 * 1024;
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };

    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public async Task<ParsedBook> ParseAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var reader = new StreamReader(
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (text.Length > MaximumCharacterCount)
        {
            throw new ArgumentException("TXT 內容不可超過 10 MiB。", nameof(content));
        }

        var normalized = NormalizeNewLines(text).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("TXT 內容是空的。", nameof(content));
        }

        var fallbackTitle = GetFallbackTitle(fileName);
        var chapters = ExtractChapters(normalized, fallbackTitle);
        if (chapters.Count == 0)
        {
            throw new ArgumentException("TXT 沒有可匯入的章節內容。", nameof(content));
        }

        return new ParsedBook(fallbackTitle, chapters);
    }

    private static IReadOnlyList<ParsedChapter> ExtractChapters(
        string text,
        string fallbackTitle)
    {
        var chapters = new List<ParsedChapter>();
        var body = new StringBuilder();
        string? currentTitle = null;
        var sawHeading = false;

        foreach (var line in text.Split((char)10))
        {
            var match = ChapterHeading().Match(line);
            if (!match.Success)
            {
                body.AppendLine(line.TrimEnd());
                continue;
            }

            sawHeading = true;
            AddChapter(chapters, currentTitle ?? "前言", body);
            currentTitle = match.Groups["title"].Value.Trim();
        }

        AddChapter(chapters, currentTitle ?? fallbackTitle, body);

        if (!sawHeading && chapters.Count == 1)
        {
            chapters[0] = chapters[0] with { Title = fallbackTitle };
        }

        return chapters;
    }

    private static void AddChapter(
        List<ParsedChapter> chapters,
        string title,
        StringBuilder body)
    {
        var originalText = body.ToString().Trim();
        body.Clear();
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return;
        }

        chapters.Add(new ParsedChapter(
            chapters.Count + 1,
            title,
            originalText));
    }

    private static string GetFallbackTitle(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName)).Trim();
        return string.IsNullOrWhiteSpace(title) ? "未命名書籍" : title;
    }

    private static string NormalizeNewLines(string value)
    {
        var normalized = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != (char)13)
            {
                normalized.Append(character);
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == (char)10)
            {
                index++;
            }

            normalized.Append((char)10);
        }

        return normalized.ToString();
    }

    [GeneratedRegex(
        @"^\s*(?<title>(?:第[0-9０-９一二三四五六七八九十百千兩〇零]+[章回節篇卷](?:\s+|[:：、.\-])?.*|Chapter\s+[0-9]+(?:\s+|[:：.\-])?.*))\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterHeading();
}
