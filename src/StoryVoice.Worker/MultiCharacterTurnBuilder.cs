using System.Security.Cryptography;
using System.Text;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Worker;

/// <summary>
/// The chapter text and locked-in confirmed plan the Worker needs for one chapter of a
/// multi-character job, in the order the chapters should be narrated.
/// </summary>
public sealed record ChapterPlanSource(
    int ChapterSortOrder,
    ConfirmedSpeechPlanRevision Revision,
    string ChapterTitle,
    string ChapterBody);

/// <summary>
/// Thrown when a locked speech plan no longer matches its own fingerprint, or a segment's
/// recomputed text hash no longer matches the chapter text it should have come from. The Worker
/// treats this as a permanent failure (<c>speech_plan_integrity_mismatch</c>) — it never falls
/// back to "the latest draft" or guesses at recovery.
/// </summary>
public sealed class SpeechPlanIntegrityException(string reasonCode) : InvalidOperationException(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}

/// <summary>
/// Compiles the immutable, job-locked confirmed speech plans for every chapter in a
/// multi-character narration job into an ordered <see cref="NarrationTurn"/> sequence: resolves
/// each segment's voice from the job's cast revision, merges adjacent same-voice turns within a
/// chapter (never across a chapter boundary), and inserts bounded pauses at speaker and chapter
/// changes. Re-verifies plan/segment integrity against the actual chapter text before it will
/// produce any turns at all.
/// </summary>
public static class MultiCharacterTurnBuilder
{
    public const string IntegrityMismatchReasonCode = "speech_plan_integrity_mismatch";
    private const int MaximumMergedTurnLength = 5_000;

    public static IReadOnlyList<NarrationTurn> BuildTurns(
        NarrationCastRevision castRevision,
        IReadOnlyList<ChapterPlanSource> chapterPlans)
    {
        ArgumentNullException.ThrowIfNull(castRevision);
        ArgumentNullException.ThrowIfNull(chapterPlans);
        if (chapterPlans.Count == 0)
        {
            throw new SpeechPlanIntegrityException(IntegrityMismatchReasonCode);
        }

        var orderedChapters = chapterPlans.OrderBy(plan => plan.ChapterSortOrder).ToArray();
        var turns = new List<NarrationTurn>();
        string? previousVoice = null;
        string? previousRate = null;

        foreach (var chapterPlan in orderedChapters)
        {
            if (!chapterPlan.Revision.VerifyFingerprint())
            {
                throw new SpeechPlanIntegrityException(IntegrityMismatchReasonCode);
            }

            var isFirstSegmentOfChapter = true;
            foreach (var segment in chapterPlan.Revision.Segments.OrderBy(candidate => candidate.SortOrder))
            {
                var sourceText = segment.SourceKind == SpeechSegmentSourceKind.ChapterTitle
                    ? chapterPlan.ChapterTitle
                    : chapterPlan.ChapterBody;
                if (segment.StartOffset < 0
                    || segment.Length <= 0
                    || segment.StartOffset > sourceText.Length
                    || segment.Length > sourceText.Length - segment.StartOffset)
                {
                    throw new SpeechPlanIntegrityException(IntegrityMismatchReasonCode);
                }

                var text = sourceText.Substring(segment.StartOffset, segment.Length);
                if (!string.Equals(HashSlice(text), segment.TextHash, StringComparison.Ordinal))
                {
                    throw new SpeechPlanIntegrityException(IntegrityMismatchReasonCode);
                }

                var (voice, rate) = ResolveVoice(castRevision, segment);
                var sameVoiceAsPrevious = voice == previousVoice && rate == previousRate;
                var canMerge = turns.Count > 0
                    && !isFirstSegmentOfChapter
                    && sameVoiceAsPrevious
                    && turns[^1].Text.Length + text.Length <= MaximumMergedTurnLength;

                if (canMerge)
                {
                    turns[^1] = turns[^1] with { Text = turns[^1].Text + text };
                }
                else
                {
                    var pauseBeforeMs = turns.Count == 0
                        ? 0
                        : isFirstSegmentOfChapter
                            ? castRevision.ChapterPauseMs
                            : sameVoiceAsPrevious ? 0 : castRevision.DefaultSpeakerPauseMs;
                    turns.Add(new NarrationTurn(text, voice, rate, pauseBeforeMs));
                }

                previousVoice = voice;
                previousRate = rate;
                isFirstSegmentOfChapter = false;
            }
        }

        return turns;
    }

    private static (string Voice, string Rate) ResolveVoice(
        NarrationCastRevision castRevision,
        ConfirmedSpeechSegment segment)
    {
        if (segment.Kind == SpeechSegmentTurnKind.Dialogue && segment.CharacterId is Guid characterId)
        {
            var assignment = castRevision.Assignments
                .SingleOrDefault(candidate => candidate.CharacterId == characterId);
            if (assignment is not null)
            {
                return (assignment.Voice, assignment.Rate);
            }
        }

        // Narrator segments, and any dialogue segment a human explicitly confirmed as narrator
        // fallback (or whose assigned character was somehow removed from the cast revision),
        // safely default to the narrator's own fixed voice rather than guessing.
        return (castRevision.NarratorVoice, castRevision.NarratorRate);
    }

    private static string HashSlice(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
