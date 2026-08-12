using StoryVoice.Domain.Series;

namespace StoryVoice.Domain.Characters;

internal static class CharacterProfileFieldLimits
{
    internal const int Name = 200;
    internal const int AvatarPath = 500;
    internal const int ShortText = 100;
    internal const int MediumText = 2_000;
    internal const int LongText = 4_000;
}

/// <summary>
/// A reusable, owner-scoped character identity — built once in the character library and then
/// picked into any number of <see cref="StoryVoice.Domain.Series.SeriesCharacter"/> across
/// different series, rather than each series re-creating its own copy. Everything voice-related
/// (<see cref="StoryVoice.Domain.Narrations.CharacterVoiceProfile"/>) hangs off this aggregate's
/// <see cref="Id"/>, not off any one series. The bio fields (personality, background, ...) are
/// flavor text for the character sheet — nothing here feeds synthesis directly.
/// </summary>
public sealed class CharacterProfile
{
    private CharacterProfile()
    {
    }

    private CharacterProfile(
        Guid id,
        Guid ownerId,
        string canonicalName,
        string? avatarRelativePath,
        string? age,
        string? gender,
        string? birthday,
        string? personality,
        string? catchphrase,
        string? background,
        string? speakingStyle,
        DateTimeOffset now)
    {
        EnsureId(id, nameof(id));
        EnsureId(ownerId, nameof(ownerId));

        Id = id;
        OwnerId = ownerId;
        CanonicalName = SeriesValueValidator.NormalizePrintable(canonicalName, CharacterProfileFieldLimits.Name, nameof(canonicalName));
        AvatarRelativePath = SeriesValueValidator.NormalizeOptionalPrintable(
            avatarRelativePath, CharacterProfileFieldLimits.AvatarPath, nameof(avatarRelativePath));
        Age = SeriesValueValidator.NormalizeOptionalPrintable(age, CharacterProfileFieldLimits.ShortText, nameof(age));
        Gender = SeriesValueValidator.NormalizeOptionalPrintable(gender, CharacterProfileFieldLimits.ShortText, nameof(gender));
        Birthday = SeriesValueValidator.NormalizeOptionalPrintable(birthday, CharacterProfileFieldLimits.ShortText, nameof(birthday));
        Personality = SeriesValueValidator.NormalizeOptionalPrintable(personality, CharacterProfileFieldLimits.MediumText, nameof(personality));
        Catchphrase = SeriesValueValidator.NormalizeOptionalPrintable(catchphrase, CharacterProfileFieldLimits.MediumText, nameof(catchphrase));
        Background = SeriesValueValidator.NormalizeOptionalPrintable(background, CharacterProfileFieldLimits.LongText, nameof(background));
        SpeakingStyle = SeriesValueValidator.NormalizeOptionalPrintable(speakingStyle, CharacterProfileFieldLimits.MediumText, nameof(speakingStyle));
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string CanonicalName { get; private set; } = string.Empty;
    public string? AvatarRelativePath { get; private set; }
    public string? Age { get; private set; }
    public string? Gender { get; private set; }
    public string? Birthday { get; private set; }
    public string? Personality { get; private set; }
    public string? Catchphrase { get; private set; }
    public string? Background { get; private set; }
    public string? SpeakingStyle { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    public static CharacterProfile Create(
        Guid id,
        Guid ownerId,
        string canonicalName,
        string? avatarRelativePath,
        string? age,
        string? gender,
        string? birthday,
        string? personality,
        string? catchphrase,
        string? background,
        string? speakingStyle,
        DateTimeOffset now) =>
        new(
            id,
            ownerId,
            canonicalName,
            avatarRelativePath,
            age,
            gender,
            birthday,
            personality,
            catchphrase,
            background,
            speakingStyle,
            now);

    public void Update(
        string canonicalName,
        string? age,
        string? gender,
        string? birthday,
        string? personality,
        string? catchphrase,
        string? background,
        string? speakingStyle,
        DateTimeOffset now)
    {
        CanonicalName = SeriesValueValidator.NormalizePrintable(canonicalName, CharacterProfileFieldLimits.Name, nameof(canonicalName));
        Age = SeriesValueValidator.NormalizeOptionalPrintable(age, CharacterProfileFieldLimits.ShortText, nameof(age));
        Gender = SeriesValueValidator.NormalizeOptionalPrintable(gender, CharacterProfileFieldLimits.ShortText, nameof(gender));
        Birthday = SeriesValueValidator.NormalizeOptionalPrintable(birthday, CharacterProfileFieldLimits.ShortText, nameof(birthday));
        Personality = SeriesValueValidator.NormalizeOptionalPrintable(personality, CharacterProfileFieldLimits.MediumText, nameof(personality));
        Catchphrase = SeriesValueValidator.NormalizeOptionalPrintable(catchphrase, CharacterProfileFieldLimits.MediumText, nameof(catchphrase));
        Background = SeriesValueValidator.NormalizeOptionalPrintable(background, CharacterProfileFieldLimits.LongText, nameof(background));
        SpeakingStyle = SeriesValueValidator.NormalizeOptionalPrintable(speakingStyle, CharacterProfileFieldLimits.MediumText, nameof(speakingStyle));
        Touch(now);
    }

    public void SetAvatar(string? avatarRelativePath, DateTimeOffset now)
    {
        AvatarRelativePath = SeriesValueValidator.NormalizeOptionalPrintable(
            avatarRelativePath, CharacterProfileFieldLimits.AvatarPath, nameof(avatarRelativePath));
        Touch(now);
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        Touch(now);
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("識別碼不可為空白。", parameterName);
        }
    }
}
