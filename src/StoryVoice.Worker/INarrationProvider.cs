namespace StoryVoice.Worker;

public interface INarrationProvider
{
    Task SynthesizeAsync(
        string text,
        string outputPath,
        string voice,
        string rate,
        CancellationToken cancellationToken);
}
