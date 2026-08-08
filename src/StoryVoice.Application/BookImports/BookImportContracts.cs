namespace StoryVoice.Application.BookImports;

public sealed record ParsedBook(
    string Title,
    string? Author,
    string? Language,
    IReadOnlyList<ParsedChapter> Chapters);

public sealed record ParsedChapter(
    int ChapterNumber,
    string Title,
    string OriginalText);
