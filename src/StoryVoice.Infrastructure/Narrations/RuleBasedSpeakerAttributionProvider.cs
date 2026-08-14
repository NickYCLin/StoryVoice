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
/// <see cref="SpeakerAttributionRequest.PointOfViewCharacterId"/>. The literal pronoun "我" is
/// accepted only in an explicit reporting or thought tag; ordinary narration containing "我" is
/// deliberately not treated as speaker evidence.
/// </summary>
public sealed class RuleBasedSpeakerAttributionProvider : ISpeakerAttributionProvider
{
    private const string FirstPersonPronoun = "我";
    private static readonly string DescriptiveReportingVerbPattern = string.Join(
        '|',
        ReportingVerbCatalog.Verbs
            .Where(verb => !StringComparer.Ordinal.Equals(verb, "道"))
            .OrderByDescending(verb => verb.Length)
            .Select(Regex.Escape));
    private static readonly Regex FirstPersonDialogueCue = new(
        @"^\s*我(?:心裡想|心想|暗想|想)(?:[，,:：。！？!?]|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReactionCue = new(
        @"(?:瞪|盯|冷笑|笑了|皺眉|回頭|轉頭|望向|看了)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

            var descriptiveReportingMatch = FindDescriptiveReportingClauseSpeaker(request, segment);
            if (descriptiveReportingMatch is not null)
            {
                results.Add(new SpeakerAttributionResult(
                    segment.Index,
                    descriptiveReportingMatch,
                    SpeakerAttributionOutcome.Confirmed,
                    88,
                    SpeakerAttributionDecisionSource.Rule,
                    "descriptive_reporting_clause_exact_alias"));
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

            var firstPersonMatch = FindFirstPersonDialogueSpeaker(request, segment);
            if (firstPersonMatch is not null)
            {
                results.Add(new SpeakerAttributionResult(
                    segment.Index,
                    firstPersonMatch,
                    SpeakerAttributionOutcome.Suggested,
                    74,
                    SpeakerAttributionDecisionSource.Rule,
                    "first_person_dialogue_context_pov"));
                continue;
            }

            var reactionContinuationMatch = FindReactionContinuationSpeaker(request, segment);
            if (reactionContinuationMatch is not null)
            {
                results.Add(new SpeakerAttributionResult(
                    segment.Index,
                    reactionContinuationMatch,
                    SpeakerAttributionOutcome.Suggested,
                    66,
                    SpeakerAttributionDecisionSource.Rule,
                    "named_reaction_continuation_alias"));
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

    /// <summary>
    /// Handles prose where the reporting verb is separated from the name by an action or
    /// description, such as 「死神轉過頭來，對著我問。」. It stays sentence-bounded and only accepts
    /// a known name that starts that reporting sentence, so an incidental name later in a long
    /// narrator passage cannot become a speaker by this rule.
    /// </summary>
    private static Guid? FindDescriptiveReportingClauseSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        var neighborTexts = FindReportingClauseScopes(request.Segments, dialogueSegment.Index);
        var candidates = request.KnownCharacters
            .Where(character => Names(character).Any(name => neighborTexts.Any(text =>
                HasDescriptiveReportingClauseFor(text, name))))
            .Select(character => character.CharacterId)
            .Distinct()
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool HasDescriptiveReportingClauseFor(string narratorText, string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName))
        {
            return false;
        }

        var escapedName = Regex.Escape(normalizedName);
        var pattern = $@"(?:^|[。！？!?]\s*){escapedName}(?:轉|回|抬|低|高|冷笑|怒|皺|瞪|盯|看|望|開口|對|向|朝)[^。！？!?「」『』“”]{{0,72}}?(?:{DescriptiveReportingVerbPattern})(?:[。！？!?]|$)";
        return Regex.IsMatch(narratorText, pattern, RegexOptions.CultureInvariant);
    }

    private static Guid? FindFirstPersonDialogueSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        if (request.PointOfViewCharacterId is not Guid povCharacterId)
        {
            return null;
        }

        var narratorTextAfter = LeadingSentence(FindNarratorTextAfter(request.Segments, dialogueSegment.Index));
        return narratorTextAfter is not null && FirstPersonDialogueCue.IsMatch(narratorTextAfter)
            ? povCharacterId
            : null;
    }

    /// <summary>
    /// A later quoted response may be introduced by an identifying reaction rather than a repeated
    /// name: 「…死神…紅紅的眼睛瞪了我一眼，冷笑著，『…』」. Only reuse a known identity when exactly
    /// one appears before that reaction inside the immediate connector; this is a suggestion, not a
    /// confirmed reporting clause.
    /// </summary>
    private static Guid? FindReactionContinuationSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        var narratorText = FindNarratorTextBefore(request.Segments, dialogueSegment.Index);
        var reaction = narratorText is null ? Match.Empty : ReactionCue.Match(narratorText);
        if (narratorText is null || !reaction.Success)
        {
            return null;
        }

        // A contiguous narrator segment can hold an entire scene. Restrict the weak reaction
        // inference to the reaction sentence plus its immediately preceding sentence: a name from
        // much earlier in that segment is no longer evidence that this later response is theirs.
        var reactionContext = SentencesEndingAtReaction(narratorText, reaction.Index);
        var reactionInContext = ReactionCue.Match(reactionContext);
        var candidates = request.KnownCharacters
            .Where(character => Names(character).Any(name =>
                !string.IsNullOrEmpty(name)
                && reactionContext.IndexOf(name, StringComparison.Ordinal) is var index
                && index >= 0
                && index < reactionInContext.Index))
            .Select(character => character.CharacterId)
            .Distinct()
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static (Guid CharacterId, string ReasonCode)? FindReportingClauseSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        var neighborTexts = FindReportingClauseScopes(request.Segments, dialogueSegment.Index);
        if (neighborTexts.Count == 0)
        {
            return null;
        }

