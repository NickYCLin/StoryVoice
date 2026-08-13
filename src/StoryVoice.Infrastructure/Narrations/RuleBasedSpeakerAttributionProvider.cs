using System.Text.RegularExpressions;
using StoryVoice.Application.Narrations.SpeechPlanning;

namespace StoryVoice.Infrastructure.Narrations;

/// <summary>
/// Deterministic, explainable speaker attribution: only confirms a speaker when a known
/// character's canonical name or alias sits directly next to a reporting verb ("陳大文說：") in
/// an adjacent Narrator segment, and only suggests turn continuation when the immediately
/// preceding dialogue turn was itself confirmed with no competing name in between. Everything
/// else resolves to Unknown — this provider never guesses a new character into existence.
///
/// First-person narrated books never name their narrator next to a reporting verb ("我說：",
/// never "陳大文說：" about themselves), so a series can name one cast member as the
/// <see cref="SpeakerAttributionRequest.PointOfViewCharacterId"/>: the literal pronoun "我" is
/// then treated exactly like that character's own name for reporting-clause matching, subject to
/// the same ambiguity rule as any other name.
/// </summary>
public sealed class RuleBasedSpeakerAttributionProvider : ISpeakerAttributionProvider
{
    private const string FirstPersonPronoun = "我";

    private static readonly string[] ReportingVerbs =
    [
        "笑著說", "低聲說", "輕聲說", "大聲說", "笑道", "喊道", "叫道", "應道", "回答",
        "回應", "問道", "說道", "說", "問", "答", "喊", "道",
    ];

    public Task<IReadOnlyList<SpeakerAttributionResult>> AttributeAsync(
        SpeakerAttributionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var results = new List<SpeakerAttributionResult>();
        Guid? lastConfirmedCharacterId = null;
        var sawCompetingNameSinceLastConfirmed = false;

        foreach (var segment in request.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Kind != SpeechSegmentKind.Dialogue)
            {
                if (segment.Kind == SpeechSegmentKind.Narrator
                    && ContainsAnyOtherKnownName(segment.Text, request.KnownCharacters, lastConfirmedCharacterId))
                {
                    sawCompetingNameSinceLastConfirmed = true;
                }

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
                lastConfirmedCharacterId = reportingMatch.Value.CharacterId;
                sawCompetingNameSinceLastConfirmed = false;
                continue;
            }

            if (lastConfirmedCharacterId is Guid continuation && !sawCompetingNameSinceLastConfirmed)
            {
                results.Add(new SpeakerAttributionResult(
                    segment.Index,
                    continuation,
                    SpeakerAttributionOutcome.Suggested,
                    55,
                    SpeakerAttributionDecisionSource.Rule,
                    "adjacent_turn_continuation"));
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

    private static bool HasReportingClauseFor(string narratorText, string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName))
        {
            return false;
        }

        var escapedName = Regex.Escape(normalizedName);
        var verbPattern = string.Join('|', ReportingVerbs.Select(Regex.Escape));
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

    private static bool ContainsAnyOtherKnownName(
        string narratorText,
        IReadOnlyList<KnownCharacterIdentity> knownCharacters,
        Guid? excludingCharacterId) =>
        knownCharacters
            .Where(character => character.CharacterId != excludingCharacterId)
            .Any(character => Names(character).Any(name =>
                !string.IsNullOrEmpty(name) && narratorText.Contains(name, StringComparison.Ordinal)));

    private static IEnumerable<string> Names(KnownCharacterIdentity character)
    {
        yield return character.NormalizedCanonicalName;
        foreach (var alias in character.NormalizedAliases)
        {
            yield return alias;
        }
    }
}
