using System.Text.RegularExpressions;
using StoryVoice.Domain.Books;

namespace StoryVoice.Application.Narrations.SpeechPlanning;

public sealed record CharacterCandidate(
    string Name,
    int OccurrenceCount,
    string SampleChapterTitle,
    string? SampleDialogue);

/// <summary>
/// Suggests character names for a human to confirm — never assigns anyone as a speaker. It primarily
/// reuses the same reporting-clause shape as <c>RuleBasedSpeakerAttributionProvider</c> ("XX說："),
/// but can also use one explicit title-bearing actor in the narrator bridge between two dialogue
/// lines ("…」幸運同學把椅子轉過來，「…"). It points those narrow signals at any name-like token
/// instead of only already-registered characters, so a newly imported book can surface "who shows
/// up here" before a single cast member has been typed in.
///
/// Only scans the short connector text immediately touching a dialogue quote — never a whole
/// narrator paragraph — and within that connector keeps only the match closest to the quote
/// boundary. Chinese has no word spacing, and reporting-verb characters ("說"/"道"/"答"/"問") are
/// also common substrings of everyday vocabulary unrelated to speech ("不知道", "應該說", "回答不
/// 出"); scanning whole paragraphs previously surfaced those as "candidates". Anchoring to the
/// actual reporting-clause position (right before/after a quote, closest match wins) is what makes
/// this precise enough to be useful instead of dominated by ordinary prose.
///
/// A name must also recur at least <see cref="MinimumOccurrenceCount"/> times: a real character
/// gets named next to their dialogue repeatedly, while a stray function-word mismatch from this
/// heuristic almost never lands on the exact same text twice — this catches most remaining noise
/// without needing to enumerate every possible non-name phrase.
///
/// Still deliberately imprecise in known, acceptable ways: a name must be 2+ Han characters (this
/// alone screens out nearly every third-person pronoun, which in Chinese is a single character) or
/// a capitalized Latin word, and a title used as a stand-in name ("老師說：") will still surface —
/// the caller is expected to let a person pick real characters out of the ranked list, not trust it
/// blindly. Characters who are only ever addressed by pronoun or through the POV "我" won't surface
/// at all — this is a hint, not an exhaustive cast list.
/// </summary>
public static class CharacterCandidateExtractor
{
    private const int MaximumCandidates = 30;
    private const int MaximumSampleLength = 120;

    // A real character gets named next to their dialogue repeatedly across a book; a stray
    // function-word mismatch from the regex heuristic almost never recurs at the exact same
    // boundary position twice. Requiring at least two occurrences filters out most one-off noise
    // without needing to enumerate every possible non-name phrase.
    private const int MinimumOccurrenceCount = 2;

    // Pronouns and particles that must never be swallowed into a captured name: without this, a
    // greedy/lazy CJK run can't tell "小華" (name) + "問道" (verb) apart from "小華問" + "道", and a
    // filler character next to a real pronoun ("她又") would otherwise look like a plausible 2-char
    // name on its own.
    private const string BlockedNameCharacters =
        "他她它牠祂我你妳咱誰彼此又也還都就才卻便再仍皆已曾該的了嗎呢吧啊喔哦" +
        "不一是要想會能得在著過之對把被讓使給跟和但而且將由如若雖然於向往從自剛";

    private static readonly string BlockedCharacterAlternation = string.Join(
        '|',
        BlockedNameCharacters.Select(character => Regex.Escape(character.ToString())));
    private static readonly string CjkNameCharacter =
        $"(?:(?!{BlockedCharacterAlternation})\\p{{IsCJKUnifiedIdeographs}})";

    // Multi-character function words that can still sit directly next to a reporting verb as
    // ordinary grammar ("說什麼", "不知道", "這樣說") without either character alone being a pronoun
    // caught by <see cref="BlockedNameCharacters"/>. Rejects a candidate if it CONTAINS any of these
    // anywhere, not just an exact match — a lazy quantifier without a hard boundary to stop at can
    // glue one of these onto a real name ("喵喵這樣"), and a mandatory-verb match can still truncate
    // a longer function word to its first two characters ("為什麼" → "為什").
    private static readonly string[] BlockedNameWords =
    [
        "什麼", "怎麼", "怎樣", "這樣", "那樣", "這麼", "那麼",
        "為何", "如何", "為什", "多少", "哪裡", "哪兒", "何時", "何事", "何人",
        "不是", "不過", "其實", "另外", "應該", "不知", "些什", "知道", "不出",
        "可是", "但是", "不用", "不會", "不能", "不要", "回來", "這個", "那個",
        "出來", "起來", "過來", "進來", "下去", "上去", "回去", "過去", "出去",
        "繼續",
    ];

    // A dialogue bridge can identify an otherwise-unregistered speaker without a reporting verb:
    // 「…」幸運同學把椅子轉過來，「…」. It treats explicit forms of address as actor references,
    // but only returns one that has a compact name-like modifier. This lets a bare 「老師」 make a
    // bridge ambiguous without ever turning that generic title into a candidate, and never accepts
    // arbitrary 2–4-character CJK runs that could turn ordinary verbs such as 「繼續」 into people.
    private static readonly string[] CharacterTitleSuffixes =
    [
        "同學", "學長", "學姐", "老師", "先生", "小姐", "太太", "教授", "醫師", "醫生",
        "主任", "老闆", "師傅", "大人", "同事",
    ];
    private static readonly string CharacterTitleSuffixPattern = string.Join(
        '|',
        CharacterTitleSuffixes.OrderByDescending(title => title.Length).Select(Regex.Escape));

