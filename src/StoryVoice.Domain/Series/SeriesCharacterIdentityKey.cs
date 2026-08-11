namespace StoryVoice.Domain.Series;

public enum SeriesCharacterIdentityKeyKind
{
    Canonical,
    Alias
}

public sealed class SeriesCharacterIdentityKey
{
    private SeriesCharacterIdentityKey()
    {
    }

    private SeriesCharacterIdentityKey(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid characterId,
        SeriesCharacterIdentityKeyKind kind,
        string value)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));
        EnsureId(seriesId, nameof(seriesId));
        EnsureId(characterId, nameof(characterId));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "角色識別鍵類型無效。");
        }

        Id = id;
        OwnerId = ownerId;
        SeriesId = seriesId;
        CharacterId = characterId;
        Kind = kind;
        SetValue(value);
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid CharacterId { get; private set; }
    public SeriesCharacterIdentityKeyKind Kind { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public string NormalizedValue { get; private set; } = string.Empty;

    internal static SeriesCharacterIdentityKey Create(
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid characterId,
        SeriesCharacterIdentityKeyKind kind,
        string value) =>
        new(id, ownerId, seriesId, characterId, kind, value);

    internal void RenameCanonical(string value)
    {
        if (Kind != SeriesCharacterIdentityKeyKind.Canonical)
        {
            throw new InvalidOperationException("只有 canonical identity key 可以隨角色改名。");
        }

        SetValue(value);
    }

    private void SetValue(string value)
    {
        var maximumLength = Kind == SeriesCharacterIdentityKeyKind.Canonical
            ? SeriesFieldLimits.CharacterName
            : SeriesFieldLimits.Alias;
        Value = SeriesIdentityNormalizer.NormalizeDisplayValue(value, nameof(value), maximumLength);
        NormalizedValue = SeriesIdentityNormalizer.NormalizeKey(Value, nameof(value), maximumLength);
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}

internal static class SeriesIdentityNormalizer
{
    internal static string NormalizeDisplayValue(
        string? value,
        string parameterName,
        int maximumLength) =>
        SeriesValueValidator.NormalizeIdentifier(value, maximumLength, parameterName);

    internal static string NormalizeKey(
        string? value,
        string parameterName,
        int maximumLength) =>
        NormalizeDisplayValue(value, parameterName, maximumLength).ToUpperInvariant();
}
