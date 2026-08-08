namespace StoryVoice.Domain.Insights;

public sealed class ReadingNote
{
    private ReadingNote()
    {
    }

    private ReadingNote(Guid ownerId, Guid bookId, Guid? chapterId, string body)
    {
        if (ownerId == Guid.Empty || bookId == Guid.Empty)
        {
            throw new ArgumentException("筆記的擁有者與書籍識別碼不可為空白。");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        BookId = bookId;
        ChapterId = chapterId;
        Body = NormalizeBody(body);
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid BookId { get; private set; }
    public Guid? ChapterId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReadingNote Create(Guid ownerId, Guid bookId, Guid? chapterId, string body) =>
        new(ownerId, bookId, chapterId, body);

    public void Update(string body)
    {
        Body = NormalizeBody(body);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string NormalizeBody(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 4_000)
        {
            throw new ArgumentException("筆記必須為 1 至 4000 個字元。", nameof(value));
        }

        return normalized;
    }
}
