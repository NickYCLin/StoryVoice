using StoryVoice.Domain.Narrations;

namespace StoryVoice.UnitTests;

public sealed class ChapterSpeechPlanDraftTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid SeriesId = Guid.NewGuid();
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid ChapterId = Guid.NewGuid();
    private static readonly Guid AliceId = Guid.NewGuid();

    [Fact]
    public void Create_requires_the_first_segment_to_be_the_chapter_title_narrator_turn()
    {
        var segments = new[]
        {
            new DraftSegmentInput(0, SpeechSegmentSourceKind.Body, 0, 5, Hash("body"), SpeechSegmentTurnKind.Narrator, null, 100, SpeechSegmentDecisionSource.Rule, SpeechSegmentReviewStatus.Confirmed),
        };

        Assert.Throws<ArgumentException>(() => CreateDraft(segments));
    }

    [Fact]
    public void Create_rejects_gaps_or_duplicate_sort_orders()
    {
        var segments = new[]
        {
            TitleTurn(),
            new DraftSegmentInput(2, SpeechSegmentSourceKind.Body, 5, 5, Hash("a"), SpeechSegmentTurnKind.Narrator, null, 100, SpeechSegmentDecisionSource.Rule, SpeechSegmentReviewStatus.Confirmed),
        };

        Assert.Throws<ArgumentException>(() => CreateDraft(segments));
    }

    [Fact]
    public void Create_rejects_a_second_chapter_title_segment()
    {
        var segments = new[]
        {
            TitleTurn(),
            new DraftSegmentInput(1, SpeechSegmentSourceKind.ChapterTitle, 0, 5, Hash("title2"), SpeechSegmentTurnKind.Narrator, null, 100, SpeechSegmentDecisionSource.Rule, SpeechSegmentReviewStatus.Confirmed),
        };

        Assert.Throws<ArgumentException>(() => CreateDraft(segments));
    }

    [Fact]
    public void Dialogue_segments_must_come_from_the_chapter_body_not_the_title()
    {
        var segments = new[]
        {
            TitleTurn(),
            new DraftSegmentInput(1, SpeechSegmentSourceKind.ChapterTitle, 0, 5, Hash("x"), SpeechSegmentTurnKind.Dialogue, AliceId, 90, SpeechSegmentDecisionSource.Rule, SpeechSegmentReviewStatus.Suggested),
        };

        Assert.Throws<ArgumentException>(() => CreateDraft(segments));
    }

    [Fact]
    public void Narrator_segments_cannot_carry_a_character_id()
    {
        var segments = new[]
        {
            TitleTurn(),
            new DraftSegmentInput(1, SpeechSegmentSourceKind.Body, 0, 5, Hash("n"), SpeechSegmentTurnKind.Narrator, AliceId, 100, SpeechSegmentDecisionSource.Rule, SpeechSegmentReviewStatus.Confirmed),
        };

        Assert.Throws<ArgumentException>(() => CreateDraft(segments));
    }

    [Fact]
    public void Draft_needs_review_while_any_dialogue_segment_is_unconfirmed_and_is_ready_once_all_confirmed()
    {
        var draft = CreateDraft(
        [
            TitleTurn(),
            Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
        ]);

        Assert.Equal(ChapterSpeechPlanDraftStatus.NeedsReview, draft.Status);

        draft.ConfirmSegment(draft.Segments[1].Id, AliceId);

        Assert.Equal(ChapterSpeechPlanDraftStatus.ReadyToConfirm, draft.Status);
    }

    [Fact]
    public void Draft_with_only_narrator_segments_is_ready_to_confirm_immediately()
    {
        var draft = CreateDraft([TitleTurn()]);

        Assert.Equal(ChapterSpeechPlanDraftStatus.ReadyToConfirm, draft.Status);
    }

    [Fact]
    public void Confirming_a_segment_locks_in_a_possibly_different_character_and_marks_it_user_decided()
    {
        var bobId = Guid.NewGuid();
        var draft = CreateDraft(
        [
            TitleTurn(),
            Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
        ]);

        draft.ConfirmSegment(draft.Segments[1].Id, bobId);

        var segment = draft.Segments[1];
        Assert.Equal(bobId, segment.CharacterId);
        Assert.Equal(SpeechSegmentReviewStatus.Confirmed, segment.ReviewStatus);
        Assert.Equal(SpeechSegmentDecisionSource.User, segment.DecisionSource);
        Assert.Equal(100, segment.Confidence);
    }

    [Fact]
    public void Confirming_a_segment_to_null_records_an_explicit_narrator_fallback()
    {
        var draft = CreateDraft(
        [
            TitleTurn(),
            Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
        ]);

        draft.ConfirmSegment(draft.Segments[1].Id, null);

        Assert.Null(draft.Segments[1].CharacterId);
        Assert.Equal(SpeechSegmentReviewStatus.Confirmed, draft.Segments[1].ReviewStatus);
        Assert.Equal(ChapterSpeechPlanDraftStatus.ReadyToConfirm, draft.Status);
    }

    [Fact]
    public void Rejecting_a_segment_clears_the_suggestion_and_keeps_the_draft_in_review()
    {
        var draft = CreateDraft(
        [
            TitleTurn(),
            Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
        ]);

        draft.RejectSegment(draft.Segments[1].Id);

        Assert.Null(draft.Segments[1].CharacterId);
        Assert.Equal(SpeechSegmentReviewStatus.Rejected, draft.Segments[1].ReviewStatus);
        Assert.Equal(ChapterSpeechPlanDraftStatus.NeedsReview, draft.Status);
    }

    [Fact]
    public void Narrator_segments_cannot_be_confirmed_or_rejected_by_a_human()
    {
        var draft = CreateDraft([TitleTurn()]);

        Assert.Throws<InvalidOperationException>(() => draft.ConfirmSegment(draft.Segments[0].Id, null));
        Assert.Throws<InvalidOperationException>(() => draft.RejectSegment(draft.Segments[0].Id));
    }

    [Fact]
    public void Stale_draft_rejects_review_until_regenerated()
    {
        var draft = CreateDraft(
        [
            TitleTurn(),
            Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
        ]);
        draft.MarkStale();

        Assert.Equal(ChapterSpeechPlanDraftStatus.Stale, draft.Status);
        Assert.Throws<InvalidOperationException>(() => draft.ConfirmSegment(draft.Segments[1].Id, AliceId));

        draft.RegenerateFromSegmentation(Hash("chapter-v2"), [TitleTurn()]);

        Assert.Equal(ChapterSpeechPlanDraftStatus.ReadyToConfirm, draft.Status);
        Assert.Equal(2, draft.PlanVersion);
    }

    [Fact]
    public void Confirm_throws_unless_the_draft_is_ready_and_produces_an_immutable_revision_matching_segments()
    {
        var draft = CreateDraft(
        [
            TitleTurn(),
            Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
        ]);

        Assert.Throws<InvalidOperationException>(() => draft.Confirm(1, DateTimeOffset.UtcNow));

        draft.ConfirmSegment(draft.Segments[1].Id, AliceId);
        var revision = draft.Confirm(1, DateTimeOffset.UtcNow);

        Assert.Equal(OwnerId, revision.OwnerId);
        Assert.Equal(SeriesId, revision.SeriesId);
        Assert.Equal(BookId, revision.BookId);
        Assert.Equal(ChapterId, revision.ChapterId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(draft.SourceHash, revision.SourceHash);
        Assert.Equal(2, revision.Segments.Count);
        Assert.Equal(AliceId, revision.Segments[1].CharacterId);
        Assert.False(string.IsNullOrWhiteSpace(revision.Fingerprint));
    }

    [Fact]
    public void Same_confirmed_segments_produce_the_same_fingerprint_and_any_field_change_produces_a_different_one()
    {
        ConfirmedSpeechPlanRevision Build(Guid? characterId)
        {
            var draft = CreateDraft(
            [
                TitleTurn(),
                Dialogue(1, AliceId, SpeechSegmentReviewStatus.Suggested),
            ]);
            draft.ConfirmSegment(draft.Segments[1].Id, characterId);
            return draft.Confirm(1, DateTimeOffset.UtcNow);
        }

        var first = Build(AliceId);
        var second = Build(AliceId);
        var differentCharacter = Build(Guid.NewGuid());

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Fingerprint, differentCharacter.Fingerprint);
    }

    private static ChapterSpeechPlanDraft CreateDraft(IReadOnlyList<DraftSegmentInput> segments) =>
        ChapterSpeechPlanDraft.Create(OwnerId, SeriesId, BookId, ChapterId, Hash("chapter-v1"), segments);

    private static DraftSegmentInput TitleTurn() =>
        new(0, SpeechSegmentSourceKind.ChapterTitle, 0, 4, Hash("title"), SpeechSegmentTurnKind.Narrator, null, 100, SpeechSegmentDecisionSource.Rule, SpeechSegmentReviewStatus.Confirmed);

    private static DraftSegmentInput Dialogue(int sortOrder, Guid? characterId, SpeechSegmentReviewStatus reviewStatus) =>
        new(sortOrder, SpeechSegmentSourceKind.Body, 10, 6, Hash($"dialogue-{sortOrder}"), SpeechSegmentTurnKind.Dialogue, characterId, 60, SpeechSegmentDecisionSource.Rule, reviewStatus);

    private static string Hash(string seed) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
}
