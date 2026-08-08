namespace StoryVoice.Application.BookImports;

public sealed record ParsedBook(
    string Title,
    IReadOnlyList<ParsedChapter> Chapters);

public sealed record ParsedChapter(
    int ChapterNumber,
    string Title,
    string OriginalText);
