namespace StoryVoice.Domain.Collections;

public sealed class BookCollectionBook
{
    private BookCollectionBook()
    {
    }

    private BookCollectionBook(
        Guid ownerId,
        Guid collectionId,
        Guid bookId,
        string? volumeLabel,
        int sortOrder)
    {
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(collectionId, nameof(collectionId));
        EnsureId(bookId, nameof(bookId));
        EnsureSortOrder(sortOrder);

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        CollectionId = collectionId;
        BookId = bookId;
        VolumeLabel = NormalizeVolumeLabel(volumeLabel);
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CollectionId { get; private set; }
    public Guid BookId { get; private set; }
    public string? VolumeLabel { get; private set; }
    public int SortOrder { get; private set; }

    internal static BookCollectionBook Create(
        Guid ownerId,
        Guid collectionId,
        Guid bookId,
        string? volumeLabel,
        int sortOrder) =>
        new(ownerId, collectionId, bookId, volumeLabel, sortOrder);

    internal void Update(string? volumeLabel, int sortOrder)
    {
        EnsureSortOrder(sortOrder);
        VolumeLabel = NormalizeVolumeLabel(volumeLabel);
        SortOrder = sortOrder;
    }

    private static string? NormalizeVolumeLabel(string? volumeLabel) =>
        CollectionValueValidator.NormalizeOptionalPrintable(
            volumeLabel,
            CollectionFieldLimits.VolumeLabel,
            nameof(volumeLabel));

    private static void EnsureSortOrder(int sortOrder)
    {
        if (sortOrder is < 0 or > CollectionFieldLimits.MaximumSortOrder)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                $"排序必須介於 0 與 {CollectionFieldLimits.MaximumSortOrder} 之間。");
        }
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