    private const string LatinName = @"[A-Z][A-Za-z]{1,19}";
    private static readonly string VerbPattern = string.Join(
        '|',
        ReportingVerbCatalog.Verbs.OrderByDescending(verb => verb.Length).Select(Regex.Escape));

    // Real Chinese personal names/aliases in practice are almost always 2-3 Han characters; capping
    // the run at 4 (rather than a more permissive 6) means an unrelated run that happens to reach a
    // verb or a boundary late — several words strung together — more often fails to match at all
    // instead of getting captured whole as one long garbled "name".
    private const int MaximumNameLength = 4;

    // The suffix is the evidence: without it this must never accept a free-form CJK run, because
    // prose verbs (for example 「繼續」) look just as name-like to a regular expression. The speaker
    // candidate itself must begin the bridge and have a 2–4-character modifier: this accepts
    // 「小明同學」 and 「幸運同學」 but deliberately leaves weak forms such as 「王老師」 or bare
    // 「同學」 unknown. We still enumerate the rest of the bridge below, so another named actor
    // (「幸運同學把紙遞給小美同學」) makes the attribution ambiguous instead of silently becoming
    // evidence for the first one. Missing a weak signal is safer than creating a generic person.
    private static readonly Regex LeadingTitleBearingActor = new(
        $@"^\s*(?<name>{CjkNameCharacter}{{2,{MaximumNameLength}}}?(?:{CharacterTitleSuffixPattern}))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TitleBearingActor = new(
        $@"(?<name>{CjkNameCharacter}{{2,{MaximumNameLength}}}?(?:{CharacterTitleSuffixPattern}))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CharacterTitleMention = new(
        $@"(?:{CharacterTitleSuffixPattern})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Lazy: prefer the shortest CJK run that still lets a verb match immediately follow, so
    // "小華問道" resolves as name="小華" + verb="問道" instead of name="小華問" + verb="道". Correct
    // specifically because a real verb always follows here — the lazy attempt can only succeed once
    // it stops short enough for the mandatory verb match right after it to fit.
    private static readonly string CjkNameBeforeVerb = $"{CjkNameCharacter}{{2,{MaximumNameLength}}}?";
    private static readonly string NameBeforeVerbPattern = $"(?:{CjkNameBeforeVerb}|{LatinName})";
    private static readonly Regex NameBeforeVerb = new(
        $@"(?<name>{NameBeforeVerbPattern})[，,：:、]{{0,2}}(?:{VerbPattern})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Also lazy, and deliberately NOT extended to consume a whole run the way the before-verb side
    // is: nothing mandatory follows the name here, so an unbounded/greedy run has nothing to anchor
    // its true end against and tends to swallow several unrelated words into one long "name"
    // ("什麼時候到達" instead of stopping). A short, sometimes-truncated 2-character guess ("為什"
    // instead of "為什麼") is the safer failure mode of the two.
    private static readonly string CjkNameAfterVerb = $"{CjkNameCharacter}{{2,{MaximumNameLength}}}?";
    private static readonly string NameAfterVerbPattern = $"(?:{CjkNameAfterVerb}|{LatinName})";
    private static readonly Regex NameAfterVerb = new(
        $@"(?:{VerbPattern})[的是]{{0,2}}(?<name>{NameAfterVerbPattern})",
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
                var occurrenceCount = 1;
                if (name is null && precedesDialogue && followsDialogue)
                {
                    // One clearly identified actor in the bridge is evidence for both the dialogue
                    // line before it and the line after it. Count the two actual utterances, not
                    // merely the one narrator segment that links them.
                    name = FindSoleTitleBearingActor(narratorText);
                    occurrenceCount = name is null ? 0 : 2;
                }

                if (name is null)
                {
                    continue;
                }

                counts[name] = counts.GetValueOrDefault(name) + occurrenceCount;
                if (!samples.ContainsKey(name))
                {
                    samples[name] = (chapter.Title, FindAdjacentDialogue(body, plan.BodySegments, position));
                }
            }
        }

        return counts
            .Where(entry => entry.Value >= MinimumOccurrenceCount)
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
            .Where(match => match.Success
                && !BlockedNameWords.Any(word => match.Groups["name"].Value.Contains(word, StringComparison.Ordinal)))
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

    private static string? FindSoleTitleBearingActor(string narratorText)
    {
        // The potential speaker has to start the bridge. A title-bearing person mentioned only
        // later in a narration clause is not reliable evidence that either surrounding quote is
        // theirs.
        var leadingActor = LeadingTitleBearingActor.Match(narratorText);
        if (!leadingActor.Success)
        {
            return null;
        }

        var leadingName = leadingActor.Groups["name"].Value;
        if (BlockedNameWords.Any(word => leadingName.Contains(word, StringComparison.Ordinal)))
        {
            return null;
        }

        var namedActors = TitleBearingActor.Matches(narratorText)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !BlockedNameWords.Any(word => name.Contains(word, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (namedActors.Length != 1 || !StringComparer.Ordinal.Equals(namedActors[0], leadingName))
        {
            return null;
        }

        // Any additional title occurrence makes this bridge ambiguous. Count occurrences rather
        // than distinct title text: 「幸運同學…另一位同學」 still names two people even though the
        // suffix is the same. Repeatedly naming the same actor is conservatively left unknown too;
        // missing a weak suggestion is safer than assigning the surrounding dialogue to the wrong
        // person.
        var titleMentionCount = CharacterTitleMention.Matches(narratorText).Count;
        return titleMentionCount == 1 ? leadingName : null;
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
