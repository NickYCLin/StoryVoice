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
    public async Task Adjacent_turn_continuation_is_only_ever_a_suggestion_never_a_confirmation()
    {
        var provider = new RuleBasedSpeakerAttributionProvider();
        var request = new SpeakerAttributionRequest(Cast,
        [
            new SpeechSegmentAttributionInput(0, SpeechSegmentKind.Dialogue, "「你回來了？」"),
            new SpeechSegmentAttributionInput(1, SpeechSegmentKind.Narrator, "艾莉絲說。"),
            new SpeechSegmentAttributionInput(2, SpeechSegmentKind.Narrator, "她頓了頓。"),
            new SpeechSegmentAttributionInput(3, SpeechSegmentKind.Dialogue, "「我等你很久了。」"),
        ]);

        var results = await provider.AttributeAsync(request, CancellationToken.None);

        Assert.Equal(2, results.Count);
        var first = results.Single(candidate => candidate.SegmentIndex == 0);
        var second = results.Single(candidate => candidate.SegmentIndex == 3);
        Assert.Equal(SpeakerAttributionOutcome.Confirmed, first.Outcome);
        Assert.Equal(AliceId, second.CharacterId);
        Assert.Equal(SpeakerAttributionOutcome.Suggested, second.Outcome);
        Assert.Equal("adjacent_turn_continuation", second.ReasonCode);
        Assert.True(second.Confidence < first.Confidence);
    }

    [Fact]
    public async Task Continuation_stops_as_soon_as_a_competing_name_appears_in_between()
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
        Assert.Equal(SpeakerAttributionOutcome.Unknown, second.Outcome);
        Assert.Null(second.CharacterId);
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
