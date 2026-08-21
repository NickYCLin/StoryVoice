namespace StoryVoice.Application.ExternalVoices;

public static class DeveloperVoiceProjectStatuses
{
    public const string NotYetEffective = "not-yet-effective";
    public const string Active = "active";
    public const string ExpiringSoon = "expiring-soon";
    public const string Expired = "expired";
}

public static class DeveloperVoiceGrantStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}

public sealed record DeveloperVoiceGrantSummary(
    string VoiceAlias,
    string Status,
    DateTimeOffset? RevokedAtUtc);

public sealed record DeveloperVoiceProjectSummary(
    string KeyId,
    string DisplayName,
    string ProjectId,
    string AccessTier,
    string TokenPrefix,
    string ConsumerFamilyId,
    string TerritoryCountryCode,
    DateTimeOffset EffectiveAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    IReadOnlyList<DeveloperVoiceGrantSummary> Voices);

public sealed record DeveloperVoiceConsoleOverview(
    bool ServiceEnabled,
    int RequestsPerMinute,
    int MaximumTextCharacters,
    int MaximumTextUtf8Bytes,
    IReadOnlyList<DeveloperVoiceProjectSummary> Projects);

public interface IDeveloperVoiceConsoleService
{
    Task<DeveloperVoiceConsoleOverview> GetOverviewAsync(CancellationToken cancellationToken);
}
