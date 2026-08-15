namespace StoryVoice.Infrastructure.Narrations;

public sealed class BlueMagpieOptions
{
    public const string SectionName = "BlueMagpie";
    public const string ProviderName = "bluemagpie";
    public const string MaleVoice = "hung_yi_lee";
    public const string FemaleVoice = "female_voice";
    public const string PinnedModelRevision = "6f7cab914a1e27c56b504ec663c0144dc25cc0a3";
    public const string PinnedProviderVersion = "bm1-d2d7ef3e81456915eb7a3cfe2446a9f19417c21b";

    public bool Enabled { get; set; }

    /// <summary>Separate admission switch for novel text and formal narration jobs. Preview can be
    /// enabled while this remains false, so a fixed-sentence canary never silently admits books.</summary>
    public bool FormalNarrationEnabled { get; set; }

    public string BaseUrl { get; set; } = "http://bluemagpie-gateway:8081/";

    public string InternalToken { get; set; } = string.Empty;

    public string ModelRevision { get; set; } = PinnedModelRevision;

    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int QueueTimeoutSeconds { get; set; } = 15;

    public int SynthesisWatchdogSeconds { get; set; } = 120;

    public int ModelLifecycleWatchdogSeconds { get; set; } = 120;

    public int RequestTimeoutSeconds { get; set; } = 180;

    public int MaximumResponseBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// A cancelled HTTP request can leave an uncancellable CUDA worker holding the shared Redis
    /// lease. A restarted Worker must wait beyond the gateway queue and the maximum possible
    /// remaining lease before it claims the same durable job again.
    /// </summary>
    public TimeSpan RestartCooldown => TimeSpan.FromSeconds(checked(
        QueueTimeoutSeconds
        + Math.Max(
            180,
            Math.Max(
                SynthesisWatchdogSeconds + 60,
                ModelLifecycleWatchdogSeconds + 60))
        + 30));
}
