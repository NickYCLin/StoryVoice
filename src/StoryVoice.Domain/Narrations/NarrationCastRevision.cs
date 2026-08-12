using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StoryVoice.Domain.Series;

namespace StoryVoice.Domain.Narrations;

public enum NarrationCastRevisionStatus
{
    Draft,
    Active,
    Historical
}

public sealed class NarrationCastRevision
{
    private const string FingerprintSchemaVersion = "storyvoice:narration-cast-revision:v1";
    private readonly List<NarrationCastAssignment> _assignments = [];

    private NarrationCastRevision()
    {
    }

    private NarrationCastRevision(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        int revisionNumber,
        string narratorProvider,
        string narratorProviderVersion,
        string narratorVoice,
        string narratorRate,
        string narratorPitch,
        string narratorVolume,
        int defaultSpeakerPauseMs,
        int chapterPauseMs,
        string compositionVersion,
        string ffmpegProfile,
        DateTimeOffset createdAt,
        IReadOnlyCollection<NarrationCastAssignment> assignments,
        string fingerprint)
    {
        Id = id;
        OwnerId = ownerId;
        SeriesId = seriesId;
        RevisionNumber = revisionNumber;
        NarratorProvider = narratorProvider;
        NarratorProviderVersion = narratorProviderVersion;
        NarratorVoice = narratorVoice;
        NarratorRate = narratorRate;
        NarratorPitch = narratorPitch;
        NarratorVolume = narratorVolume;
        DefaultSpeakerPauseMs = defaultSpeakerPauseMs;
        ChapterPauseMs = chapterPauseMs;
        CompositionVersion = compositionVersion;
        FfmpegProfile = ffmpegProfile;
        CreatedAt = createdAt;
        _assignments.AddRange(assignments);
        Fingerprint = fingerprint;
        Status = NarrationCastRevisionStatus.Draft;
    }

    public Guid Id { get; }
    public Guid OwnerId { get; }
    public Guid SeriesId { get; }
    public int RevisionNumber { get; }
    public string Fingerprint { get; } = string.Empty;
    public NarrationCastRevisionStatus Status { get; private set; }
    public int? EpochNumber { get; private set; }
    public string NarratorProvider { get; } = string.Empty;
    public string NarratorProviderVersion { get; } = string.Empty;
    public string NarratorVoice { get; } = string.Empty;
    public string NarratorRate { get; } = string.Empty;
    public string NarratorPitch { get; } = string.Empty;
    public string NarratorVolume { get; } = string.Empty;
    public int DefaultSpeakerPauseMs { get; }
    public int ChapterPauseMs { get; }
    public string CompositionVersion { get; } = string.Empty;
    public string FfmpegProfile { get; } = string.Empty;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public IReadOnlyList<NarrationCastAssignment> Assignments => _assignments.AsReadOnly();
    public bool CanBeUsedByRegularJob => Status == NarrationCastRevisionStatus.Active;

