using StoryVoice.Domain.Series;

namespace StoryVoice.Domain.Narrations;

public sealed class NarrationCastAssignment
{
    private NarrationCastAssignment()
    {
    }

    private NarrationCastAssignment(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid castRevisionId,
        Guid characterId,
        string canonicalNameSnapshot,
        string voiceProvider,
        string providerVersion,
        string voice,
        string rate,
        string pitch,
        string volume)
    {
        Id = id;
        OwnerId = ownerId;
        SeriesId = seriesId;
        CastRevisionId = castRevisionId;
        CharacterId = characterId;
        CanonicalNameSnapshot = canonicalNameSnapshot;
        VoiceProvider = voiceProvider;
        ProviderVersion = providerVersion;
        Voice = voice;
        Rate = rate;
        Pitch = pitch;
        Volume = volume;
    }

    public Guid Id { get; }
    public Guid OwnerId { get; }
    public Guid SeriesId { get; }
    public Guid CastRevisionId { get; }
    public Guid CharacterId { get; }
    public string CanonicalNameSnapshot { get; } = string.Empty;
    public string VoiceProvider { get; } = string.Empty;
    public string ProviderVersion { get; } = string.Empty;
    public string Voice { get; } = string.Empty;
    public string Rate { get; } = string.Empty;
    public string Pitch { get; } = string.Empty;
    public string Volume { get; } = string.Empty;

    public static NarrationCastAssignment Create(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid castRevisionId,
        Guid characterId,
        string canonicalNameSnapshot,
        string voiceProvider,
        string providerVersion,
        string voice,
        string rate,
        string pitch,
        string volume)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(castRevisionId, nameof(castRevisionId));
        EnsureId(characterId, nameof(characterId));

        var normalizedCanonicalName = SeriesValueValidator.NormalizeIdentifier(
            canonicalNameSnapshot,
            SeriesFieldLimits.CharacterName,
            nameof(canonicalNameSnapshot));
        var normalizedVoiceProvider = SeriesValueValidator.NormalizePrintable(
            voiceProvider,
            SeriesFieldLimits.Provider,
            nameof(voiceProvider));
        var normalizedProviderVersion = SeriesValueValidator.NormalizePrintable(
            providerVersion,
            SeriesFieldLimits.Provider,
            nameof(providerVersion));
        var normalizedVoice = SeriesValueValidator.NormalizePrintable(
            voice,
            SeriesFieldLimits.Voice,
            nameof(voice));
        var normalizedRate = SeriesValueValidator.NormalizePrintable(
            rate,
            SeriesFieldLimits.SynthesisParameter,
            nameof(rate));
        var normalizedPitch = SeriesValueValidator.NormalizePrintable(
            pitch,
            SeriesFieldLimits.SynthesisParameter,
            nameof(pitch));
        var normalizedVolume = SeriesValueValidator.NormalizePrintable(
            volume,
            SeriesFieldLimits.SynthesisParameter,
            nameof(volume));

        return new NarrationCastAssignment(
            id,
            ownerId,
            seriesId,
            castRevisionId,
            characterId,
            normalizedCanonicalName,
            normalizedVoiceProvider,
            normalizedProviderVersion,
            normalizedVoice,
            normalizedRate,
            normalizedPitch,
            normalizedVolume);
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
