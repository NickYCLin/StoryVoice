using StoryVoice.Application.Narrations.SpeechPlanning;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Safety envelope around an inner <see cref="ISpeakerAttributionProvider"/> (today the rule
/// engine; later potentially an out-of-process local model). Enforces the constraints from the
/// multi-character plan: input is limited to the current series cast, output can only reference
/// a known character ID or resolve to Unknown, and any timeout, exception, malformed result, or
/// out-of-scope character ID from the inner provider is treated as untrusted and safely
/// downgraded to a review-worthy Unknown result rather than propagated or guessed.
/// </summary>
public sealed class LocalSpeakerAttributionProvider(
    ISpeakerAttributionProvider innerProvider,
    TimeSpan? timeout = null) : ISpeakerAttributionProvider
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
        SpeakerAttributionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var knownCharacterIds = request.KnownCharacters
            .Select(character => character.CharacterId)
            .ToHashSet();
        var dialogueSegmentIndexes = request.Segments
            .Where(segment => segment.Kind == SpeechSegmentKind.Dialogue)
            .Select(segment => segment.Index)
            .ToHashSet();

        IReadOnlyList<SpeakerAttributionResult>? rawResults;
        try
        {
            using var timeoutSource = new CancellationTokenSource(_timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            rawResults = await innerProvider.AttributeAsync(request, linkedSource.Token)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation propagates; an inner timeout, crash, or malformed-output
            // exception does not — it degrades to "everything needs review" instead.
            return FallbackToReview(dialogueSegmentIndexes, "attribution_provider_failed");
        }

        var validated = new List<SpeakerAttributionResult>(rawResults.Count);
        var coveredIndexes = new HashSet<int>();
        foreach (var result in rawResults)
        {
            if (!dialogueSegmentIndexes.Contains(result.SegmentIndex))
            {
                // The inner provider attributed a segment we never asked about (or a Narrator
                // segment) — drop it rather than trust an out-of-scope claim.
                continue;
            }

            coveredIndexes.Add(result.SegmentIndex);
            if (result.CharacterId is Guid characterId && !knownCharacterIds.Contains(characterId))
            {
                validated.Add(result with
                {
                    CharacterId = null,
                    Outcome = SpeakerAttributionOutcome.Unknown,
                    Confidence = 0,
                    ReasonCode = "unknown_character_id_rejected",
                });
                continue;
            }

            validated.Add(result);
        }

        foreach (var missingIndex in dialogueSegmentIndexes.Except(coveredIndexes))
        {
            validated.Add(new SpeakerAttributionResult(
                missingIndex,
                null,
                SpeakerAttributionOutcome.Unknown,
                0,
                SpeakerAttributionDecisionSource.LocalModel,
                "attribution_provider_missing_result"));
        }

        return validated;
    }

    private static IReadOnlyList<SpeakerAttributionResult> FallbackToReview(
        IReadOnlySet<int> dialogueSegmentIndexes,
        string reasonCode) =>
        dialogueSegmentIndexes
            .Select(index => new SpeakerAttributionResult(
                index,
                null,
                SpeakerAttributionOutcome.Unknown,
                0,
                SpeakerAttributionDecisionSource.LocalModel,
                reasonCode))
            .ToArray();
}
