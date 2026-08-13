using System.Text.RegularExpressions;
using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Narrations.SpeechPlanning;

public sealed record CharacterCandidate(
    string Name,
    int OccurrenceCount,
    string SampleChapterTitle,
    string? SampleDialogue);

/// <summary>
/// Suggests character names for a human to confirm — never assigns anyone as a speaker. Reuses the
/// same reporting-clause shape as <c>RuleBasedSpeakerAttributionProvider</c> ("XX說："), but points
/// it at any name-like token instead of only already-registered characters, so a newly imported book
/// can surface "who shows up here" before a single cast member has been typed in.
///
/// Only scans the short connector text immediately touching a dialogue quote — never a whole
/// narrator paragraph — and within that connector keeps only the match closest to the quote
/// boundary. Chinese has no word spacing, and reporting-verb characters ("說"/"道"/"答"/"問") are
/// also common substrings of everyday vocabulary unrelated to speech ("不知道", "應該說", "回答不
/// 出"); scanning whole paragraphs previously surfaced those as "candidates". Anchoring to the
/// actual reporting-clause position (right before/after a quote, closest match wins) is what makes
/// this precise enough to be useful instead of dominated by ordinary prose.
///
/// Still deliberately noisy in known, acceptable ways: a name must be 2+ Han characters (this alone
/// screens out nearly every third-person pronoun, which in Chinese is a single character) or a
/// capitalized Latin word, and a title used as a stand-in name ("老師說：") will still surface — the
/// caller is expected to let a person pick real characters out of the ranked list, not trust it
/// blindly.
/// </summary>
public static class CharacterCandidateExtractor
{
    private const int MaximumCandidates = 30;
    private const int MaximumSampleLength = 120;

    // Pronouns and particles that must never be swallowed into a captured name: without this, a
    // greedy/lazy CJK run can't tell "小華" (name) + "問道" (verb) apart from "小華問" + "道", and a
    // filler character next to a real pronoun ("她又") would otherwise look like a plausible 2-char
    // name on its own.
    private const string BlockedNameCharacters =
        "他她它牠祂我你妳咱誰彼此又也還都就才卻便再仍皆已曾的了嗎呢吧啊喔哦";

    private static readonly string BlockedCharacterAlternation = string.Join(
        '|',
        BlockedNameCharacters.Select(character => Regex.Escape(character.ToString())));
    private static readonly string CjkNameCharacter =
        $"(?:(?!{BlockedCharacterAlternation})\\p{{IsCJKUnifiedIdeographs}})";

    // Lazy: prefer the shortest CJK run that still lets a verb match immediately follow, so
    // "小華問道" resolves as name="小華" + verb="問道" instead of name="小華問" + verb="道".
    private static readonly string CjkName = $"{CjkNameCharacter}{{2,6}}?";
    private const string LatinName = @"[A-Z][A-Za-z]{1,19}";
    private static readonly string NamePattern = $"(?:{CjkName}|{LatinName})";
    private static readonly string VerbPattern = string.Join(
        '|',
        ReportingVerbCatalog.Verbs.OrderByDescending(verb => verb.Length).Select(Regex.Escape));

    private static readonly Regex NameBeforeVerb = new(
        $@"(?<name>{NamePattern})[，,：:、]{{0,2}}(?:{VerbPattern})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NameAfterVerb = new(
        $@"(?:{VerbPattern})[的是]{{0,2}}(?<name>{NamePattern})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<CharacterCandidate> Extract(IEnumerable<Chapter> chapters)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = new Dictionary<string, (string ChapterTitle, string? Dialogue)>(StringComparer.Ordinal);

        foreach (var chapter in chapters.OrderBy(chapter => chapter.SortOrder))
        {
            var body = chapter.OriginalText;
            var plan = new ChineseSpeechSegmenter().Segment(chapter.Title, body);
            for (var position = 0; position < plan.BodySegments.Count; position++)
            {
                var segment = plan.BodySegments[position];
                if (segment.Kind != SpeechSegmentKind.Narrator)
                {
                    continue;
                }

                var precedesDialogue = position < plan.BodySegments.Count - 1
                    && plan.BodySegments[position + 1].Kind == SpeechSegmentKind.Dialogue;
                var followsDialogue = position > 0
                    && plan.BodySegments[position - 1].Kind == SpeechSegmentKind.Dialogue;
                if (!precedesDialogue && !followsDialogue)
                {
                    // Ordinary narrative prose, not touching any dialogue quote — never a reporting
                    // clause, and full of incidental "說"/"道"/"答" substrings if scanned anyway.
                    continue;
                }

                var narratorText = body.Substring(segment.StartOffset, segment.Length);
                var name = precedesDialogue
                    ? FindClosestName(narratorText, preferLast: true)
                    : FindClosestName(narratorText, preferLast: false);
                if (name is null)
                {
                    continue;
                }

                counts[name] = counts.GetValueOrDefault(name) + 1;
                if (!samples.ContainsKey(name))
                {
                    samples[name] = (chapter.Title, FindAdjacentDialogue(body, plan.BodySegments, position));
                }
            }
        }

        return counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(MaximumCandidates)
            .Select(entry =>
            {
                var (chapterTitle, dialogue) = samples[entry.Key];
                return new CharacterCandidate(entry.Key, entry.Value, chapterTitle, dialogue);
            })
            .ToArray();
    }

    /// <summary>
    /// Among every name+verb match in this (short, dialogue-adjacent) connector text, keeps only
    /// the one closest to the quote boundary: the last match when the segment precedes the dialogue
    /// ("…剛剛才說：「…"), the first match when it follows ("…」他說，然後…"). A connector can contain
    /// an earlier, unrelated verb-shaped phrase; only the boundary-nearest one is the actual
    /// reporting-clause tag for this particular line of dialogue.
    /// </summary>
    private static string? FindClosestName(string narratorText, bool preferLast)
    {
        var matches = NameBeforeVerb.Matches(narratorText)
            .Concat(NameAfterVerb.Matches(narratorText))
            .Where(match => match.Success)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var chosen = preferLast
            ? matches.OrderByDescending(match => match.Index + match.Length).First()
            : matches.OrderBy(match => match.Index).First();
        return chosen.Groups["name"].Value;
    }

    private static string? FindAdjacentDialogue(
        string body,
        IReadOnlyList<SpeechSegment> segments,
        int narratorPosition)
    {
        var after = narratorPosition < segments.Count - 1 ? segments[narratorPosition + 1] : null;
        if (after?.Kind == SpeechSegmentKind.Dialogue)
        {
            return Truncate(body.Substring(after.StartOffset, after.Length));
        }

        var before = narratorPosition > 0 ? segments[narratorPosition - 1] : null;
        if (before?.Kind == SpeechSegmentKind.Dialogue)
        {
            return Truncate(body.Substring(before.StartOffset, before.Length));
        }

        return null;
    }

    private static string Truncate(string text) =>
        text.Length <= MaximumSampleLength ? text : string.Concat(text.AsSpan(0, MaximumSampleLength), "…");
}
