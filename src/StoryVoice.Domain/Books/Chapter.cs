namespace StoryVoice.Domain.Books;

public sealed class Chapter
{
    private Chapter()
    {
    }

    private Chapter(Guid bookId, int chapterNumber, string title, string originalText)
    {
        Id = Guid.NewGuid();
        BookId = bookId;
        ChapterNumber = chapterNumber;
        SortOrder = chapterNumber;
        Title = Require(title, nameof(title));
        OriginalText = Require(originalText, nameof(originalText));
    }

    public Guid Id { get; private set; }

    public Guid BookId { get; private set; }

    public int ChapterNumber { get; private set; }

    public int SortOrder { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string OriginalText { get; private set; } = string.Empty;

    internal static Chapter Create(Guid bookId, int chapterNumber, string title, string originalText)
    {
        if (chapterNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterNumber), "章節編號必須大於零。");
        }

        return new Chapter(bookId, chapterNumber, title, originalText);
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("欄位不可為空白。", parameterName);
        }

        return value.Trim();
    }
}
