using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StoryVoice.Domain.Narrations;

/// <summary>
/// Immutable, per-chapter confirmed speech plan. Created only from a
/// <see cref="ChapterSpeechPlanDraft"/> whose dialogue segments are all confirmed; once created it
/// can never be edited — a Worker locks a <see cref="NarrationJob"/> onto one of these via
/// <see cref="NarrationJobSpeechPlan"/> and must be able to trust it never changes underneath it.
/// </summary>
public sealed class ConfirmedSpeechPlanRevision
{
    private const string FingerprintSchemaVersion = "storyvoice:confirmed-speech-plan:v1";
    private readonly List<ConfirmedSpeechSegment> _segments = [];

    private ConfirmedSpeechPlanRevision()
    {
    }

    private ConfirmedSpeechPlanRevision(
        Guid ownerId,
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        int revisionNumber,
        string sourceHash,
        DateTimeOffset createdAt,
        string fingerprint)
    {
        Id = Guid.NewGuid();
        OwnerId = ownerId;
        SeriesId = seriesId;
        BookId = bookId;
        ChapterId = chapterId;
        RevisionNumber = revisionNumber;
        SourceHash = sourceHash;
        Fingerprint = fingerprint;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid BookId { get; private set; }
    public Guid ChapterId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string SourceHash { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<ConfirmedSpeechSegment> Segments => _segments.AsReadOnly();

    internal static ConfirmedSpeechPlanRevision Create(
        Guid ownerId,
        Guid seriesId,
        Guid bookId,
        Guid chapterId,
        int revisionNumber,
        string sourceHash,
        DateTimeOffset createdAt,
        IReadOnlyList<SpeechSegmentDraft> draftSegments)
    {
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(bookId, nameof(bookId));
        EnsureId(chapterId, nameof(chapterId));
        if (revisionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "劇本修訂編號必須從 1 開始。");
        }

        ArgumentNullException.ThrowIfNull(draftSegments);
        if (draftSegments.Count == 0)
        {
            throw new ArgumentException("確認的劇本至少要有一個片段。", nameof(draftSegments));
        }

        var fingerprint = ComputeFingerprint(sourceHash, draftSegments);
        var revision = new ConfirmedSpeechPlanRevision(
            ownerId,
            seriesId,
            bookId,
            chapterId,
            revisionNumber,
            sourceHash,
            createdAt,
            fingerprint);

        foreach (var draftSegment in draftSegments.OrderBy(segment => segment.SortOrder))
        {
            revision._segments.Add(
                ConfirmedSpeechSegment.FromConfirmedDraft(ownerId, seriesId, revision.Id, draftSegment));
        }

        return revision;
    }

    private static string ComputeFingerprint(
        string sourceHash,
        IReadOnlyList<SpeechSegmentDraft> segments)
    {
        using var canonical = new MemoryStream();
        WriteField(canonical, FingerprintSchemaVersion);
        WriteField(canonical, sourceHash);
        WriteField(canonical, segments.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var segment in segments.OrderBy(candidate => candidate.SortOrder))
        {
            WriteField(canonical, segment.SortOrder.ToString(CultureInfo.InvariantCulture));
            WriteField(canonical, segment.SourceKind.ToString());
            WriteField(canonical, segment.StartOffset.ToString(CultureInfo.InvariantCulture));
            WriteField(canonical, segment.Length.ToString(CultureInfo.InvariantCulture));
            WriteField(canonical, segment.TextHash);
            WriteField(canonical, segment.Kind.ToString());
            WriteField(canonical, segment.CharacterId?.ToString("N", CultureInfo.InvariantCulture) ?? string.Empty);
            WriteField(canonical, segment.DecisionSource.ToString());
        }

        var hash = SHA256.HashData(canonical.GetBuffer().AsSpan(0, checked((int)canonical.Length)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteField(Stream destination, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, encoded.Length);
        destination.Write(lengthPrefix);
        destination.Write(encoded);
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
