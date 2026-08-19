namespace StoryVoice.Application.VoiceCatalog;

public static class PublicVoiceCatalogStatus
{
    public const string Available = "available";
}

public static class PublicVoiceCatalogCtaKinds
{
    public const string ViewPlans = "view-plans";
}

public sealed record PublicVoiceCatalogCard(
    string Alias,
    string DisplayName,
    string Subtitle,
    string Disclosure,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> UseCases,
    string SampleUrl,
    bool CanPreview,
    string CtaKind,
    bool SubscriptionAvailable,
    string Status);

public sealed record PublicVoiceDemo(
    byte[] Content,
    string ContentType);

public interface IPublicVoiceCatalogService
{
    Task<IReadOnlyList<PublicVoiceCatalogCard>> GetVoicesAsync(
        CancellationToken cancellationToken);

    Task<PublicVoiceDemo?> GetDemoAsync(
        string alias,
        CancellationToken cancellationToken);
}
