namespace StoryVoice.Domain.Books;

public sealed class Book
{
    private readonly List<Chapter> _chapters = [];

    private Book()
    {
    }

    private Book(string title, string author, string language, string originalFileName)
    {
        Id = Guid.NewGuid();
        Title = Require(title, nameof(title));
        Author = Require(author, nameof(author));
        Language = Require(language, nameof(language));
        OriginalFileName = Require(originalFileName, nameof(originalFileName));
        FileType = Path.GetExtension(OriginalFileName).TrimStart('.').ToLowerInvariant();
        Status = BookStatus.Uploaded;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Author { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;

    public string FileType { get; private set; } = string.Empty;

    public string? StoragePath { get; private set; }

    public BookStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Chapter> Chapters => _chapters.AsReadOnly();

    public static Book Create(string title, string author, string language, string originalFileName) =>
        new(title, author, language, originalFileName);

    public Chapter AddChapter(int chapterNumber, string title, string originalText)
    {
        if (_chapters.Any(chapter => chapter.ChapterNumber == chapterNumber))
        {
            throw new InvalidOperationException($"章節編號 {chapterNumber} 已存在。");
        }

        var chapter = Chapter.Create(Id, chapterNumber, title, originalText);
        _chapters.Add(chapter);
        return chapter;
    }

    public void SetStoragePath(string storagePath)
    {
        StoragePath = Require(storagePath, nameof(storagePath));
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
