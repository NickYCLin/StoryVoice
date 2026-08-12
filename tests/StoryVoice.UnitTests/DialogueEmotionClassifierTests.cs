using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class DialogueEmotionClassifierTests
{
    [Fact]
    public void Reporting_clause_with_an_anger_verb_classifies_as_angry()
    {
        var emotion = DialogueEmotionClassifier.Classify("你到底想怎樣", "他氣得對我怒吼道：");

        Assert.Equal(DialogueEmotion.Angry, emotion);
    }

    [Fact]
    public void Double_exclamation_marks_classify_as_angry_even_without_a_reporting_clause()
    {
        var emotion = DialogueEmotionClassifier.Classify("你給我閉嘴！！", "");

        Assert.Equal(DialogueEmotion.Angry, emotion);
    }

    [Fact]
    public void Stuttered_dialogue_classifies_as_nervous()
    {
        var emotion = DialogueEmotionClassifier.Classify("我、我才沒有", "");

        Assert.Equal(DialogueEmotion.Nervous, emotion);
    }

    [Fact]
    public void Reporting_clause_with_a_nervous_cue_classifies_as_nervous()
    {
        var emotion = DialogueEmotionClassifier.Classify("沒事的", "她緊張的抖著聲音說：");

        Assert.Equal(DialogueEmotion.Nervous, emotion);
    }

    [Fact]
    public void Reporting_clause_with_a_smile_cue_classifies_as_happy()
    {
        var emotion = DialogueEmotionClassifier.Classify("好啊一起去", "她甜甜的笑了：");

        Assert.Equal(DialogueEmotion.Happy, emotion);
    }

    [Fact]
    public void Reporting_clause_with_a_sadness_cue_classifies_as_sad()
    {
        var emotion = DialogueEmotionClassifier.Classify("我們回不去了", "她含淚說道：");

        Assert.Equal(DialogueEmotion.Sad, emotion);
    }

    [Fact]
    public void Plain_dialogue_with_no_cues_classifies_as_neutral()
    {
        var emotion = DialogueEmotionClassifier.Classify("下下禮拜一。", "我立刻招了，");

        Assert.Equal(DialogueEmotion.Neutral, emotion);
    }

    [Fact]
    public void Neutral_emotion_produces_zero_deltas()
    {
        var deltas = DialogueEmotionClassifier.ToDeltas(DialogueEmotion.Neutral);

        Assert.Equal(("+0%", "+0Hz", "+0%"), deltas);
    }

    [Theory]
    [InlineData(DialogueEmotion.Nervous)]
    [InlineData(DialogueEmotion.Happy)]
    [InlineData(DialogueEmotion.Angry)]
    [InlineData(DialogueEmotion.Sad)]
    public void Non_neutral_emotions_produce_non_zero_deltas(DialogueEmotion emotion)
    {
        var deltas = DialogueEmotionClassifier.ToDeltas(emotion);

        Assert.False(deltas.RateDelta == "+0%" && deltas.PitchDelta == "+0Hz" && deltas.VolumeDelta == "+0%");
    }
}