    public static NarrationCastRevision Create(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        int revisionNumber,
        string narratorProvider,
        string narratorProviderVersion,
        string narratorVoice,
        string narratorRate,
        string narratorPitch,
        string narratorVolume,
        int defaultSpeakerPauseMs,
        int chapterPauseMs,
        string compositionVersion,
        string ffmpegProfile,
        DateTimeOffset createdAt,
        IEnumerable<NarrationCastAssignment> assignments)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        if (revisionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "角色表修訂編號必須大於零。");
        }

        EnsurePause(defaultSpeakerPauseMs, nameof(defaultSpeakerPauseMs));
        EnsurePause(chapterPauseMs, nameof(chapterPauseMs));

        var normalizedNarratorProvider = SeriesValueValidator.NormalizePrintable(
            narratorProvider,
            SeriesFieldLimits.Provider,
            nameof(narratorProvider));
        var normalizedNarratorProviderVersion = SeriesValueValidator.NormalizePrintable(
            narratorProviderVersion,
            SeriesFieldLimits.Provider,
            nameof(narratorProviderVersion));
        var normalizedNarratorVoice = SeriesValueValidator.NormalizePrintable(
            narratorVoice,
            SeriesFieldLimits.Voice,
            nameof(narratorVoice));
        var normalizedNarratorRate = SeriesValueValidator.NormalizePrintable(
            narratorRate,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorRate));
        var normalizedNarratorPitch = SeriesValueValidator.NormalizePrintable(
            narratorPitch,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorPitch));
        var normalizedNarratorVolume = SeriesValueValidator.NormalizePrintable(
            narratorVolume,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorVolume));
        var normalizedCompositionVersion = SeriesValueValidator.NormalizePrintable(
            compositionVersion,
            SeriesFieldLimits.Provider,
            nameof(compositionVersion));
        var normalizedFfmpegProfile = SeriesValueValidator.NormalizePrintable(
            ffmpegProfile,
            SeriesFieldLimits.Voice,
            nameof(ffmpegProfile));

        ArgumentNullException.ThrowIfNull(assignments);
        var assignmentSnapshot = assignments.ToArray();
        if (assignmentSnapshot.Any(assignment => assignment is null))
        {
            throw new ArgumentException("角色語音指派不可包含空值。", nameof(assignments));
        }

        foreach (var assignment in assignmentSnapshot)
        {
            if (assignment.OwnerId != ownerId)
            {
                throw new InvalidOperationException("角色語音指派與修訂必須屬於同一位擁有者。");
            }

            if (assignment.SeriesId != seriesId)
            {
                throw new InvalidOperationException("角色語音指派與修訂必須屬於同一系列。");
            }

            if (assignment.CastRevisionId != id)
            {
                throw new InvalidOperationException("角色語音指派必須指向正在建立的修訂。");
            }
        }

        if (assignmentSnapshot
            .GroupBy(assignment => assignment.Id)
            .Any(group => group.Skip(1).Any()))
        {
            throw new InvalidOperationException("同一修訂內的指派識別碼不可重複。");
        }

        if (assignmentSnapshot
            .GroupBy(assignment => assignment.CharacterId)
            .Any(group => group.Skip(1).Any()))
        {
            throw new InvalidOperationException("同一修訂內的角色識別碼不可重複。");
        }

        var canonicalAssignments = assignmentSnapshot
            .OrderBy(
                assignment => assignment.CharacterId.ToString("N", CultureInfo.InvariantCulture),
                StringComparer.Ordinal)
            .ToArray();
        var fingerprint = ComputeFingerprint(
            normalizedNarratorProvider,
            normalizedNarratorProviderVersion,
            normalizedNarratorVoice,
            normalizedNarratorRate,
            normalizedNarratorPitch,
            normalizedNarratorVolume,
            defaultSpeakerPauseMs,
            chapterPauseMs,
            normalizedCompositionVersion,
            normalizedFfmpegProfile,
            canonicalAssignments);

        return new NarrationCastRevision(
            id,
            ownerId,
            seriesId,
            revisionNumber,
            normalizedNarratorProvider,
            normalizedNarratorProviderVersion,
            normalizedNarratorVoice,
            normalizedNarratorRate,
            normalizedNarratorPitch,
            normalizedNarratorVolume,
            defaultSpeakerPauseMs,
            chapterPauseMs,
            normalizedCompositionVersion,
            normalizedFfmpegProfile,
            createdAt,
            canonicalAssignments,
            fingerprint);
    }

    internal void Activate(int epochNumber, DateTimeOffset activatedAt)
    {
        if (Status != NarrationCastRevisionStatus.Draft)
        {
            throw new InvalidOperationException("只有草稿角色表修訂可以啟用。");
        }

        if (epochNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epochNumber), "角色表 epoch 必須大於零。");
        }

        if (activatedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(activatedAt), "啟用時間不可早於角色表修訂的建立時間。");
        }

        Status = NarrationCastRevisionStatus.Active;
        EpochNumber = epochNumber;
        ActivatedAt = activatedAt;
    }

    internal void MakeHistorical()
    {
        if (Status != NarrationCastRevisionStatus.Active)
        {
            throw new InvalidOperationException("只有啟用中的角色表修訂可以轉為歷史版本。");
        }

        Status = NarrationCastRevisionStatus.Historical;
    }

    internal static string ComputeFingerprint(
        string narratorProvider,
        string narratorProviderVersion,
        string narratorVoice,
        string narratorRate,
        string narratorPitch,
        string narratorVolume,
        int defaultSpeakerPauseMs,
        int chapterPauseMs,
        string compositionVersion,
        string ffmpegProfile,
        IReadOnlyCollection<NarrationCastAssignment> assignments)
    {
        using var canonical = new MemoryStream();
        WriteField(canonical, FingerprintSchemaVersion);
        WriteField(canonical, narratorProvider);
        WriteField(canonical, narratorProviderVersion);
        WriteField(canonical, narratorVoice);
        WriteField(canonical, narratorRate);
        WriteField(canonical, narratorPitch);
        WriteField(canonical, narratorVolume);
        WriteField(canonical, defaultSpeakerPauseMs.ToString(CultureInfo.InvariantCulture));
        WriteField(canonical, chapterPauseMs.ToString(CultureInfo.InvariantCulture));
        WriteField(canonical, compositionVersion);
        WriteField(canonical, ffmpegProfile);
        WriteField(canonical, assignments.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var assignment in assignments)
        {
            WriteField(canonical, assignment.CharacterId.ToString("N", CultureInfo.InvariantCulture));
            WriteField(canonical, assignment.CanonicalNameSnapshot);
            WriteField(canonical, assignment.VoiceProvider);
            WriteField(canonical, assignment.ProviderVersion);
            WriteField(canonical, assignment.Voice);
            WriteField(canonical, assignment.Rate);
            WriteField(canonical, assignment.Pitch);
            WriteField(canonical, assignment.Volume);
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

    private static void EnsurePause(int value, string parameterName)
    {
        if (value is < 0 or > SeriesFieldLimits.MaximumSpeakerPauseMs)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"停頓毫秒數必須介於 0 與 {SeriesFieldLimits.MaximumSpeakerPauseMs} 之間。");
        }
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
