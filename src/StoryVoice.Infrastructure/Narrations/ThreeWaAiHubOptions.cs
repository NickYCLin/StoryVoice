namespace StoryVoice.Infrastructure.Narrations;

public sealed class ThreeWaAiHubOptions
{
    public const string SectionName = "ThreeWaAiHub";

    public string BaseUrl { get; set; } = "https://3wa.tw/3waAIHub/";
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Maximum accepted JSON body for submit, status, and result responses.</summary>
    public int MaximumJsonResponseBytes { get; set; } = 64 * 1024;

    /// <summary>Maximum audio artifact downloaded by a single synthesis request.</summary>
    public int MaximumAudioResponseBytes { get; set; } = 20 * 1024 * 1024;
}
