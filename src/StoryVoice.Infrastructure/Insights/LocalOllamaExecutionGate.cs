namespace StoryVoice.Infrastructure.Insights;

internal static class LocalOllamaExecutionGate
{
    internal static SemaphoreSlim Gate { get; } = new(1, 1);
}
