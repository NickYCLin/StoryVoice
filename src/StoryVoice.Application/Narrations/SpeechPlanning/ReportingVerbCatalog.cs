namespace StoryVoice.Application.Narrations.SpeechPlanning;

/// <summary>
/// The reporting verbs ("說", "問道", …) used by <c>RuleBasedSpeakerAttributionProvider</c>
/// only to confirm an already-known character next to dialogue. Unknown roles belong to the
/// separately versioned local-LLM character-analysis workflow.
/// </summary>
public static class ReportingVerbCatalog
{
    public static readonly IReadOnlyList<string> Verbs =
    [
        "笑著說", "低聲說", "輕聲說", "大聲說", "笑道", "喊道", "叫道", "應道", "回答",
        "回應", "問道", "說道", "說", "問", "答", "喊", "道",
    ];
}
