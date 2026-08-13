using System.Text.RegularExpressions;
using StoryVoice.Application.Narrations.SpeechPlanning;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Deterministic, explainable speaker attribution: only confirms a speaker when a known
/// character's canonical name or alias sits directly next to a reporting verb ("陳大文說：") in
/// an adjacent Narrator segment. When no reporting verb is present, it falls back to a weaker
/// signal — a known character is simply the only one named anywhere in the adjacent Narrator
/// text ("陳大文轉過身，撿起筆"), which is common when a connector sentence describes what
/// someone is doing rather than explicitly saying they spoke; this only ever produces a
/// Suggested-confidence guess, never a Confirmed one, and stays silent (Unknown) the moment more
/// than one known name shows up in that same text, exactly like the reporting-clause rule does.
/// Everything else resolves to Unknown — this provider never guesses a new character into
/// existence.
///
/// First-person narrated books never name their narrator next to a reporting verb ("我說：",
/// never "陳大文說：" about themselves), so a series can name one cast member as the
/// <see cref="SpeakerAttributionRequest.PointOfViewCharacterId"/>: the literal pronoun "我" is
/// then treated exactly like that character's own name for both rules above, subject to the same
/// ambiguity guard as any other name.
/// </summary>
public sealed class RuleBasedSpeakerAttributionProvider : ISpeakerAttributionProvider
{
    private const string FirstPersonPronoun = "我";

    public Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
        SpeakerAttributionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var results = new List<SpeakerAttributionResult>();

        foreach (var segment in request.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Kind != SpeechSegmentKind.Dialogue)
            {
                continue;
            }

            var reportingMatch = FindReportingClauseSpeaker(request, segment);
            if (reportingMatch is not null)
            {
                results.Add(new SpeakerAttributionResult(
                    segment.Index,
                    reportingMatch.Value.CharacterId,
                    SpeakerAttributionOutcome.Confirmed,
                    92,
                    SpeakerAttributionDecisionSource.Rule,
                    reportingMatch.Value.ReasonCode));
                continue;
            }

            var soleActorMatch = FindSoleNamedActorSpeaker(request, segment);
            if (soleActorMatch is not null)
            {
                results.Add(new SpeakerAttributionResult(
                    segment.Index,
                    soleActorMatch.Value.CharacterId,
                    SpeakerAttributionOutcome.Suggested,
                    55,
                    SpeakerAttributionDecisionSource.Rule,
                    soleActorMatch.Value.ReasonCode));
                continue;
            }

            results.Add(new SpeakerAttributionResult(
                segment.Index,
                null,
                SpeakerAttributionOutcome.Unknown,
                0,
                SpeakerAttributionDecisionSource.Rule,
                "unknown_no_reporting_clause"));
        }

        return Task.FromResult<IReadOnlyList<SpeakerAttributionResult>>(results);
    }

    private static (Guid CharacterId, string ReasonCode)? FindReportingClauseSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        var neighborText = FindAdjacentNarratorText(request.Segments, dialogueSegment.Index);
        if (neighborText is null)
        {
            return null;
        }

        Guid? candidate = null;
        var reasonCode = "reporting_clause_exact_alias";
        foreach (var character in request.KnownCharacters)
        {
            foreach (var name in Names(character))
            {
                if (!HasReportingClauseFor(neighborText, name))
                {
                    continue;
                }

                if (candidate is not null && candidate != character.CharacterId)
                {
                    // Two different known names both look like reporting clauses in the same
                    // narrator text: ambiguous, do not guess.
                    return null;
                }

                candidate = character.CharacterId;
            }
        }

        if (request.PointOfViewCharacterId is Guid povCharacterId
            && HasReportingClauseFor(neighborText, FirstPersonPronoun))
        {
            if (candidate is not null && candidate != povCharacterId)
            {
                // A named cast member and the first-person narrator both look like reporting
                // clauses in the same narrator text: ambiguous, do not guess.
                return null;
            }

            candidate = povCharacterId;
            reasonCode = "reporting_clause_first_person_pov";
        }

        return candidate is Guid resolved ? (resolved, reasonCode) : null;
    }

    /// <summary>
    /// Weaker fallback for when no reporting verb is present: a known character (or the POV
    /// pronoun) is the only one mentioned anywhere in the adjacent Narrator text, regardless of
    /// what they're doing there. Two different known names in that same text means the text isn't
    /// unambiguously about one person — do not guess.
    /// </summary>
    private static (Guid CharacterId, string ReasonCode)? FindSoleNamedActorSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        var neighborText = FindAdjacentNarratorText(request.Segments, dialogueSegment.Index);
        if (neighborText is null)
        {
            return null;
        }

        Guid? candidate = null;
        var reasonCode = "narrator_sole_named_actor";
        foreach (var character in request.KnownCharacters)
        {
            if (!Names(character).Any(name =>
                !string.IsNullOrEmpty(name) && neighborText.Contains(name, StringComparison.Ordinal)))
            {
                continue;
            }

            if (candidate is not null && candidate != character.CharacterId)
            {
                // Two different known characters are both mentioned in the same connector text:
                // ambiguous, do not guess who it's actually about.
                return null;
            }

            candidate = character.CharacterId;
        }

        if (request.PointOfViewCharacterId is Guid povCharacterId
            && neighborText.Contains(FirstPersonPronoun, StringComparison.Ordinal))
        {
            if (candidate is not null && candidate != povCharacterId)
            {
                return null;
            }

            candidate = povCharacterId;
            reasonCode = "narrator_sole_named_actor_first_person_pov";
        }

        return candidate is Guid resolved ? (resolved, reasonCode) : null;
    }

    private static bool HasReportingClauseFor(string narratorText, string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName))
        {
            return false;
        }

        var escapedName = Regex.Escape(normalizedName);
        var verbPattern = string.Join('|', ReportingVerbCatalog.Verbs.Select(Regex.Escape));
        // Name immediately followed (allowing a little punctuation) by a reporting verb, or a
        // reporting verb immediately followed by the name ("XX說" / "說話的是 XX").
        var pattern = $"{escapedName}[，,：:、]{{0,2}}(?:{verbPattern})|(?:{verbPattern})[的是]{{0,2}}{escapedName}";
        return Regex.IsMatch(narratorText, pattern, RegexOptions.CultureInvariant);
    }

    private static string? FindAdjacentNarratorText(
        IReadOnlyList<SpeechSegmentAttributionInput> segments,
        int dialogueIndex)
    {
        var position = -1;
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Index == dialogueIndex)
            {
                position = i;
                break;
            }
        }

        if (position < 0)
        {
            return null;
        }

        var before = position > 0 ? segments[position - 1] : null;
        var after = position < segments.Count - 1 ? segments[position + 1] : null;
        var texts = new List<string>();
        if (before?.Kind == SpeechSegmentKind.Narrator) texts.Add(before.Text);
        if (after?.Kind == SpeechSegmentKind.Narrator) texts.Add(after.Text);
        return texts.Count == 0 ? null : string.Join('\n', texts);
    }

    private static IEnumerable<string> Names(KnownCharacterIdentity character)
    {
        yield return character.NormalizedCanonicalName;
        foreach (var alias in character.NormalizedAliases)
        {
            yield return alias;
        }
    }
}
