namespace StoryVoice.Infrastructure.Persistence;

public sealed class SeriesVoiceCatalogOptions
{
    public const string SectionName = "SeriesVoiceCatalog";

    public List<SeriesVoiceCatalogEntry> Voices { get; set; } = [];

    public static List<SeriesVoiceCatalogEntry> CreateDefaultVoices() =>
    [
        new()
        {
            Provider = "edge",
            Voice = "zh-TW-HsiaoChenNeural",
            DisplayName = "曉臻（女聲）",
            Locale = "zh-TW"
        },
        new()
        {
            Provider = "edge",
            Voice = "zh-TW-YunJheNeural",
            DisplayName = "雲哲（男聲）",
            Locale = "zh-TW"
        },
        new()
        {
            Provider = "edge",
            Voice = "zh-CN-YunxiNeural",
            DisplayName = "雲希（男聲）",
            Locale = "zh-CN"
        },
        new()
        {
            Provider = "edge",
            Voice = "zh-CN-YunjianNeural",
            DisplayName = "雲健（男聲）",
            Locale = "zh-CN"
        },
        new()
        {
            Provider = "edge",
            Voice = "zh-CN-XiaoxiaoNeural",
            DisplayName = "曉曉（女聲）",
            Locale = "zh-CN"
        },
        new()
        {
            Provider = "edge",
            Voice = "zh-CN-XiaoyiNeural",
            DisplayName = "曉伊（女聲）",
            Locale = "zh-CN"
        },
        new()
        {
            Provider = "edge",
            Voice = "zh-TW-HsiaoYuNeural",
            DisplayName = "曉雨（女聲）",
            Locale = "zh-TW"
        }
    ];
}

public sealed class SeriesVoiceCatalogEntry
{
    public string Provider { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
}
