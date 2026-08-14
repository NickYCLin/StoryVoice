using System.Buffers.Binary;
using System.Security.Cryptography;

namespace StoryVoice.Application.Narrations.SpeechPlanning;

public sealed class ChineseSpeechSegmenter
{
    // The source hash doubles as the speech-plan regeneration fingerprint. Bump its discriminator
    // whenever contextual attribution semantics change, so existing drafts are rebuilt rather than
    // silently retaining assignments generated under an older rule set.
    public const string AlgorithmVersion = "zh-quote-v2-context-attribution";

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
                SpeechSegmentKind.Dialogue,
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

    private static string ComputeSourceHash(string title, string body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("StoryVoice.ChapterSpeechSource.v2-context-attribution\0"u8);
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
