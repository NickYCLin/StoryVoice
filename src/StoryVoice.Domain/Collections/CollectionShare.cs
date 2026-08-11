namespace StoryVoice.Domain.Collections;

public sealed class CollectionShare
{
    private CollectionShare()
    {
    }

    private CollectionShare(
        Guid ownerId,
        Guid collectionId,
        Guid granteeUserId,
        string granteeEmail)
    {
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(collectionId, nameof(collectionId));
        EnsureId(granteeUserId, nameof(granteeUserId));
        if (granteeUserId == ownerId)
        {
            throw new InvalidOperationException("不可以把書冊分享給自己。");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        CollectionId = collectionId;
        GranteeUserId = granteeUserId;
        GranteeEmail = CollectionValueValidator.NormalizePrintable(
            granteeEmail,
            CollectionFieldLimits.GranteeEmail,
            nameof(granteeEmail));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid CollectionId { get; private set; }
    public Guid GranteeUserId { get; private set; }
    public string GranteeEmail { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    internal static CollectionShare Create(
        Guid ownerId,
        Guid collectionId,
        Guid granteeUserId,
        string granteeEmail) =>
        new(ownerId, collectionId, granteeUserId, granteeEmail);

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
