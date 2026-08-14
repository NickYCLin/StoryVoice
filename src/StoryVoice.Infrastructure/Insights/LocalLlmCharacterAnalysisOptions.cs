namespace StoryVoice.Infrastructure.Insights;

public sealed class LocalLlmCharacterAnalysisOptions
{
    public const string SectionName = "LocalLlmCharacterAnalysis";

    public string BaseUrl { get; set; } = "http://host.docker.internal:11434/";
    public string Model { get; set; } = "gpt-oss:20b";
    public int TimeoutSeconds { get; set; } = 600;
    public int UnloadTimeoutSeconds { get; set; } = 15;
    public int MaximumResponseBytes { get; set; } = 16 * 1024;
}
