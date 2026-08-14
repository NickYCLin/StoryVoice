namespace StoryVoice.Worker;

public sealed record VoAiAudioSegment(
    string InputWavPath,
    string Volume,
    int PauseBeforeMs);

public interface IVoAiAudioComposer
{
    Task ComposeAsync(
        IReadOnlyList<VoAiAudioSegment> segments,
        string outputPath,
        CancellationToken cancellationToken);
}
