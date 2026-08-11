namespace StoryVoice.Domain.Series;

public sealed class SeriesBook
{
    private SeriesBook()
    {
    }

    private SeriesBook(
        Guid ownerId,
        Guid seriesId,
        Guid bookId,
        string volumeLabel,
        int sortOrder,
        int membershipRevision)
    {
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(bookId, nameof(bookId));
        if (sortOrder is < 0 or > SeriesFieldLimits.MaximumSortOrder)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                $"冊次排序必須介於 0 與 {SeriesFieldLimits.MaximumSortOrder} 之間。");
        }

        if (membershipRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(membershipRevision), "成員版本必須大於零。");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        SeriesId = seriesId;
        BookId = bookId;
        VolumeLabel = SeriesValueValidator.NormalizeIdentifier(
            volumeLabel,
            SeriesFieldLimits.VolumeLabel,
            nameof(volumeLabel));
        SortOrder = sortOrder;
        MembershipRevision = membershipRevision;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid BookId { get; private set; }
    public string VolumeLabel { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public int MembershipRevision { get; private set; }
    public Guid? ActiveNarrationJobId { get; private set; }

    internal static SeriesBook Create(
        Guid ownerId,
        Guid seriesId,
        Guid bookId,
        string volumeLabel,
        int sortOrder,
        int membershipRevision) =>
        new(ownerId, seriesId, bookId, volumeLabel, sortOrder, membershipRevision);

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
