namespace StoryVoice.Worker;

public sealed record VoAiAudioSegment(
    string InputWavPath,
    string Volume,
    int PauseBeforeMs);

/// <summary>A provider-neutral WAV segment consumed by the shared ffmpeg composition seam.</summary>
public sealed record FfmpegAudioSegment(
    string InputWavPath,
    string Volume,
    int PauseBeforeMs,
    bool DeleteInputAfterNormalization = true);

public interface IVoAiAudioComposer
{
    Task ComposeAsync(
        IReadOnlyList<VoAiAudioSegment> segments,
        string outputPath,
        CancellationToken cancellationToken);
}

public interface IFfmpegAudioComposer
{
    Task ComposeAsync(
        IReadOnlyList<FfmpegAudioSegment> segments,
        string outputPath,
        int outputSampleRate,
        CancellationToken cancellationToken);
}