        Guid? candidate = null;
        var reasonCode = "reporting_clause_exact_alias";
        foreach (var character in request.KnownCharacters)
        {
            foreach (var name in Names(character))
            {
                if (!neighborTexts.Any(text => HasReportingClauseFor(text, name)))
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
            && neighborTexts.Any(text => HasReportingClauseFor(text, FirstPersonPronoun)))
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
    /// Weaker fallback for when no reporting verb is present: a known character is the only one
    /// mentioned in the narrator sentence immediately touching the dialogue, regardless of what
    /// they're doing there. Two different known names in that boundary sentence means the text isn't
    /// unambiguously about one person — do not guess.
    /// </summary>
    private static (Guid CharacterId, string ReasonCode)? FindSoleNamedActorSpeaker(
        SpeakerAttributionRequest request,
        SpeechSegmentAttributionInput dialogueSegment)
    {
        var neighborTexts = FindReportingClauseScopes(request.Segments, dialogueSegment.Index);
        if (neighborTexts.Count == 0)
        {
            return null;
        }

        // Do not let the weak fallback override a prior explicit-but-ambiguous first-person
        // reporting clause (for example 「我說：鮑伯說：」). A bare narrator mention is never enough
        // to choose between the configured POV and the named character.
        if (request.PointOfViewCharacterId is not null
            && neighborTexts.Any(text => HasReportingClauseFor(text, FirstPersonPronoun)))
        {
            return null;
        }

        Guid? candidate = null;
        var reasonCode = "narrator_sole_named_actor";
        foreach (var character in request.KnownCharacters)
        {
            if (!Names(character).Any(name => neighborTexts.Any(text =>
                !string.IsNullOrEmpty(name) && text.Contains(name, StringComparison.Ordinal))))
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

        // A first-person narrator is present throughout ordinary narration, so a bare 「我」 in an
        // adjacent connector is not evidence that either touching quote belongs to the POV. Unlike
        // a named actor, treating it as the sole actor would turn entire dialogue scenes into
        // narrator speech. First-person assignment requires an explicit reporting/thought cue and
        // is handled above by FindReportingClauseSpeaker or FindFirstPersonDialogueSpeaker.
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


    /// <summary>
    /// A narrator segment can contain several sentences. Only the opening sentence after a quote
    /// or the closing sentence before it may introduce that specific line, so a prior speaker tag
    /// cannot spill across a second speaker's response in the same segment.
    /// </summary>
    private static IReadOnlyList<string> FindReportingClauseScopes(
        IReadOnlyList<SpeechSegmentAttributionInput> segments,
        int dialogueIndex)
    {
        var scopes = new List<string>(2);
        var after = LeadingSentence(FindNarratorTextAfter(segments, dialogueIndex));
        if (!string.IsNullOrWhiteSpace(after))
        {
            scopes.Add(after);
        }

        var before = TrailingSentence(FindNarratorTextBefore(segments, dialogueIndex));
        if (!string.IsNullOrWhiteSpace(before))
        {
            scopes.Add(before);
        }

        return scopes;
    }

    private static string? FindNarratorTextBefore(
        IReadOnlyList<SpeechSegmentAttributionInput> segments,
        int dialogueIndex)
    {
        var position = FindSegmentPosition(segments, dialogueIndex);
        return position > 0 && segments[position - 1].Kind == SpeechSegmentKind.Narrator
            ? segments[position - 1].Text
            : null;
    }

    private static string? FindNarratorTextAfter(
        IReadOnlyList<SpeechSegmentAttributionInput> segments,
        int dialogueIndex)
    {
        var position = FindSegmentPosition(segments, dialogueIndex);
        return position >= 0 && position < segments.Count - 1 && segments[position + 1].Kind == SpeechSegmentKind.Narrator
            ? segments[position + 1].Text
            : null;
    }

    private static int FindSegmentPosition(
        IReadOnlyList<SpeechSegmentAttributionInput> segments,
        int dialogueIndex)
    {
        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index].Index == dialogueIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private static string? LeadingSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var terminal = text.IndexOfAny(['。', '！', '？', '!', '?']);
        return terminal < 0 ? text : text[..(terminal + 1)];
    }

    private static string? TrailingSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var terminal = text.LastIndexOfAny(['。', '！', '？', '!', '?']);
        if (terminal < 0)
        {
            return text;
        }

        // A connector may end with its own sentence terminator ("艾莉絲頓了頓。")
        // immediately before the next quote. In that case the final complete sentence—not the
        // empty text after its terminator—is the relevant scope.
        if (terminal == text.Length - 1)
        {
            var previousTerminal = terminal > 0
                ? text.LastIndexOfAny(['。', '！', '？', '!', '?'], terminal - 1)
                : -1;
            return text[(previousTerminal + 1)..];
        }

        return text[(terminal + 1)..];
    }

    private static string SentencesEndingAtReaction(string text, int reactionIndex)
    {
        var currentSentenceStart = reactionIndex > 0
            ? text.LastIndexOfAny(['。', '！', '？', '!', '?'], reactionIndex - 1) + 1
            : 0;
        var previousSentenceStart = currentSentenceStart > 1
            ? text.LastIndexOfAny(['。', '！', '？', '!', '?'], currentSentenceStart - 2) + 1
            : 0;
        return text[previousSentenceStart..];
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
