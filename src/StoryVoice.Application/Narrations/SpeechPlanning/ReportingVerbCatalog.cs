namespace StoryVoice.Application.Narrations.SpeechPlanning;

/// <summary>
/// The reporting verbs ("說", "問道", …) that both <c>RuleBasedSpeakerAttributionProvider</c> (to
/// confirm a known character next to one) and <c>CharacterCandidateExtractor</c> (to notice an
/// unregistered name next to one) treat as evidence that the adjacent name is a speaker.
/// </summary>
public static class ReportingVerbCatalog
{
    public static readonly IReadOnlyList<string> Verbs =
    [
        "笑著說", "低聲說", "輕聲說", "大聲說", "笑道", "喊道", "叫道", "應道", "回答",
        "回應", "問道", "說道", "說", "問", "答", "喊", "道",
    ];
}
