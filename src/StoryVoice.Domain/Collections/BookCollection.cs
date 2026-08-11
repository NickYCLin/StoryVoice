using StoryVoice.Domain.Books;

namespace StoryVoice.Domain.Collections;

public sealed class BookCollection
{
    private readonly List<BookCollectionBook> _books = [];
    private readonly List<CollectionShare> _shares = [];

    private BookCollection()
    {
    }

    private BookCollection(Guid ownerId, string name, string? description)
    {
        EnsureId(ownerId, nameof(ownerId));

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Name = CollectionIdentityNormalizer.NormalizeDisplayValue(
            name,
            nameof(name),
            CollectionFieldLimits.CollectionName);
        NormalizedName = CollectionIdentityNormalizer.NormalizeKey(
            Name,
            nameof(name),
            CollectionFieldLimits.CollectionName);
        Description = CollectionValueValidator.NormalizeOptionalPrintable(
            description,
            CollectionFieldLimits.Description,
            nameof(description));
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
    public IReadOnlyCollection<BookCollectionBook> Books => _books.AsReadOnly();
    public IReadOnlyCollection<CollectionShare> Shares => _shares.AsReadOnly();

    public static BookCollection Create(Guid ownerId, string name, string? description) =>
        new(ownerId, name, description);

    public void Rename(string name)
    {
        Name = CollectionIdentityNormalizer.NormalizeDisplayValue(
            name,
            nameof(name),
            CollectionFieldLimits.CollectionName);
        NormalizedName = CollectionIdentityNormalizer.NormalizeKey(
            Name,
            nameof(name),
            CollectionFieldLimits.CollectionName);
        Touch();
    }

    public void UpdateDescription(string? description)
    {
        Description = CollectionValueValidator.NormalizeOptionalPrintable(
            description,
            CollectionFieldLimits.Description,
            nameof(description));
        Touch();
    }

    public BookCollectionBook AddBook(Book contentBook, string? volumeLabel, int sortOrder)
    {
        ArgumentNullException.ThrowIfNull(contentBook);
        if (contentBook.Id == Guid.Empty)
        {
            throw new ArgumentException("書籍識別碼不可為空白。", nameof(contentBook));
        }

        if (contentBook.OwnerId is null
            || contentBook.OwnerId == Guid.Empty
            || contentBook.OwnerId != OwnerId)
        {
            throw new InvalidOperationException("書冊與正文書籍必須屬於同一位擁有者。");
        }

        if (contentBook.Status == BookStatus.Linked)
        {
            throw new InvalidOperationException("書冊只能收錄真正含正文的 owner-scoped 書籍。");
        }

        if (_books.Any(book => book.BookId == contentBook.Id))
        {
            throw new InvalidOperationException("這本書已經加入書冊。");
        }

        if (_books.Any(book => book.SortOrder == sortOrder))
        {
            throw new InvalidOperationException("書冊內的排序不可重複。");
        }

        var membership = BookCollectionBook.Create(OwnerId, Id, contentBook.Id, volumeLabel, sortOrder);
        _books.Add(membership);
        Touch();
        return membership;
    }

    public void RemoveBook(Guid bookId)
    {
        var membership = _books.SingleOrDefault(book => book.BookId == bookId)
            ?? throw new InvalidOperationException("這本書不屬於這個書冊。");
        _books.Remove(membership);
        Touch();
    }

    public void UpdateBook(Guid bookId, string? volumeLabel, int sortOrder)
    {
        var membership = _books.SingleOrDefault(book => book.BookId == bookId)
            ?? throw new InvalidOperationException("這本書不屬於這個書冊。");
        if (_books.Any(book => book.BookId != bookId && book.SortOrder == sortOrder))
        {
            throw new InvalidOperationException("書冊內的排序不可重複。");
        }

        membership.Update(volumeLabel, sortOrder);
        Touch();
    }

    public CollectionShare AddShare(Guid granteeUserId, string granteeEmail)
    {
        if (_shares.Any(share => share.GranteeUserId == granteeUserId))
        {
            throw new InvalidOperationException("這個使用者已經有這個書冊的分享。");
        }

        var share = CollectionShare.Create(OwnerId, Id, granteeUserId, granteeEmail);
        _shares.Add(share);
        Touch();
        return share;
    }

    public void RevokeShare(Guid shareId)
    {
        var share = _shares.SingleOrDefault(candidate => candidate.Id == shareId)
            ?? throw new InvalidOperationException("找不到這個分享。");
        _shares.Remove(share);
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
