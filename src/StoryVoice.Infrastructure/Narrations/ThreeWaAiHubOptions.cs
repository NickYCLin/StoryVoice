namespace StoryVoice.Infrastructure.Narrations;

public sealed class ThreeWaAiHubOptions
{
    public const string SectionName = "ThreeWaAiHub";

    public string BaseUrl { get; set; } = "https://3wa.tw/3waAIHub/";
    public string ApiToken { get; set; } = string.Empty;
}
