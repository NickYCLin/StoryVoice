namespace StoryVoice.Domain.Insights;

public sealed class BookExtractiveSummary
{
    private BookExtractiveSummary()
    {
    }

    private BookExtractiveSummary(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string sourceHash,
        string excerptsJson)
    {
        BookId = bookId;
        OwnerId = ownerId;
        Replace(contentBookId, sourceHash, excerptsJson);
    }

    public Guid BookId { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ContentBookId { get; private set; }
    public string Kind { get; private set; } = "Extractive";
    public string Generator { get; private set; } = "builtin-leading-sentences";
    public string Version { get; private set; } = "v1";
    public string SourceHash { get; private set; } = string.Empty;
    public string ExcerptsJson { get; private set; } = "[]";
    public DateTimeOffset GeneratedAt { get; private set; }

    public static BookExtractiveSummary Create(
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        string sourceHash,
        string excerptsJson) =>
        new(ownerId, bookId, contentBookId, sourceHash, excerptsJson);

    public void Replace(Guid contentBookId, string sourceHash, string excerptsJson)
    {
        if (OwnerId == Guid.Empty || BookId == Guid.Empty || contentBookId == Guid.Empty)
        {
            throw new ArgumentException("摘要的書籍識別碼不可為空白。");
        }

        ContentBookId = contentBookId;
        SourceHash = Require(sourceHash, nameof(sourceHash), 128);
        ExcerptsJson = Require(excerptsJson, nameof(excerptsJson), 64_000);
        GeneratedAt = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximumLength)
        {
            throw new ArgumentException("摘要欄位長度無效。", parameterName);
        }

        return normalized;
    }
}
