using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure.Identity;

public sealed record IssuedCompanionToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record CompanionTokenPrincipal(Guid UserId, string Email);

public sealed class CompanionTokenService(StoryVoiceDbContext dbContext)
{
    private const string TokenPrefix = "svc_";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(7);

    public async Task<IssuedCompanionToken> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeTokens = await dbContext.CompanionAccessTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.Revoke(now);
        }

        var rawToken = TokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.Add(TokenLifetime);
        dbContext.CompanionAccessTokens.Add(CompanionAccessToken.Create(
            userId,
            Hash(rawToken),
            rawToken[^8..],
            expiresAt));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new IssuedCompanionToken(rawToken, expiresAt);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeTokens = await dbContext.CompanionAccessTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.Revoke(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CompanionTokenPrincipal?> ValidateAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) ||
            !rawToken.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
            rawToken.Length > 128)
        {
            return null;
        }

        var tokenHash = Hash(rawToken);
        var now = DateTimeOffset.UtcNow;
        var token = await dbContext.CompanionAccessTokens
            .AsNoTracking()
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.TokenHash == tokenHash &&
                    candidate.RevokedAt == null &&
                    candidate.ExpiresAt > now,
                cancellationToken);
        if (token?.User.Email is null)
        {
            return null;
        }

        return new CompanionTokenPrincipal(token.UserId, token.User.Email);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
