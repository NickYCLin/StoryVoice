using StoryVoice.Application.Narrations.SpeechPlanning;

namespace StoryVoice.UnitTests;

public sealed class ChineseSpeechSegmenterTests
{
    private readonly ChineseSpeechSegmenter _segmenter = new();

    [Fact]
    public void Segment_models_the_chapter_title_as_a_separate_narrator_turn()
    {
        const string title = "第一章　月夜";
        const string body = "月光照在石階上。風很輕。";

        var plan = _segmenter.Segment(title, body);

        Assert.Equal(SpeechPlanStatus.Ready, plan.Status);
        Assert.Equal(ChineseSpeechSegmenter.AlgorithmVersion, plan.AlgorithmVersion);
        Assert.Equal(new ChapterTitleNarratorTurn(0, title.Length), plan.TitleTurn);
        var segment = Assert.Single(plan.BodySegments);
        Assert.Equal(new SpeechSegment(0, SpeechSegmentKind.Narrator, 0, body.Length, SpeechSegmentStatus.Ready), segment);
        AssertReconstructsExactly(body, plan);
    }

    [Fact]
    public void Segment_recognizes_supported_quote_pairs_and_keeps_adjacent_dialogue_separate()
    {
        const string body = "夜雨落下。「走吧！」「等等……」她回答：『好。』“現在出發。”";

        var plan = _segmenter.Segment(null, body);

        Assert.Null(plan.TitleTurn);
        Assert.Equal(SpeechPlanStatus.Ready, plan.Status);
        Assert.Equal(
            [
                SpeechSegmentKind.Narrator,
                SpeechSegmentKind.Dialogue,
                SpeechSegmentKind.Dialogue,
                SpeechSegmentKind.Narrator,
                SpeechSegmentKind.Dialogue,
                SpeechSegmentKind.Dialogue
            ],
            plan.BodySegments.Select(segment => segment.Kind));
        Assert.Equal(
            ["夜雨落下。", "「走吧！」", "「等等……」", "她回答：", "『好。』", "“現在出發。”"],
            plan.BodySegments.Select(segment => Slice(body, segment)));
        AssertReconstructsExactly(body, plan);
    }

    [Fact]
    public void Segment_keeps_cross_line_dialogue_and_nested_quotes_in_one_outer_turn()
    {
        const string body = "他低聲說：「第一行，\n『別回頭！』\n第三行。」天亮了。";

        var plan = _segmenter.Segment("雨夜", body);

        Assert.Equal(SpeechPlanStatus.Ready, plan.Status);
        Assert.Equal(
            [SpeechSegmentKind.Narrator, SpeechSegmentKind.Dialogue, SpeechSegmentKind.Narrator],
            plan.BodySegments.Select(segment => segment.Kind));
        Assert.Equal("「第一行，\n『別回頭！』\n第三行。」", Slice(body, plan.BodySegments[1]));
        Assert.All(plan.BodySegments, segment => Assert.Equal(SpeechSegmentStatus.Ready, segment.Status));
        AssertReconstructsExactly(body, plan);
    }

    [Fact]
    public void Segment_marks_an_unclosed_quote_for_review_without_losing_following_text()
    {
        const string body = "前文。「還沒說完，\n下一行仍在原文中。";

        var plan = _segmenter.Segment(null, body);

        Assert.Equal(SpeechPlanStatus.NeedsReview, plan.Status);
        Assert.Equal(2, plan.BodySegments.Count);
        Assert.Equal(SpeechSegmentStatus.Ready, plan.BodySegments[0].Status);
        Assert.Equal(SpeechSegmentStatus.NeedsReview, plan.BodySegments[1].Status);
        Assert.Equal("「還沒說完，\n下一行仍在原文中。", Slice(body, plan.BodySegments[1]));
        AssertReconstructsExactly(body, plan);
    }

    [Fact]
    public void Segment_marks_unexpected_or_mismatched_closing_quotes_for_review()
    {
        const string unexpectedClosing = "風停了。”眾人沉默。";
        const string mismatchedNested = "旁白。『外層「內層。』尾聲。";

        var unexpectedPlan = _segmenter.Segment(null, unexpectedClosing);
        var mismatchedPlan = _segmenter.Segment(null, mismatchedNested);

        Assert.Equal(SpeechPlanStatus.NeedsReview, unexpectedPlan.Status);
        Assert.Equal(SpeechSegmentKind.Narrator, Assert.Single(unexpectedPlan.BodySegments).Kind);
        Assert.Equal(SpeechSegmentStatus.NeedsReview, unexpectedPlan.BodySegments[0].Status);
        Assert.Equal(SpeechPlanStatus.NeedsReview, mismatchedPlan.Status);
        Assert.Equal(
            ["旁白。", "『外層「內層。』", "尾聲。"],
            mismatchedPlan.BodySegments.Select(segment => Slice(mismatchedNested, segment)));
        Assert.Equal(SpeechSegmentStatus.NeedsReview, mismatchedPlan.BodySegments[1].Status);
        Assert.Equal(SpeechSegmentKind.Narrator, mismatchedPlan.BodySegments[2].Kind);
        Assert.Equal(SpeechSegmentStatus.Ready, mismatchedPlan.BodySegments[2].Status);
        AssertReconstructsExactly(unexpectedClosing, unexpectedPlan);
        AssertReconstructsExactly(mismatchedNested, mismatchedPlan);
    }

