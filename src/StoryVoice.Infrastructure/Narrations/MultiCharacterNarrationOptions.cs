namespace StoryVoice.Infrastructure.Narrations;

public sealed class MultiCharacterNarrationOptions
{
    public const string SectionName = "MultiCharacterNarration";

    public string ProviderVersion { get; set; } = "edge-tts-7.2.8";
    public int ChapterPauseMs { get; set; } = 700;
    public string CompositionVersion { get; set; } = "edge-tts-multi-voice-v1";
    public string FfmpegProfile { get; set; } = "concat-demuxer-mp3-v1";
}
