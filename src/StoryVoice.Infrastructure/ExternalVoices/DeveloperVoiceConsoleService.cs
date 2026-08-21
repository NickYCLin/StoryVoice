using Microsoft.Extensions.Options;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.ExternalVoices;

namespace StoryVoice.Infrastructure.ExternalVoices;

public sealed class DeveloperVoiceConsoleService(
    IOptions<ExternalVoiceApiOptions> options,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDeveloperVoiceConsoleService
{
    // 到期前的提醒視窗；與授權判定無關，純粹讓擁有者提早換發。
    private static readonly TimeSpan ExpiryWarningWindow = TimeSpan.FromDays(14);

    public Task<DeveloperVoiceConsoleOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var value = options.Value;
        var ownerId = currentUser.UserId;
        var now = timeProvider.GetUtcNow();
        var projects = value.Consumers
            .Where(pair => pair.Value.OwnerId != Guid.Empty && pair.Value.OwnerId == ownerId)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => CreateProjectSummary(pair.Key, pair.Value, now))
            .ToArray();

        return Task.FromResult(new DeveloperVoiceConsoleOverview(
            value.Enabled,
            value.RequestsPerMinute,
            value.MaximumTextCharacters,
            value.MaximumTextUtf8Bytes,
            projects));
    }

    private static DeveloperVoiceProjectSummary CreateProjectSummary(
        string keyId,
        ExternalVoiceConsumerOptions consumer,
        DateTimeOffset now)
    {
        var voices = consumer.AllowedVoices
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DeveloperVoiceGrantSummary(
                pair.Key,
                pair.Value.RevokedAtUtc is { } revokedAt && revokedAt <= now
                    ? DeveloperVoiceGrantStatuses.Revoked
                    : DeveloperVoiceGrantStatuses.Active,
                pair.Value.RevokedAtUtc))
            .ToArray();

        return new DeveloperVoiceProjectSummary(
            keyId,
            consumer.DisplayName,
            consumer.ProjectId,
            consumer.AccessTier,
            ResolveTokenPrefix(consumer.AccessTier),
            consumer.ConsumerFamilyId,
            consumer.TerritoryCountryCode,
            consumer.EffectiveAtUtc,
            consumer.ExpiresAtUtc,
            ResolveProjectStatus(consumer, now),
            voices);
    }

    // 與 ExternalVoiceAuthenticationHandler 相同的邊界：EffectiveAtUtc <= now < ExpiresAtUtc 才有效。
    private static string ResolveProjectStatus(ExternalVoiceConsumerOptions consumer, DateTimeOffset now)
    {
        if (consumer.EffectiveAtUtc > now)
        {
            return DeveloperVoiceProjectStatuses.NotYetEffective;
        }

        if (consumer.ExpiresAtUtc <= now)
        {
            return DeveloperVoiceProjectStatuses.Expired;
        }

        return consumer.ExpiresAtUtc - now <= ExpiryWarningWindow
            ? DeveloperVoiceProjectStatuses.ExpiringSoon
            : DeveloperVoiceProjectStatuses.Active;
    }

    private static string ResolveTokenPrefix(string accessTier) => accessTier switch
    {
        ExternalVoiceAccessTiers.PrivateDevelopment => ExternalVoiceAccessTiers.PrivateDevelopmentTokenPrefix,
        ExternalVoiceAccessTiers.SubscriptionCommercial => ExternalVoiceAccessTiers.SubscriptionCommercialTokenPrefix,
        _ => string.Empty,
    };
}