    [Fact]
    public void Segment_preserves_exact_utf16_offsets_for_emoji_before_inside_and_after_dialogue()
    {
        const string body = "🙂前「🙂話」🙂後";

        var plan = _segmenter.Segment(null, body);

        Assert.Equal(SpeechPlanStatus.Ready, plan.Status);
        Assert.Equal(
            [
                new SpeechSegment(0, SpeechSegmentKind.Narrator, 0, 3, SpeechSegmentStatus.Ready),
                new SpeechSegment(1, SpeechSegmentKind.Dialogue, 3, 5, SpeechSegmentStatus.Ready),
                new SpeechSegment(2, SpeechSegmentKind.Narrator, 8, 3, SpeechSegmentStatus.Ready)
            ],
            plan.BodySegments);
        Assert.Equal(["🙂前", "「🙂話」", "🙂後"], plan.BodySegments.Select(segment => Slice(body, segment)));
        AssertReconstructsExactly(body, plan);
    }

    [Fact]
    public void Segment_returns_a_ready_zero_segment_plan_for_an_empty_body_and_preserves_title_behavior()
    {
        const string title = "空章🙂";

        var titledPlan = _segmenter.Segment(title, string.Empty);
        var untitledPlan = _segmenter.Segment(null, string.Empty);

        Assert.Equal(SpeechPlanStatus.Ready, titledPlan.Status);
        Assert.Equal(new ChapterTitleNarratorTurn(0, title.Length), titledPlan.TitleTurn);
        Assert.Empty(titledPlan.BodySegments);
        Assert.Equal(SpeechPlanStatus.Ready, untitledPlan.Status);
        Assert.Null(untitledPlan.TitleTurn);
        Assert.Empty(untitledPlan.BodySegments);
        AssertReconstructsExactly(string.Empty, titledPlan);
        AssertReconstructsExactly(string.Empty, untitledPlan);
    }

    [Fact(Timeout = 5_000)]
    public void Segment_handles_many_absent_mismatched_closers_without_repeated_stack_scans()
    {
        const int quoteCount = 500_000;
        var body = string.Concat(new string('「', quoteCount), new string('』', quoteCount));

        var plan = _segmenter.Segment(null, body);

        Assert.Equal(SpeechPlanStatus.NeedsReview, plan.Status);
        var dialogue = Assert.Single(plan.BodySegments);
        Assert.Equal(
            new SpeechSegment(0, SpeechSegmentKind.Dialogue, 0, body.Length, SpeechSegmentStatus.NeedsReview),
            dialogue);
        AssertReconstructsExactly(body, plan);
    }

    [Theory]
    [InlineData("")]
    [InlineData("純旁白，沒有引號。")]
    [InlineData("「甲。」「乙！」")]
    [InlineData("開頭『跨行\n對話』結尾")]
    [InlineData("旁白。「外層『內層？』外層！」尾聲。")]
    [InlineData("錯誤的右引號」仍保留。")]
    [InlineData("未閉合“對話與🙂字元")]
    public void Every_body_is_covered_once_in_utf16_source_order(string body)
    {
        var plan = _segmenter.Segment("合成測試章名", body);

        AssertReconstructsExactly(body, plan);
    }

    [Fact]
    public void Segment_is_deterministic_and_hashes_exact_title_and_body_source()
    {
        const string title = "序章";
        const string body = "旁白。「你好。」\n第二行。";

        var first = _segmenter.Segment(title, body);
        var second = _segmenter.Segment(title, body);
        var changedTitle = _segmenter.Segment("第一章", body);
        var changedNewline = _segmenter.Segment(title, body.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(64, first.SourceHash.Length);
        Assert.Equal("f30e4b1ba6f69051faad4c1d9986a33c438bd583c8cb8520b0f04fe46d12d416", first.SourceHash);
        Assert.Matches("^[0-9a-f]{64}$", first.SourceHash);
        Assert.Equal(first.SourceHash, second.SourceHash);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.TitleTurn, second.TitleTurn);
        Assert.Equal(first.BodySegments, second.BodySegments);
        Assert.NotEqual(first.SourceHash, changedTitle.SourceHash);
        Assert.NotEqual(first.SourceHash, changedNewline.SourceHash);
    }

    [Fact]
    public void Source_hash_frames_title_and_body_so_equal_concatenations_do_not_collide()
    {
        var first = _segmenter.Segment("a", "bc");
        var second = _segmenter.Segment("ab", "c");

        Assert.NotEqual(first.SourceHash, second.SourceHash);
    }

    [Fact]
    public void Segment_contracts_neither_duplicate_source_text_nor_assign_a_speaker()
    {
        var plan = _segmenter.Segment(null, "「由後續流程辨識說話者。」");

        var dialogue = Assert.Single(plan.BodySegments);
        Assert.Equal(SpeechSegmentKind.Dialogue, dialogue.Kind);
        var propertyNames = typeof(SpeechSegment).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(propertyNames, name => name.Contains("Speaker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name is "Text" or "Content" or "SourceText");
    }

    private static string Slice(string source, SpeechSegment segment) =>
        source.Substring(segment.StartOffset, segment.Length);

    private static void AssertReconstructsExactly(string body, ChapterSpeechPlan plan)
    {
        var expectedOffset = 0;
        foreach (var segment in plan.BodySegments)
        {
            Assert.Equal(expectedOffset, segment.StartOffset);
            Assert.True(segment.Length > 0);
            Assert.InRange(segment.StartOffset + segment.Length, 0, body.Length);
            expectedOffset += segment.Length;
        }

        Assert.Equal(body.Length, expectedOffset);
        Assert.Equal(
            body,
            string.Concat(plan.BodySegments.Select(segment => Slice(body, segment))));
        Assert.Equal(
            Enumerable.Range(0, plan.BodySegments.Count),
            plan.BodySegments.Select(segment => segment.Index));
    }
}
