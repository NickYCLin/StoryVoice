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

    internal void SwitchActiveNarrationJob(Guid? expectedCurrentJobId, Guid nextJobId)
    {
        if (expectedCurrentJobId == Guid.Empty)
        {
            throw new ArgumentException(
                "可為空值的朗讀工作識別碼在有值時不可為空白 Guid。",
                nameof(expectedCurrentJobId));
        }

        EnsureId(nextJobId, nameof(nextJobId));
        if (ActiveNarrationJobId != expectedCurrentJobId)
        {
            throw new InvalidOperationException("目前朗讀工作指標已經變更。");
        }

        if (ActiveNarrationJobId == nextJobId)
        {
            throw new InvalidOperationException("下一個朗讀工作必須與目前工作不同。");
        }

        ActiveNarrationJobId = nextJobId;
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
