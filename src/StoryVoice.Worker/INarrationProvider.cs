namespace StoryVoice.Worker;

public sealed record NarrationSynthesisProgress(int CompletedChunks, int TotalChunks);

public interface INarrationProvider
{
    Task SynthesizeAsync(
        string text,
        string outputPath,
        string voice,
        string rate,
        Func<NarrationSynthesisProgress, CancellationToken, Task>? progressCallback,
        CancellationToken cancellationToken);
}
