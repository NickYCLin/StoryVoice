using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StoryVoice.Application.Narrations.SpeechPlanning;

public sealed class ChineseSpeechSegmenter
{
    // The source hash doubles as the speech-plan regeneration fingerprint. Bump its discriminator
    // whenever contextual attribution semantics change, so existing drafts are rebuilt rather than
    // silently retaining assignments generated under an older rule set.
    public const string AlgorithmVersion = "zh-quote-v3-semantic-kinds";

    private const int QuoteContextLength = 180;
    private const int DirectCueContextLength = 96;
    private static readonly Regex ExplicitDialogueBeforeQuote = new(
        @"(?:(?:說|問|答|喊|吼|叫|回答|回應|開口|朗讀|讀出|念出|吟唱)(?:著|道)?|念給[^。！？!?『』]{0,24}聽|(?<!寫)道)\s*[，,：:]?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ExplicitDialogueAfterQuote = new(
        @"^\s*(?:[^。！？!?]{0,48})?(?:說話|說道|說的|問道|回答|喊道|朗讀|讀出|念出|念給[^。！？!?『』]{0,24}聽|聲音|語氣|電話|手機的那端|電話的那端|對方|聽見)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PhoneOrCommunicationContext = new(
        @"(?:放在耳朵|接起(?:手機|電話)?|拿起(?:手機|電話)?|手機的那端|電話的那端|電話另?一頭|對方(?:傳來|說|回答)|掛掉(?:手機|電話)?|通訊器|話筒|耳機).{0,32}(?:聲音|語氣|說|回答|掛斷)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InnerThoughtBeforeQuote = new(
        @"(?:我|自己|心裡|心中|腦中|腦海)(?:[^。！？!?『』]{0,32})?(?:心想|暗想|默念|默讀|想著|想到|想)\s*[，,：:]?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DocumentOrReadingBeforeQuote = new(
        @"(?:叫做|名為|題為|寫道|標題(?:是|為|叫)?|書名(?:是|為|叫)?|校名(?:是|為|叫)?|名稱(?:是|為|叫)?)\s*[，,：:]?\s*$"
        + @"|(?:封口|封面|紙上|上面|資料|通知|信件|表格|螢幕)(?:[^。！？!?『』]{0,48})?(?:寫(?:著|了|的)?|印(?:著|了|的)?|顯示|標示)(?:[^。！？!?『』]{0,24})?(?:[。！？!?][^。！？!?『』]{0,64})?[。！？!?….\s]*$"
        + @"|(?:寫(?:著|了|的)?|印(?:著|了|的)?|顯示|標示)(?:[^。！？!?『』]{0,24})?(?:大字|小字|字樣|內容)(?:[^。！？!?『』]{0,16})?$"
        + @"|(?:消息|結果)(?:[^。！？!?『』]{0,36})?(?:一件事情|顯示(?:的)?(?:內容|結果)?|內容(?:是|為))(?:[。！？!?….\s]*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DocumentOrReadingAfterQuote = new(
        @"^\s*(?:的)?(?:這行字|這幾個字|字樣|標題|書名|校名|名稱|內容|資料|通知|恐嚇信)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> SoundEffects = new(StringComparer.Ordinal)
    {
        "啪", "砰", "碰", "咚", "轟", "哐", "鏘", "叮", "鈴", "喀嚓", "啪喳"
    };

    public ChapterSpeechPlan Segment(string? chapterTitle, string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var title = chapterTitle ?? string.Empty;
        var segments = SegmentBody(body);
        var status = segments.Any(segment => segment.Status == SpeechSegmentStatus.NeedsReview)
            ? SpeechPlanStatus.NeedsReview
            : SpeechPlanStatus.Ready;
        var titleTurn = title.Length == 0
            ? null
            : new ChapterTitleNarratorTurn(0, title.Length);

        return new ChapterSpeechPlan(
            ComputeSourceHash(title, body),
            AlgorithmVersion,
            status,
            titleTurn,
            segments);
    }

    private static IReadOnlyList<SpeechSegment> SegmentBody(string body)
    {
        var segments = new List<SpeechSegment>();
        var expectedClosingQuotes = new Stack<char>();
        var expectedClosingQuoteCounts = new Dictionary<char, int>(3);
        var nextNarratorOffset = 0;
        var dialogueOffset = -1;
        var narratorNeedsReview = false;
        var dialogueNeedsReview = false;

        for (var offset = 0; offset < body.Length; offset++)
        {
            var current = body[offset];
            if (TryGetClosingQuote(current, out var expectedClosingQuote))
            {
                if (expectedClosingQuotes.Count == 0)
                {
                    AddSegment(
                        segments,
                        SpeechSegmentKind.Narrator,
                        nextNarratorOffset,
                        offset - nextNarratorOffset,
                        narratorNeedsReview);
                    dialogueOffset = offset;
                    dialogueNeedsReview = false;
                    narratorNeedsReview = false;
                }

                PushExpectedClosingQuote(
                    expectedClosingQuotes,
                    expectedClosingQuoteCounts,
                    expectedClosingQuote);
                continue;
            }

            if (!IsClosingQuote(current))
            {
                continue;
            }

            if (expectedClosingQuotes.Count == 0)
            {
                narratorNeedsReview = true;
                continue;
            }

            if (expectedClosingQuotes.Peek() == current)
            {
                PopExpectedClosingQuote(expectedClosingQuotes, expectedClosingQuoteCounts);
            }
            else
            {
                dialogueNeedsReview = true;
                RecoverAtMismatchedClosingQuote(
                    expectedClosingQuotes,
                    expectedClosingQuoteCounts,
                    current);
            }

            if (expectedClosingQuotes.Count != 0)
            {
                continue;
            }

            AddSegment(
                segments,
                ClassifyCompletedQuote(body, dialogueOffset, offset + 1, dialogueNeedsReview),
                dialogueOffset,
                offset + 1 - dialogueOffset,
                dialogueNeedsReview);
            nextNarratorOffset = offset + 1;
            dialogueOffset = -1;
            dialogueNeedsReview = false;
        }

        if (expectedClosingQuotes.Count > 0)
        {
            AddSegment(
                segments,
                SpeechSegmentKind.Dialogue,
                dialogueOffset,
                body.Length - dialogueOffset,
                needsReview: true);
        }
        else
        {
            AddSegment(
                segments,
                SpeechSegmentKind.Narrator,
                nextNarratorOffset,
                body.Length - nextNarratorOffset,
                narratorNeedsReview);
        }

        EnsureExactCoverage(body, segments);
        return segments.AsReadOnly();
    }

    private static void PushExpectedClosingQuote(
        Stack<char> expectedClosingQuotes,
        Dictionary<char, int> expectedClosingQuoteCounts,
        char expectedClosingQuote)
    {
        expectedClosingQuotes.Push(expectedClosingQuote);
        expectedClosingQuoteCounts.TryGetValue(expectedClosingQuote, out var count);
        expectedClosingQuoteCounts[expectedClosingQuote] = count + 1;
    }

    private static char PopExpectedClosingQuote(
        Stack<char> expectedClosingQuotes,
        Dictionary<char, int> expectedClosingQuoteCounts)
    {
        var removed = expectedClosingQuotes.Pop();
        var remainingCount = expectedClosingQuoteCounts[removed] - 1;
        if (remainingCount == 0)
        {
            expectedClosingQuoteCounts.Remove(removed);
        }
        else
        {
            expectedClosingQuoteCounts[removed] = remainingCount;
        }

        return removed;
    }

    private static void RecoverAtMismatchedClosingQuote(
        Stack<char> expectedClosingQuotes,
        Dictionary<char, int> expectedClosingQuoteCounts,
        char current)
    {
        if (!expectedClosingQuoteCounts.ContainsKey(current))
        {
            return;
        }

        char removed;
        do
        {
            removed = PopExpectedClosingQuote(expectedClosingQuotes, expectedClosingQuoteCounts);
        }
        while (removed != current);
    }

    private static void AddSegment(
        List<SpeechSegment> segments,
        SpeechSegmentKind kind,
        int startOffset,
        int length,
        bool needsReview)
    {
        if (length == 0)
        {
            return;
        }

        segments.Add(new SpeechSegment(
            segments.Count,
            kind,
            startOffset,
            length,
            needsReview ? SpeechSegmentStatus.NeedsReview : SpeechSegmentStatus.Ready));
    }

    private static void EnsureExactCoverage(string body, IReadOnlyList<SpeechSegment> segments)
    {
        var expectedOffset = 0;
        foreach (var segment in segments)
        {
            if (segment.StartOffset != expectedOffset || segment.Length <= 0)
            {
                throw new InvalidOperationException("Speech segmentation did not preserve exact source coverage.");
            }

            expectedOffset = segment.EndOffset;
        }

        if (expectedOffset != body.Length)
        {
            throw new InvalidOperationException("Speech segmentation did not preserve exact source coverage.");
        }
    }

    private static bool TryGetClosingQuote(char current, out char closingQuote)
    {
        closingQuote = current switch
        {
            '「' => '」',
            '『' => '』',
            '“' => '”',
            _ => default
        };
        return closingQuote != default;
    }

    private static bool IsClosingQuote(char current) => current is '」' or '』' or '”';

    private static SpeechSegmentKind ClassifyCompletedQuote(
        string body,
        int startOffset,
        int endOffset,
        bool needsReview)
    {
        if (needsReview || body[startOffset] != '『')
        {
            return SpeechSegmentKind.Dialogue;
        }

        var beforeStart = Math.Max(0, startOffset - QuoteContextLength);
        var afterLength = Math.Min(QuoteContextLength, body.Length - endOffset);
        var before = body.Substring(beforeStart, startOffset - beforeStart);
        var after = body.Substring(endOffset, afterLength);
        var directBefore = before.Length <= DirectCueContextLength
            ? before
            : before[^DirectCueContextLength..];
        var directAfter = after.Length <= DirectCueContextLength
            ? after
            : after[..DirectCueContextLength];
        var quotedText = body[(startOffset + 1)..(endOffset - 1)].Trim();

        if (HasExplicitDialogueContext(directBefore, directAfter))
        {
            return SpeechSegmentKind.Dialogue;
        }

        if (IsSoundEffect(quotedText, directBefore, directAfter)
            || IsInlineTypographicEmphasis(body, startOffset, endOffset, quotedText))
        {
            return SpeechSegmentKind.Narrator;
        }

        if (InnerThoughtBeforeQuote.IsMatch(before)
            || DocumentOrReadingBeforeQuote.IsMatch(before)
            || DocumentOrReadingAfterQuote.IsMatch(after))
        {
            return SpeechSegmentKind.InnerMonologue;
        }

        return SpeechSegmentKind.Dialogue;
    }

    private static bool HasExplicitDialogueContext(string before, string after) =>
        ExplicitDialogueBeforeQuote.IsMatch(before)
        || ExplicitDialogueAfterQuote.IsMatch(after)
        || PhoneOrCommunicationContext.IsMatch(before)
        || PhoneOrCommunicationContext.IsMatch(after);

    private static bool IsSoundEffect(string quotedText, string before, string after)
    {
        if (!SoundEffects.Contains(quotedText))
        {
            return false;
        }

        return before.Contains("聲", StringComparison.Ordinal)
            || before.Contains("響", StringComparison.Ordinal)
            || after.Contains("聲", StringComparison.Ordinal)
            || after.Contains("響", StringComparison.Ordinal);
    }

    private static bool IsInlineTypographicEmphasis(
        string body,
        int startOffset,
        int endOffset,
        string quotedText) =>
        quotedText.Length is > 0 and <= 8
        && startOffset > 0
        && endOffset < body.Length
        && char.IsLetterOrDigit(body[startOffset - 1])
        && char.IsLetterOrDigit(body[endOffset]);

    private static string ComputeSourceHash(string title, string body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("StoryVoice.ChapterSpeechSource.v3-semantic-kinds\0"u8);
        AppendUtf16(hash, title);
        AppendUtf16(hash, body);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendUtf16(IncrementalHash hash, string value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);

        Span<byte> buffer = stackalloc byte[512];
        for (var offset = 0; offset < value.Length;)
        {
            var characterCount = Math.Min(buffer.Length / 2, value.Length - offset);
            for (var index = 0; index < characterCount; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    buffer.Slice(index * 2, 2),
                    value[offset + index]);
            }

            hash.AppendData(buffer[..(characterCount * 2)]);
            offset += characterCount;
        }
    }
}
