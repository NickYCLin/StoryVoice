namespace StoryVoice.Domain.Books;

public sealed class Book
{
    private readonly List<Chapter> _chapters = [];

    private Book()
    {
    }

    private Book(Guid ownerId, string title, string author, string language, string originalFileName)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("使用者識別碼不可為空白。", nameof(ownerId));
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Title = Require(title, nameof(title));
        Author = Require(author, nameof(author));
        Language = Require(language, nameof(language));
        OriginalFileName = Require(originalFileName, nameof(originalFileName));
        FileType = Path.GetExtension(OriginalFileName).TrimStart('.').ToLowerInvariant();
        Status = BookStatus.Uploaded;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid? OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Author { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;

    public string FileType { get; private set; } = string.Empty;

    public string? StoragePath { get; private set; }

    public Guid? ContentBookId { get; private set; }

    public string? SourceProvider { get; private set; }

    public string? ExternalSourceId { get; private set; }

    public string? SourceUrl { get; private set; }

    public string? CoverImageUrl { get; private set; }

    public string? TitleCorrection { get; private set; }

    public string? AuthorCorrection { get; private set; }

    public string? CoverImageUrlCorrection { get; private set; }

    public bool? NativeTtsAvailable { get; private set; }

    public EbookLayout? EbookLayout { get; private set; }

    public DateTimeOffset? SourceSyncedAt { get; private set; }

    public BookStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Chapter> Chapters => _chapters.AsReadOnly();

    public static Book Create(
        Guid ownerId,
        string title,
        string author,
        string language,
        string originalFileName) =>
        new(ownerId, title, author, language, originalFileName);

    public static Book CreateExternal(
        Guid ownerId,
        string title,
        string author,
        string language,
        string sourceProvider,
        string externalSourceId,
        string sourceUrl,
        string? coverImageUrl,
        bool? nativeTtsAvailable = null,
        EbookLayout? ebookLayout = null)
    {
        var book = new Book(ownerId, title, author, language, $"{externalSourceId}.link")
        {
            FileType = "external",
            Status = BookStatus.Linked,
            SourceProvider = Require(sourceProvider, nameof(sourceProvider)),
            ExternalSourceId = Require(externalSourceId, nameof(externalSourceId)),
            SourceUrl = Require(sourceUrl, nameof(sourceUrl)),
            CoverImageUrl = NormalizeOptional(coverImageUrl),
            NativeTtsAvailable = nativeTtsAvailable,
            EbookLayout = ebookLayout,
            SourceSyncedAt = DateTimeOffset.UtcNow
        };

        return book;
    }

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

    public void LinkAuthorizedContent(Guid contentBookId)
    {
        if (Status != BookStatus.Linked)
        {
            throw new InvalidOperationException("只有外部書目可以連結另一本已授權正文。");
        }

        if (contentBookId == Guid.Empty || contentBookId == Id)
        {
            throw new ArgumentException("正文書籍識別碼無效。", nameof(contentBookId));
        }

        ContentBookId = contentBookId;
    }

    public void UnlinkAuthorizedContent() => ContentBookId = null;

    public void SetMetadataCorrections(
        string? title,
        string? author,
        string? coverImageUrl)
    {
        var normalizedTitle = NormalizeCorrection(title, 500, nameof(title));
        var normalizedAuthor = NormalizeCorrection(author, 300, nameof(author));
        var normalizedCover = NormalizeCorrection(coverImageUrl, 2_000, nameof(coverImageUrl));
        if (normalizedCover is not null
            && (!Uri.TryCreate(normalizedCover, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("封面網址必須是有效的 HTTP 或 HTTPS 網址。", nameof(coverImageUrl));
        }

        TitleCorrection = string.Equals(normalizedTitle, Title, StringComparison.Ordinal) ? null : normalizedTitle;
        AuthorCorrection = string.Equals(normalizedAuthor, Author, StringComparison.Ordinal) ? null : normalizedAuthor;
        CoverImageUrlCorrection = string.Equals(normalizedCover, CoverImageUrl, StringComparison.Ordinal) ? null : normalizedCover;
    }

    public void UpdateExternalMetadata(
        string title,
        string author,
        string language,
        string sourceUrl,
        string? coverImageUrl,
        bool? nativeTtsAvailable,
        EbookLayout? ebookLayout)
    {
        if (SourceProvider is null || ExternalSourceId is null)
        {
            throw new InvalidOperationException("只有外部來源書籍可以更新來源 metadata。");
        }

        Title = Require(title, nameof(title));
        Author = Require(author, nameof(author));
        Language = Require(language, nameof(language));
        SourceUrl = Require(sourceUrl, nameof(sourceUrl));
        CoverImageUrl = NormalizeOptional(coverImageUrl);
        NativeTtsAvailable = nativeTtsAvailable;
        EbookLayout = ebookLayout;

        SourceSyncedAt = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("欄位不可為空白。", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeCorrection(string? value, int maximumLength, string parameterName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized?.Length > maximumLength)
        {
            throw new ArgumentException($"欄位不可超過 {maximumLength} 個字元。", parameterName);
        }

        return normalized;
    }
}
