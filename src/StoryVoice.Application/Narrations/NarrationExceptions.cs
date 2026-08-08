namespace StoryVoice.Application.Narrations;

public sealed class NarrationRightsRequiredException()
    : InvalidOperationException("請先確認你有權將這份合法正文送交語音服務處理。")
{
    public const string StableCode = "narration_rights_attestation_required";
}

public sealed class NarrationTextUnavailableException()
    : InvalidOperationException("這本書沒有可供 StoryVoice 朗讀的合法 EPUB／TXT 正文。")
{
    public const string StableCode = "narration_text_unavailable";
}
