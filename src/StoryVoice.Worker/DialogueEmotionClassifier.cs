using System.Text.RegularExpressions;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Worker;

public enum DialogueEmotion
{
    Neutral,
    Nervous,
    Happy,
    Angry,
    Sad,
}

/// <summary>
/// Deterministic, rule-based reading-style hint for one dialogue turn. Runs entirely inside the
/// Worker against text it already has legitimate access to (the chapter body used to verify the
/// locked speech plan) — nothing here is persisted or exposed through any API response, matching
/// the "no private text leaves the Worker" boundary the rest of the multi-character pipeline
/// holds to. Purely a delivery-style guess from punctuation and nearby reporting-clause wording;
/// it is not a sentiment model and makes no claim to literary accuracy.
/// </summary>
public static class DialogueEmotionClassifier
{
    private static readonly Regex StutterPattern = new(@"(.)、\1|(.)\2\2", RegexOptions.Compiled);

    private static readonly string[] AngryCues = ["吼", "罵", "咆", "怒", "兇狠", "凶狠", "瞪", "咬牙切齒"];
    private static readonly string[] NervousCues = ["抖", "顫", "結巴", "慌", "緊張", "心跳", "冒汗", "發毛"];
    private static readonly string[] HappyCues = ["笑", "甜甜", "開心", "愉快", "眨眼", "咧嘴", "興奮"];
    private static readonly string[] SadCues = ["哭", "眼淚", "難過", "嘆氣", "嗚", "含淚", "心酸"];

    /// <param name="dialogueText">The exact quoted text of this turn (never logged verbatim).</param>
    /// <param name="precedingContext">A short slice of narration immediately before the quote —
    /// typically where a reporting clause like "他怒吼道" or "她笑著說" lives.</param>
    public static DialogueEmotion Classify(string dialogueText, string precedingContext)
    {
        ArgumentNullException.ThrowIfNull(dialogueText);
        ArgumentNullException.ThrowIfNull(precedingContext);

        if (ContainsAny(precedingContext, AngryCues) || ExclaimsHard(dialogueText))
        {
            return DialogueEmotion.Angry;
        }

        if (ContainsAny(precedingContext, NervousCues) || StutterPattern.IsMatch(dialogueText))
        {
            return DialogueEmotion.Nervous;
        }

        if (ContainsAny(precedingContext, SadCues))
        {
            return DialogueEmotion.Sad;
        }

        if (ContainsAny(precedingContext, HappyCues))
        {
            return DialogueEmotion.Happy;
        }

        return DialogueEmotion.Neutral;
    }

    public static (string RateDelta, string PitchDelta, string VolumeDelta) ToDeltas(DialogueEmotion emotion) =>
        emotion switch
        {
            DialogueEmotion.Nervous => ("+8%", "+10Hz", "+0%"),
            DialogueEmotion.Happy => ("+5%", "+15Hz", "+5%"),
            DialogueEmotion.Angry => ("+10%", "-5Hz", "+10%"),
            DialogueEmotion.Sad => ("-10%", "-10Hz", "-8%"),
            _ => ("+0%", "+0Hz", "+0%"),
        };

    /// <summary>The <see cref="CharacterVoiceSceneCodes"/> a cloned/designed situational voice
    /// would be filed under for this emotion — the shared vocabulary a custom-provider character's
    /// scene profile lookup uses instead of the plain rate/pitch/volume deltas in <see cref="ToDeltas"/>.</summary>
    public static string ToSceneCode(DialogueEmotion emotion) =>
        emotion switch
        {
            DialogueEmotion.Nervous => CharacterVoiceSceneCodes.Nervous,
            DialogueEmotion.Happy => CharacterVoiceSceneCodes.Happy,
            DialogueEmotion.Angry => CharacterVoiceSceneCodes.Angry,
            DialogueEmotion.Sad => CharacterVoiceSceneCodes.Sad,
            _ => CharacterVoiceSceneCodes.Neutral,
        };

    private static bool ExclaimsHard(string dialogueText) =>
        dialogueText.Count(ch => ch is '！' or '!') >= 2;

    private static bool ContainsAny(string text, string[] cues) =>
        cues.Any(cue => text.Contains(cue, StringComparison.Ordinal));
}
