namespace StoryVoice.Infrastructure.Narrations;

public sealed class BlueMagpieOptions
{
    public const string SectionName = "BlueMagpie";
    public const string ProviderName = "bluemagpie";
    public const string MaleVoice = "hung_yi_lee";
    public const string FemaleVoice = "female_voice";
    public const string PinnedModelRevision = "6f7cab914a1e27c56b504ec663c0144dc25cc0a3";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://bluemagpie-gateway:8081/";

    public string InternalToken { get; set; } = string.Empty;

    public string ModelRevision { get; set; } = PinnedModelRevision;

    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int QueueTimeoutSeconds { get; set; } = 15;

    public int SynthesisWatchdogSeconds { get; set; } = 120;

    public int RequestTimeoutSeconds { get; set; } = 180;

    public int MaximumResponseBytes { get; set; } = 5 * 1024 * 1024;
}
