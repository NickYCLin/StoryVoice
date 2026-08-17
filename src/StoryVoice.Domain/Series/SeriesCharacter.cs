namespace StoryVoice.Domain.Series;

public enum SeriesCharacterRole
{
    Main,
    Supporting,
    Minor
}

public sealed class SeriesCharacter
{
    private SeriesCharacter()
    {
    }

    private SeriesCharacter(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid canonicalIdentityKeyId,
        Guid? characterProfileId,
        string canonicalName,
        SeriesCharacterRole role,
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume,
        string? notes,
        DateTimeOffset now)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(canonicalIdentityKeyId, nameof(canonicalIdentityKeyId));
        if (characterProfileId == Guid.Empty)
        {
            throw new ArgumentException("角色庫識別碼在有值時不可為空白 Guid。", nameof(characterProfileId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "角色類型無效。");
        }

        Id = id;
        OwnerId = ownerId;
        SeriesId = seriesId;
        CanonicalIdentityKeyId = canonicalIdentityKeyId;
        CharacterProfileId = characterProfileId;
        CanonicalName = SeriesIdentityNormalizer.NormalizeDisplayValue(
            canonicalName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        NormalizedName = SeriesIdentityNormalizer.NormalizeKey(
            CanonicalName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        Role = role;
        VoiceProvider = SeriesValueValidator.NormalizePrintable(
            voiceProvider,
            SeriesFieldLimits.Provider,
            nameof(voiceProvider));
        Voice = SeriesValueValidator.NormalizePrintable(voice, SeriesFieldLimits.Voice, nameof(voice));
        Rate = SeriesValueValidator.NormalizePrintable(rate, SeriesFieldLimits.SynthesisParameter, nameof(rate));
        Pitch = SeriesValueValidator.NormalizePrintable(pitch, SeriesFieldLimits.SynthesisParameter, nameof(pitch));
        Volume = SeriesValueValidator.NormalizePrintable(volume, SeriesFieldLimits.SynthesisParameter, nameof(volume));
        Notes = SeriesValueValidator.NormalizeOptionalPrintable(notes, SeriesFieldLimits.Notes, nameof(notes));
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid CanonicalIdentityKeyId { get; private set; }
    public Guid? CharacterProfileId { get; private set; }
    public string CanonicalName { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public SeriesCharacterRole Role { get; private set; }
    public string VoiceProvider { get; private set; } = string.Empty;
    public string Voice { get; private set; } = string.Empty;
    public string Rate { get; private set; } = string.Empty;
    public string Pitch { get; private set; } = string.Empty;
    public string Volume { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    internal static SeriesCharacter Create(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid canonicalIdentityKeyId,
        Guid? characterProfileId,
        string canonicalName,
        SeriesCharacterRole role,
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume,
        string? notes,
        DateTimeOffset now) =>
        new(
            id,
            ownerId,
            seriesId,
            canonicalIdentityKeyId,
            characterProfileId,
            canonicalName,
            role,
            voiceProvider,
            voice,
            rate,
            pitch,
            volume,
            notes,
            now);

    internal void Rename(string canonicalName, DateTimeOffset now)
    {
        CanonicalName = SeriesIdentityNormalizer.NormalizeDisplayValue(
            canonicalName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        NormalizedName = SeriesIdentityNormalizer.NormalizeKey(
            CanonicalName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    internal void Update(
        string canonicalName,
        SeriesCharacterRole role,
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume,
        string? notes,
        Guid? characterProfileId,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), "角色類型無效。");
        }

        if (characterProfileId == Guid.Empty)
        {
            throw new ArgumentException("角色庫識別碼在有值時不可為空白 Guid。", nameof(characterProfileId));
        }

        var normalizedName = SeriesIdentityNormalizer.NormalizeDisplayValue(
            canonicalName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        var normalizedKey = SeriesIdentityNormalizer.NormalizeKey(
            normalizedName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        var normalizedProvider = SeriesValueValidator.NormalizePrintable(
            voiceProvider,
            SeriesFieldLimits.Provider,
            nameof(voiceProvider));
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
        var normalizedNotes = SeriesValueValidator.NormalizeOptionalPrintable(
            notes,
            SeriesFieldLimits.Notes,
            nameof(notes));

        CanonicalName = normalizedName;
        NormalizedName = normalizedKey;
        Role = role;
        VoiceProvider = normalizedProvider;
        Voice = normalizedVoice;
        Rate = normalizedRate;
        Pitch = normalizedPitch;
        Volume = normalizedVolume;
        Notes = normalizedNotes;
        CharacterProfileId = characterProfileId;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    internal bool SetVoice(
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume,
        DateTimeOffset now)
    {
        var normalizedProvider = SeriesValueValidator.NormalizePrintable(
            voiceProvider,
            SeriesFieldLimits.Provider,
            nameof(voiceProvider));
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
        if (string.Equals(VoiceProvider, normalizedProvider, StringComparison.Ordinal)
            && string.Equals(Voice, normalizedVoice, StringComparison.Ordinal)
            && string.Equals(Rate, normalizedRate, StringComparison.Ordinal)
            && string.Equals(Pitch, normalizedPitch, StringComparison.Ordinal)
            && string.Equals(Volume, normalizedVolume, StringComparison.Ordinal))
        {
            return false;
        }

        VoiceProvider = normalizedProvider;
        Voice = normalizedVoice;
        Rate = normalizedRate;
        Pitch = normalizedPitch;
        Volume = normalizedVolume;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    internal bool SetCharacterProfile(Guid? characterProfileId, DateTimeOffset now)
    {
        if (characterProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "角色庫識別碼在有值時不可為空白 Guid。",
                nameof(characterProfileId));
        }

        if (CharacterProfileId == characterProfileId)
        {
            return false;
        }

        CharacterProfileId = characterProfileId;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
