using StoryVoice.Domain.Narrations;

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
        },
        new()
        {
            Provider = CharacterVoiceProviders.BlueMagpie,
            Voice = "female_voice",
            DisplayName = "BlueMagpie 內建女聲（私人自架）",
            Locale = "zh-TW"
        },
        new()
        {
            Provider = CharacterVoiceProviders.BlueMagpie,
            Voice = "hung_yi_lee",
            DisplayName = "BlueMagpie 內建男聲（私人自架）",
            Locale = "zh-TW"
        },
        new()
        {
            // VoAI's public VoiceAPI documentation uses this exact model, speaker, and style in
            // its synthesis examples. Keep all three pinned in the provider-scoped reference.
            Provider = CharacterVoiceProviders.VoAi,
            Voice = "v1:Neo:佑希:預設",
            DisplayName = "VoAI 佑希（Neo／預設）",
            Locale = "zh-TW"
        },
        new()
        {
            // The sentinel entry for the custom-voice provider — its actual per-character,
            // per-emotion voice comes from CharacterVoiceProfile lookups at synthesis time (see
            // MultiCharacterTurnBuilder), so "custom" here is just a placeholder that satisfies
            // SeriesCharacter.Voice's NOT NULL/allow-listed constraint, not a real voice id. The
            // provider name must stay in sync with ThreeWaVoxCpm2NarrationProvider.ProviderName.
            Provider = "3wa-voxcpm2",
            Voice = "custom",
            DisplayName = "自訂聲線（角色聲線工作室）",
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
