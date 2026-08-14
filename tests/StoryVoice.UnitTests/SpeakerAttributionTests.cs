using System.Reflection;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Infrastructure.Narrations;

namespace StoryVoice.UnitTests;

public sealed class SpeakerAttributionTests
{
    private static readonly Guid AliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly IReadOnlyList<KnownCharacterIdentity> Cast =
    [
        new KnownCharacterIdentity(AliceId, "艾莉絲", ["隊長"]),
        new KnownCharacterIdentity(BobId, "鮑伯", []),
    ];

    [Fact]
    public void Result_carries_no_source_text_only_ids_reason_codes_and_confidence()
    {
        // Mirrors the SpeechSegment contract test: whatever gets logged from a
        // SpeakerAttributionResult can never leak private chapter text.
        var forbidden = new[] { "Text", "Content", "Body", "SourceText" };
        foreach (var property in typeof(SpeakerAttributionResult).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.DoesNotContain(forbidden, keyword => property.Name.Contains(keyword, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Reporting_clause_with_exact_canonical_name_confirms_known_character()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「你回來了？」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "艾莉絲說。"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(0, result.SegmentIndex);
        Assert.Equal(AliceId, result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Confirmed, result.Outcome);
        Assert.Equal("reporting_clause_exact_alias", result.ReasonCode);
        Assert.Equal(SpeakerAttributionDecisionSource.Rule, result.Source);
        Assert.True(result.Confidence >= 90);
    }

    [Fact]
    public async Task Reporting_clause_with_alias_resolves_to_the_same_character_as_canonical_name()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Narrator, "隊長笑道："),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "「跟我來。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(AliceId, result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Confirmed, result.Outcome);
    }

    [Fact]
    public async Task First_person_reporting_clause_confirms_the_configured_point_of_view_character()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「這裡就是新學校了。」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "我說。"),
        ], AliceId);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(AliceId, result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Confirmed, result.Outcome);
        Assert.Equal("reporting_clause_first_person_pov", result.ReasonCode);
        Assert.True(result.Confidence >= 90);
    }

    [Fact]
    public async Task First_person_pronoun_without_a_configured_point_of_view_character_resolves_unknown()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「這裡就是新學校了。」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "我說。"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Bare_first_person_narration_does_not_make_an_adjacent_quote_the_point_of_view_speaker()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「這裡是哪裡？」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "我盯著窗外，沒有回答。"),
        ], AliceId);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
        Assert.Equal("unknown_no_reporting_clause", result.ReasonCode);
    }

    [Fact]
    public async Task Point_of_view_pronoun_competing_with_a_named_character_in_the_same_clause_is_ambiguous()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「小心！」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "我說：鮑伯說："),
        ], AliceId);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Ambiguous_pronoun_without_a_reporting_clause_resolves_unknown()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Narrator, "他望向窗外。"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "「快走。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task Multiple_known_names_in_the_same_narrator_text_are_ambiguous_and_resolve_unknown()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「小心！」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "艾莉絲說：鮑伯說："),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Unknown_name_that_is_not_in_the_cast_never_gets_invented_as_a_character()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「早安。」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "查理說。"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Sole_named_actor_in_connector_text_is_only_ever_a_suggestion_never_a_confirmation()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「你回來了？」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "艾莉絲說。"),
            new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Narrator, "艾莉絲頓了頓。"),
            new SpeechSegmentAttributionInput(3, SpeechSegmentKind.Dialogue, "「我等你很久了。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.Equal(2, results.Count);
        var first = results.Single(candidate => candidate.SegmentIndex == 0);
        var second = results.Single(candidate => candidate.SegmentIndex == 3);
        Assert.Equal(SpeakerAttributionOutcome.Confirmed, first.Outcome);
        Assert.Equal(AliceId, second.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Suggested, second.Outcome);
        Assert.Equal("narrator_sole_named_actor", second.ReasonCode);
        Assert.True(second.Confidence < first.Confidence);
    }

    [Fact]
    public async Task Sole_named_actor_needs_no_reporting_verb_and_attributes_both_surrounding_dialogue_lines()
    {
        // The motivating case: a connector sentence describes what someone is doing (not that
        // they're speaking), with no reporting verb at all. Both the dialogue line before and
        // after that connector should still be attributed to the one person named in it.
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「這樣喔，我聽說中縣有間學校工科感覺還不錯。」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "艾莉絲乾脆把椅子轉過來，拿了原子筆就畫圈圈，"),
            new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Dialogue, "「如果你也申請能過，我們還可以再當三年同學哩。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.All(results, result =>
        {
            Assert.Equal(AliceId, result.CharacterId);
            Assert.Equal(SpeakerAttributionOutcome.Suggested, result.Outcome);
            Assert.Equal("narrator_sole_named_actor", result.ReasonCode);
        });
    }

    [Fact]
    public async Task Title_bearing_named_actor_in_a_dialogue_bridge_suggests_both_surrounding_lines()
    {
        var luckyClassmateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(
            [new KnownCharacterIdentity(luckyClassmateId, "幸運同學", [])],
            [
                new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「這樣喔，我聽說中縣有間學校工科感覺還不錯。」"),
                new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "幸運同學乾脆把椅子轉過來，拿了原子筆就在我的單子空白處畫圈圈，"),
                new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Dialogue, "「如果你也申請能過，我們還可以再當三年同學哩。」"),
            ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal(luckyClassmateId, result.CharacterId);
            Assert.Equal(SpeakerAttributionOutcome.Suggested, result.Outcome);
            Assert.Equal("narrator_sole_named_actor", result.ReasonCode);
        });
    }

    [Fact]
    public async Task Sole_named_actor_switches_to_whichever_character_is_newly_named_in_between()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「你回來了？」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "艾莉絲說。"),
            new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Narrator, "鮑伯站在門口。"),
            new SpeechSegmentAttributionInput(3, SpeechSegmentKind.Dialogue, "「嗨。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var second = results.Single(candidate => candidate.SegmentIndex == 3);
        Assert.Equal(BobId, second.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Suggested, second.Outcome);
        Assert.Equal("narrator_sole_named_actor", second.ReasonCode);
    }

    [Fact]
    public async Task Sole_named_actor_stays_unknown_when_two_known_characters_share_the_connector_text()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Narrator, "艾莉絲拍了拍鮑伯的肩膀，"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "「走吧。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Descriptive_reporting_first_person_thought_and_reaction_continuation_are_attributed_in_order()
    {
        var deathGodId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var narratorId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(
        [
            new KnownCharacterIdentity(deathGodId, "死神", []),
            new KnownCharacterIdentity(narratorId, "主角", []),
        ],
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「你昏醒了？」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "死神轉過頭來，口氣非常之不好的對著我問。連忙用力點頭，"),
            new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Dialogue, "「我在陰間嗎？」"),
            new SpeechSegmentAttributionInput(3, SpeechSegmentKind.Narrator, "我想，這地方怎麼看都不像人間，一定是我沒死成又昏倒。眼前的漂亮死神不知道該怎麼辦。紅紅的眼睛瞪了我一眼，居然有點冷笑的，"),
            new SpeechSegmentAttributionInput(4, SpeechSegmentKind.Dialogue, "「如果你要當這裡是陰間也無所謂。」"),
        ], narratorId);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.Collection(
            results,
            first =>
            {
                Assert.Equal(0, first.SegmentIndex);
                Assert.Equal(deathGodId, first.CharacterId);
                Assert.Equal(SpeakerAttributionOutcome.Confirmed, first.Outcome);
                Assert.Equal("descriptive_reporting_clause_exact_alias", first.ReasonCode);
            },
            second =>
            {
                Assert.Equal(2, second.SegmentIndex);
                Assert.Equal(narratorId, second.CharacterId);
                Assert.Equal(SpeakerAttributionOutcome.Suggested, second.Outcome);
                Assert.Equal("first_person_dialogue_context_pov", second.ReasonCode);
            },
            third =>
            {
                Assert.Equal(4, third.SegmentIndex);
                Assert.Equal(deathGodId, third.CharacterId);
                Assert.Equal(SpeakerAttributionOutcome.Suggested, third.Outcome);
                Assert.Equal("named_reaction_continuation_alias", third.ReasonCode);
            });
    }

    [Fact]
    public async Task Reaction_continuation_does_not_reuse_a_name_from_more_than_one_preceding_sentence()
    {
        var deathGodId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(
        [new KnownCharacterIdentity(deathGodId, "死神", [])],
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Narrator, "死神走到窗邊。過了一會兒，房間安靜下來。有人冷笑了一聲，"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "「別再猜了。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task Narrator_segments_are_never_scored_only_dialogue_segments_are()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Narrator, "艾莉絲說。"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Local_provider_passes_through_healthy_inner_results_unchanged()
    {
        var request = DialogueOnlyRequest();
        var inner = new StubProvider((_, _) => Task.FromResult<IReadOnlyList<SpeakerAttributionResult>>(
        [
            new SpeakerAttributionResult(0, AliceId, SpeakerAttributionOutcome.Confirmed, 92, SpeakerAttributionDecisionSource.Rule, "reporting_clause_exact_alias"),
        ]));
        var provider = new LocalSpeakerAttributionProvider(inner);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(AliceId, result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Confirmed, result.Outcome);
    }

    [Fact]
    public async Task Local_provider_rejects_a_character_id_outside_the_known_cast()
    {
        var request = DialogueOnlyRequest();
        var strangerId = Guid.NewGuid();
        var inner = new StubProvider((_, _) => Task.FromResult<IReadOnlyList<SpeakerAttributionResult>>(
        [
            new SpeakerAttributionResult(0, strangerId, SpeakerAttributionOutcome.Confirmed, 99, SpeakerAttributionDecisionSource.LocalModel, "hallucinated"),
        ]));
        var provider = new LocalSpeakerAttributionProvider(inner);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
        Assert.Equal("unknown_character_id_rejected", result.ReasonCode);
    }

    [Fact]
    public async Task Local_provider_fills_in_missing_results_as_unknown_instead_of_dropping_segments()
    {
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「一」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Dialogue, "「二」"),
        ]);
        var inner = new StubProvider((_, _) => Task.FromResult<IReadOnlyList<SpeakerAttributionResult>>(
        [
            new SpeakerAttributionResult(0, AliceId, SpeakerAttributionOutcome.Confirmed, 92, SpeakerAttributionDecisionSource.Rule, "reporting_clause_exact_alias"),
        ]));
        var provider = new LocalSpeakerAttributionProvider(inner);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.Equal(2, results.Count);
        var missing = results.Single(candidate => candidate.SegmentIndex == 1);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, missing.Outcome);
        Assert.Equal("attribution_provider_missing_result", missing.ReasonCode);
    }

    [Fact]
    public async Task Local_provider_falls_back_to_review_when_the_inner_provider_throws()
    {
        var request = DialogueOnlyRequest();
        var inner = new StubProvider((_, _) => throw new InvalidOperationException("malformed model output"));
        var provider = new LocalSpeakerAttributionProvider(inner);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Null(result.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
        Assert.Equal("attribution_provider_failed", result.ReasonCode);
    }

    [Fact]
    public async Task Local_provider_falls_back_to_review_when_the_inner_provider_times_out()
    {
        var request = DialogueOnlyRequest();
        var inner = new StubProvider(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return (IReadOnlyList<SpeakerAttributionResult>)[];
        });
        var provider = new LocalSpeakerAttributionProvider(inner, TimeSpan.FromMilliseconds(20));

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SpeakerAttributionOutcome.Unknown, result.Outcome);
        Assert.Equal("attribution_provider_failed", result.ReasonCode);
    }

    [Fact]
    public async Task Local_provider_still_propagates_genuine_caller_cancellation()
    {
        var request = DialogueOnlyRequest();
        var inner = new StubProvider(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return (IReadOnlyList<SpeakerAttributionResult>)[];
        });
        var provider = new LocalSpeakerAttributionProvider(inner, TimeSpan.FromSeconds(30));
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.AttributeAsync(request, cancellationSource.Token));
    }

    private static SpeakerAttributionRequest DialogueOnlyRequest() =>
        new(Cast, [new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「你好。」")]);

    private sealed class StubProvider(
        Func<SpeakerAttributionRequest, CancellationToken, Task<IReadOnlyList<SpeakerAttributionResult>>> handler)
        : ISpeakerAttributionProvider
    {
        public Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
            SpeakerAttributionRequest request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
