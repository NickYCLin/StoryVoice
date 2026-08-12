using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Domain.Narrations;
using StoryVoice.Worker;

namespace StoryVoice.UnitTests;

public sealed class MultiCharacterTurnBuilderTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid SeriesId = Guid.NewGuid();
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid AliceId = Guid.NewGuid();
    private static readonly ChineseSpeechSegmenter Segmenter = new();

    [Fact]
    public void Narrator_and_dialogue_segments_resolve_to_the_narrator_and_assigned_character_voices()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");
        var chapter = BuildConfirmedChapter(0, "序章", "「你回來了？」艾莉絲說。", AliceId);

        var turns = MultiCharacterTurnBuilder.BuildTurns(castRevision, [chapter]);

        Assert.True(turns.Count >= 2);
        Assert.Equal("narrator-voice", turns[0].Voice);
        Assert.Equal("-2Hz", turns[0].Pitch);
        Assert.Equal("-3%", turns[0].Volume);
        var characterTurn = Assert.Single(turns, turn => turn.Voice == "alice-voice");
        Assert.Equal("+4Hz", characterTurn.Pitch);
        Assert.Equal("+2%", characterTurn.Volume);
    }

    [Fact]
    public void Adjacent_same_voice_segments_within_a_chapter_merge_into_one_turn()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");
        // Two consecutive narrator sentences with no dialogue in between should merge.
        var chapter = BuildConfirmedChapter(0, "序章", "風穿過長廊。吹熄了燈。", AliceId);

        var turns = MultiCharacterTurnBuilder.BuildTurns(castRevision, [chapter]);

        var narratorTurns = turns.Where(turn => turn.Voice == "narrator-voice").ToArray();
        Assert.Single(narratorTurns);
        Assert.Contains("風穿過長廊", narratorTurns[0].Text);
        Assert.Contains("吹熄了燈", narratorTurns[0].Text);
    }

    [Fact]
    public void Same_voice_segments_never_merge_across_a_chapter_boundary()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");
        // Chapter 1 ends on a narrator turn ("艾莉絲說。"); chapter 2 opens on a narrator turn too
        // (its title). Both are the same voice and would normally merge — except one is the first
        // segment of a new chapter, which must always force a fresh turn.
        var chapterOne = BuildConfirmedChapter(0, "第一章", "「你好。」艾莉絲說。", AliceId);
        var chapterTwo = BuildConfirmedChapter(1, "第二章", "雨還在下。", AliceId);

        var turns = MultiCharacterTurnBuilder.BuildTurns(castRevision, [chapterOne, chapterTwo]);

        var narratorTurns = turns.Where(turn => turn.Voice == "narrator-voice").ToArray();
        Assert.Equal(3, narratorTurns.Length); // chapter-1 title, chapter-1 trailing narration, chapter-2 title+body
        var chapterTwoTurn = Assert.Single(turns, turn => turn.Text.Contains("第二章"));
        Assert.DoesNotContain("艾莉絲說", chapterTwoTurn.Text);
        Assert.Equal(castRevision.ChapterPauseMs, chapterTwoTurn.PauseBeforeMs);
    }

    [Fact]
    public void Chapter_boundary_uses_the_chapter_pause_and_speaker_change_uses_the_speaker_pause()
    {
        var castRevision = BuildCastRevision(
            narratorVoice: "narrator-voice",
            aliceVoice: "alice-voice",
            defaultSpeakerPauseMs: 200,
            chapterPauseMs: 900);
        var chapterOne = BuildConfirmedChapter(0, "第一章", "「你回來了？」艾莉絲說。", AliceId);
        var chapterTwo = BuildConfirmedChapter(1, "第二章", "雨還在下。", AliceId);

        var turns = MultiCharacterTurnBuilder.BuildTurns(castRevision, [chapterOne, chapterTwo]);

        Assert.Equal(0, turns[0].PauseBeforeMs); // very first turn overall: no pause needed
        Assert.Contains(turns, turn => turn.PauseBeforeMs == 200); // narrator -> Alice speaker change
        var secondChapterTitleTurnIndex = turns.ToList().FindIndex(turn => turn.Text.Contains("第二章"));
        Assert.Equal(900, turns[secondChapterTitleTurnIndex].PauseBeforeMs);
    }

    [Fact]
    public void A_dialogue_segment_with_no_matching_cast_assignment_safely_falls_back_to_the_narrator_voice()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");
        var removedCharacterId = Guid.NewGuid();
        var chapter = BuildConfirmedChapter(0, "序章", "「快走。」他說。", removedCharacterId);

        var turns = MultiCharacterTurnBuilder.BuildTurns(castRevision, [chapter]);

        Assert.All(turns, turn => Assert.Equal("narrator-voice", turn.Voice));
    }

    [Fact]
    public void Throws_integrity_exception_when_the_chapter_title_changed_after_confirmation()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");
        var chapter = BuildConfirmedChapter(0, "序章", "「你回來了？」艾莉絲說。", AliceId);
        // Same length as the original title (so offsets still fit) but different content, so the
        // recomputed segment text hash can never match what was hashed at confirmation time.
        var tamperedChapter = chapter with { ChapterTitle = "偽章" };

        var exception = Assert.Throws<SpeechPlanIntegrityException>(
            () => MultiCharacterTurnBuilder.BuildTurns(castRevision, [tamperedChapter]));
        Assert.Equal(MultiCharacterTurnBuilder.IntegrityMismatchReasonCode, exception.ReasonCode);
    }

    [Fact]
    public void Throws_integrity_exception_when_a_segment_offset_no_longer_fits_the_chapter_text()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");
        var chapter = BuildConfirmedChapter(0, "序章", "「你回來了？」艾莉絲說。", AliceId);
        var truncatedChapter = chapter with { ChapterBody = "短" };

        Assert.Throws<SpeechPlanIntegrityException>(
            () => MultiCharacterTurnBuilder.BuildTurns(castRevision, [truncatedChapter]));
    }

    [Fact]
    public void Throws_integrity_exception_for_an_empty_chapter_plan_list()
    {
        var castRevision = BuildCastRevision(narratorVoice: "narrator-voice", aliceVoice: "alice-voice");

        Assert.Throws<SpeechPlanIntegrityException>(
            () => MultiCharacterTurnBuilder.BuildTurns(castRevision, []));
    }

    private static NarrationCastRevision BuildCastRevision(
        string narratorVoice,
        string aliceVoice,
        int defaultSpeakerPauseMs = 200,
        int chapterPauseMs = 800)
    {
        var revisionId = Guid.NewGuid();
        var assignment = NarrationCastAssignment.Create(
            Guid.NewGuid(),
            OwnerId,
            SeriesId,
            revisionId,
            AliceId,
            "艾莉絲",
            "edge",
            "1",
            aliceVoice,
            "+0%",
            "+4Hz",
            "+2%");
        return NarrationCastRevision.Create(
            revisionId,
            OwnerId,
            SeriesId,
            revisionNumber: 1,
            narratorProvider: "edge",
            narratorProviderVersion: "1",
            narratorVoice: narratorVoice,
            narratorRate: "-5%",
            narratorPitch: "-2Hz",
            narratorVolume: "-3%",
            defaultSpeakerPauseMs: defaultSpeakerPauseMs,
            chapterPauseMs: chapterPauseMs,
            compositionVersion: "v1",
            ffmpegProfile: "default",
            createdAt: DateTimeOffset.UtcNow,
            assignments: [assignment]);
    }

    private static ChapterPlanSource BuildConfirmedChapter(
        int chapterSortOrder,
        string title,
        string body,
        Guid dialogueCharacterId)
    {
        var chapterId = Guid.NewGuid();
        var plan = Segmenter.Segment(title, body);
        var segments = new List<DraftSegmentInput>
        {
            new(
                0,
                SpeechSegmentSourceKind.ChapterTitle,
                plan.TitleTurn!.StartOffset,
                plan.TitleTurn.Length,
                HashSlice(title[plan.TitleTurn.StartOffset..(plan.TitleTurn.StartOffset + plan.TitleTurn.Length)]),
                SpeechSegmentTurnKind.Narrator,
                null,
                100,
                SpeechSegmentDecisionSource.Rule,
                SpeechSegmentReviewStatus.Confirmed),
        };
        foreach (var bodySegment in plan.BodySegments)
        {
            var sortOrder = segments.Count;
            var text = body.Substring(bodySegment.StartOffset, bodySegment.Length);
            var kind = bodySegment.Kind == SpeechSegmentKind.Dialogue
                ? SpeechSegmentTurnKind.Dialogue
                : SpeechSegmentTurnKind.Narrator;
            segments.Add(new DraftSegmentInput(
                sortOrder,
                SpeechSegmentSourceKind.Body,
                bodySegment.StartOffset,
                bodySegment.Length,
                HashSlice(text),
                kind,
                kind == SpeechSegmentTurnKind.Dialogue ? dialogueCharacterId : null,
                100,
                SpeechSegmentDecisionSource.Rule,
                SpeechSegmentReviewStatus.Confirmed));
        }

        var draft = ChapterSpeechPlanDraft.Create(OwnerId, SeriesId, BookId, chapterId, plan.SourceHash, segments);
        var revision = draft.Confirm(1, DateTimeOffset.UtcNow);
        return new ChapterPlanSource(chapterSortOrder, revision, title, body);
    }

    private static string HashSlice(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
}
