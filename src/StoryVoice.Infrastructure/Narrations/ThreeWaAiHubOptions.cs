namespace StoryVoice.Infrastructure.Narrations;

public sealed class ThreeWaAiHubOptions
{
    public const string SectionName = "ThreeWaAiHub";

    public string BaseUrl { get; set; } = "https://3wa.tw/3waAIHub/";
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Maximum accepted JSON body for submit, status, and result responses.</summary>
    public int MaximumJsonResponseBytes { get; set; } = 64 * 1024;

    /// <summary>Maximum audio artifact downloaded by a single synthesis request.</summary>
    public int MaximumAudioResponseBytes { get; set; } = 20 * 1024 * 1024;
}

/// <summary>
/// Code-pinned 3wa capabilities. Design must not be enabled by environment configuration: the
/// official manifest currently does not forward <c>voice_prompt</c> to VoxCPM2, and the production
/// account currently has no configured managed voice presets. Treating prompt text as a character
/// identity would therefore silently collapse every designed role onto the same voice.
/// </summary>
public static class ThreeWaSynthesisCapabilities
{
    public static bool SupportsVoiceDesign => false;
    public const string DesignVoiceUnavailableCode = "3wa_design_voice_unsupported";
    public const string DesignVoiceUnavailableMessage =
        "3wa 目前不會把 voice_prompt 傳給 VoxCPM2，文字設計聲線無法產生可區分的角色聲音；請改用已授權且已完成的克隆聲線。";
    public const string CloneVoiceUnavailableCode = "3wa_clone_voice_unavailable";
    public const string CloneVoiceUnavailableMessage =
        "3wa 角色正式配音必須連結一組已完成的克隆基礎聲線。";
    public const string NarratorFallbackVoice = "zh-TW-YunJheNeural";
    public const string LegacyNarratorVoiceUnavailableCode = "3wa_legacy_narrator_voice_unavailable";
    public const string LegacyNarratorVoiceUnavailableMessage =
        "舊版 3wa 旁白聲線只保存了 custom 佔位值，無法安全合成；請重新建立系列聲線版本。";
}
