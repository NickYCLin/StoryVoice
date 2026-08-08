namespace StoryVoice.Infrastructure.Identity;

public sealed class CompanionAccessToken
{
    private CompanionAccessToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public string TokenHint { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public ApplicationUser User { get; private set; } = null!;

    public static CompanionAccessToken Create(
        Guid userId,
        string tokenHash,
        string tokenHint,
        DateTimeOffset expiresAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            TokenHint = tokenHint,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAt ??= revokedAt;
    }
}
