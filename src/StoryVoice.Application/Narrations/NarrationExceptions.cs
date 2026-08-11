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

public sealed class SingleVoiceNarrationRetiredException()
    : InvalidOperationException("新的朗讀工作必須從多角色系列流程建立；既有單聲線音訊僅保留為歷史版本。")
{
    public const string StableCode = "single_voice_narration_retired";
}

public sealed class NarrationAdmissionDisabledException()
    : InvalidOperationException("目前暫停建立新的朗讀工作，既有朗讀工作不受影響。")
{
    public const string StableCode = "narration_admission_disabled";
}
