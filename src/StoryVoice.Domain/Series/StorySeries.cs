using StoryVoice.Domain.Books;

namespace StoryVoice.Domain.Series;

public sealed class StorySeries
{
    private readonly List<SeriesBook> _books = [];
    private readonly List<SeriesCharacter> _characters = [];
    private readonly List<SeriesCharacterIdentityKey> _identityKeys = [];
    private bool _isMutationAggregateComplete;

    private StorySeries()
    {
    }

    private StorySeries(
        Guid ownerId,
        string name,
        string narratorProvider,
        string narratorVoice,
        string narratorRate,
        string narratorPitch,
        string narratorVolume,
        int defaultSpeakerPauseMs)
    {
        EnsureId(ownerId, nameof(ownerId));
        if (defaultSpeakerPauseMs is < 0 or > SeriesFieldLimits.MaximumSpeakerPauseMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSpeakerPauseMs),
                $"說話者預設停頓毫秒數必須介於 0 與 {SeriesFieldLimits.MaximumSpeakerPauseMs} 之間。");
        }

        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Name = SeriesIdentityNormalizer.NormalizeDisplayValue(
            name,
            nameof(name),
            SeriesFieldLimits.SeriesName);
        NormalizedName = SeriesIdentityNormalizer.NormalizeKey(
            Name,
            nameof(name),
            SeriesFieldLimits.SeriesName);
        NarratorProvider = SeriesValueValidator.NormalizePrintable(
            narratorProvider,
            SeriesFieldLimits.Provider,
            nameof(narratorProvider));
        NarratorVoice = SeriesValueValidator.NormalizePrintable(
            narratorVoice,
            SeriesFieldLimits.Voice,
            nameof(narratorVoice));
        NarratorRate = SeriesValueValidator.NormalizePrintable(
            narratorRate,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorRate));
        NarratorPitch = SeriesValueValidator.NormalizePrintable(
            narratorPitch,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorPitch));
        NarratorVolume = SeriesValueValidator.NormalizePrintable(
            narratorVolume,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorVolume));
        DefaultSpeakerPauseMs = defaultSpeakerPauseMs;
        NarrativeVoiceMode = StoryVoice.Domain.Series.NarrativeVoiceMode.IndependentNarrator;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        ConcurrencyStamp = Guid.NewGuid();
        _isMutationAggregateComplete = true;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string NarratorProvider { get; private set; } = string.Empty;
    public string NarratorVoice { get; private set; } = string.Empty;
    public string NarratorRate { get; private set; } = string.Empty;
    public string NarratorPitch { get; private set; } = string.Empty;
    public string NarratorVolume { get; private set; } = string.Empty;
    public int DefaultSpeakerPauseMs { get; private set; }
    public Guid? ActiveCastRevisionId { get; private set; }
    public Guid? PointOfViewCharacterId { get; private set; }
    public NarrativeVoiceMode NarrativeVoiceMode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
    public IReadOnlyCollection<SeriesBook> Books => _books.AsReadOnly();
    public IReadOnlyCollection<SeriesCharacter> Characters => _characters.AsReadOnly();
    public IReadOnlyCollection<SeriesCharacterIdentityKey> IdentityKeys => _identityKeys.AsReadOnly();

    public static StorySeries Create(
        Guid ownerId,
        string name,
        string narratorProvider,
        string narratorVoice,
        string narratorRate,
        string narratorPitch,
        string narratorVolume,
        int defaultSpeakerPauseMs) =>
        new(
            ownerId,
            name,
            narratorProvider,
            narratorVoice,
            narratorRate,
            narratorPitch,
            narratorVolume,
            defaultSpeakerPauseMs);

    public SeriesBook AddBook(Book contentBook, string volumeLabel, int sortOrder)
    {
        EnsureMutationAggregateComplete();
        ArgumentNullException.ThrowIfNull(contentBook);
        if (contentBook.Id == Guid.Empty)
        {
            throw new ArgumentException("書籍識別碼不可為空白。", nameof(contentBook));
        }

        if (contentBook.OwnerId is null
            || contentBook.OwnerId == Guid.Empty
            || contentBook.OwnerId != OwnerId)
        {
            throw new InvalidOperationException("系列冊次與正文書籍必須屬於同一位擁有者。");
        }

        if (contentBook.Status == BookStatus.Linked)
        {
            throw new InvalidOperationException("系列冊次只能指向真正含正文的 owner-scoped 書籍。");
        }

        if (_books.Any(book => book.BookId == contentBook.Id))
        {
            throw new InvalidOperationException("這本書已經加入系列。");
        }

        if (_books.Any(book => book.SortOrder == sortOrder))
        {
            throw new InvalidOperationException("系列內的冊次排序不可重複。");
        }

        var membershipRevision = _books.Count == 0
            ? 1
            : checked(_books.Max(book => book.MembershipRevision) + 1);
        var membership = SeriesBook.Create(
            OwnerId,
            Id,
            contentBook.Id,
            volumeLabel,
            sortOrder,
            membershipRevision);
        _books.Add(membership);
        Touch();
        return membership;
    }

    public SeriesCharacter AddCharacter(
        string canonicalName,
        SeriesCharacterRole role,
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume,
        string? notes,
        Guid? characterProfileId = null)
    {
        EnsureMutationAggregateComplete();
        EnsureIdentityKeyAvailable(canonicalName, SeriesFieldLimits.CharacterName);

        var now = DateTimeOffset.UtcNow;
        var characterId = Guid.NewGuid();
        var canonicalIdentityKeyId = Guid.NewGuid();
        var character = SeriesCharacter.Create(
            characterId,
            OwnerId,
            Id,
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
        var canonicalKey = SeriesCharacterIdentityKey.Create(
            canonicalIdentityKeyId,
            OwnerId,
            Id,
            characterId,
            SeriesCharacterIdentityKeyKind.Canonical,
            character.CanonicalName);

        EnsureCanonicalRelationship(character, canonicalKey);
        _characters.Add(character);
        _identityKeys.Add(canonicalKey);
        Touch(now);
        return character;
    }

    public SeriesCharacterIdentityKey AddAlias(Guid characterId, string alias)
    {
        EnsureMutationAggregateComplete();
        EnsureId(characterId, nameof(characterId));
        var character = _characters.SingleOrDefault(candidate => candidate.Id == characterId)
            ?? throw new InvalidOperationException("角色不屬於這個系列。");
        return AddAlias(character, alias);
    }

    public SeriesCharacterIdentityKey AddAlias(SeriesCharacter character, string alias)
    {
        EnsureMutationAggregateComplete();
        var attachedCharacter = EnsureAttachedCharacter(character);
        EnsureIdentityKeyAvailable(alias, SeriesFieldLimits.Alias);

        var identityKey = SeriesCharacterIdentityKey.Create(
            Guid.NewGuid(),
            OwnerId,
            Id,
            attachedCharacter.Id,
            SeriesCharacterIdentityKeyKind.Alias,
            alias);
        _identityKeys.Add(identityKey);
        Touch();
        return identityKey;
    }

    public void RenameCharacter(Guid characterId, string canonicalName)
    {
        EnsureMutationAggregateComplete();
        EnsureId(characterId, nameof(characterId));
        var character = _characters.SingleOrDefault(candidate => candidate.Id == characterId)
            ?? throw new InvalidOperationException("角色不屬於這個系列。");
        RenameCharacter(character, canonicalName);
    }

    public void RenameCharacter(SeriesCharacter character, string canonicalName)
    {
        EnsureMutationAggregateComplete();
        var attachedCharacter = EnsureAttachedCharacter(character);
        var canonicalKey = _identityKeys.SingleOrDefault(key => key.Id == attachedCharacter.CanonicalIdentityKeyId)
            ?? throw new InvalidOperationException("角色缺少 canonical identity key。");
        EnsureCanonicalRelationship(attachedCharacter, canonicalKey);
        EnsureIdentityKeyAvailable(
            canonicalName,
            SeriesFieldLimits.CharacterName,
            canonicalKey.Id);

        var normalizedName = SeriesIdentityNormalizer.NormalizeDisplayValue(
            canonicalName,
            nameof(canonicalName),
            SeriesFieldLimits.CharacterName);
        var now = DateTimeOffset.UtcNow;
        attachedCharacter.Rename(normalizedName, now);
        canonicalKey.RenameCanonical(normalizedName);
        Touch(now);
    }

    public void UpdateCharacter(
        Guid characterId,
        string canonicalName,
        SeriesCharacterRole role,
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume,
        string? notes,
        Guid? characterProfileId = null)
    {
        EnsureMutationAggregateComplete();
        EnsureId(characterId, nameof(characterId));
        var character = _characters.SingleOrDefault(candidate => candidate.Id == characterId)
            ?? throw new InvalidOperationException("角色不屬於這個系列。");
        var canonicalKey = _identityKeys.SingleOrDefault(key => key.Id == character.CanonicalIdentityKeyId)
            ?? throw new InvalidOperationException("角色缺少 canonical identity key。");
        EnsureCanonicalRelationship(character, canonicalKey);
        EnsureIdentityKeyAvailable(
            canonicalName,
            SeriesFieldLimits.CharacterName,
            canonicalKey.Id);

        var now = DateTimeOffset.UtcNow;
        character.Update(
            canonicalName,
            role,
            voiceProvider,
            voice,
            rate,
            pitch,
            volume,
            notes,
            characterProfileId,
            now);
        canonicalKey.RenameCanonical(character.CanonicalName);
        Touch(now);
    }

    public void SetNarratorVoice(
        string narratorProvider,
        string narratorVoice,
        string narratorRate,
        string narratorPitch,
        string narratorVolume)
    {
        EnsureMutationAggregateComplete();
        var normalizedProvider = SeriesValueValidator.NormalizePrintable(
            narratorProvider,
            SeriesFieldLimits.Provider,
            nameof(narratorProvider));
        var normalizedVoice = SeriesValueValidator.NormalizePrintable(
            narratorVoice,
            SeriesFieldLimits.Voice,
            nameof(narratorVoice));
        var normalizedRate = SeriesValueValidator.NormalizePrintable(
            narratorRate,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorRate));
        var normalizedPitch = SeriesValueValidator.NormalizePrintable(
            narratorPitch,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorPitch));
        var normalizedVolume = SeriesValueValidator.NormalizePrintable(
            narratorVolume,
            SeriesFieldLimits.SynthesisParameter,
            nameof(narratorVolume));
        if (string.Equals(NarratorProvider, normalizedProvider, StringComparison.Ordinal)
            && string.Equals(NarratorVoice, normalizedVoice, StringComparison.Ordinal)
            && string.Equals(NarratorRate, normalizedRate, StringComparison.Ordinal)
            && string.Equals(NarratorPitch, normalizedPitch, StringComparison.Ordinal)
            && string.Equals(NarratorVolume, normalizedVolume, StringComparison.Ordinal))
        {
            return;
        }

        NarratorProvider = normalizedProvider;
        NarratorVoice = normalizedVoice;
        NarratorRate = normalizedRate;
        NarratorPitch = normalizedPitch;
        NarratorVolume = normalizedVolume;
        Touch();
    }

    public void SetCharacterVoice(
        Guid characterId,
        string voiceProvider,
        string voice,
        string rate,
        string pitch,
        string volume)
    {
        EnsureMutationAggregateComplete();
        EnsureId(characterId, nameof(characterId));
        var character = _characters.SingleOrDefault(candidate => candidate.Id == characterId)
            ?? throw new InvalidOperationException("角色不屬於這個系列。");
        var now = DateTimeOffset.UtcNow;
        if (character.SetVoice(voiceProvider, voice, rate, pitch, volume, now))
        {
            Touch(now);
        }
    }

    /// <summary>
    /// Names the character whose narration voice is the story's first-person "我" — lets speaker
    /// attribution resolve self-referential reporting clauses ("我說：") to a real cast member
    /// instead of leaving every line the narrator speaks about themselves stuck at Unknown.
    /// </summary>
    public void SetPointOfViewCharacter(Guid? characterId)
    {
        ConfigureNarrativeVoice(NarrativeVoiceMode, characterId);
    }

    public void ConfigureNarrativeVoice(
        NarrativeVoiceMode mode,
        Guid? pointOfViewCharacterId)
    {
        EnsureMutationAggregateComplete();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "不支援指定的敘事聲線模式。");
        }

        if (pointOfViewCharacterId == Guid.Empty)
        {
            throw new ArgumentException("視角角色識別碼不可為空值。", nameof(pointOfViewCharacterId));
        }

        if (pointOfViewCharacterId is Guid id && _characters.All(character => character.Id != id))
        {
            throw new InvalidOperationException("視角角色必須是這個系列裡已經存在的角色。");
        }

        if (mode == StoryVoice.Domain.Series.NarrativeVoiceMode.PointOfViewInnerMonologue
            && pointOfViewCharacterId is null)
        {
            throw new InvalidOperationException("視角角色內心敘事模式必須指定系列內角色。");
        }

        if (NarrativeVoiceMode == mode && PointOfViewCharacterId == pointOfViewCharacterId)
        {
            return;
        }

        NarrativeVoiceMode = mode;
        PointOfViewCharacterId = pointOfViewCharacterId;
        Touch();
    }

    internal void SwitchActiveCastRevision(
        Guid? expectedCurrentRevisionId,
        Guid nextRevisionId,
        DateTimeOffset now)
    {
        if (expectedCurrentRevisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "可為空值的角色表修訂識別碼在有值時不可為空白 Guid。",
                nameof(expectedCurrentRevisionId));
        }

        EnsureId(nextRevisionId, nameof(nextRevisionId));
        if (ActiveCastRevisionId != expectedCurrentRevisionId)
        {
            throw new InvalidOperationException("目前角色表修訂指標已經變更。");
        }

        if (ActiveCastRevisionId == nextRevisionId)
        {
            throw new InvalidOperationException("下一個角色表修訂必須與目前修訂不同。");
        }

        if (now < CreatedAt || now < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "切換時間不可早於建立時間或目前更新時間。");
        }

        ActiveCastRevisionId = nextRevisionId;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private SeriesCharacter EnsureAttachedCharacter(SeriesCharacter? character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var attachedCharacter = _characters.SingleOrDefault(candidate => candidate.Id == character.Id);
        if (attachedCharacter is null
            || !ReferenceEquals(attachedCharacter, character)
            || character.OwnerId != OwnerId
            || character.SeriesId != Id
            || attachedCharacter.OwnerId != OwnerId
            || attachedCharacter.SeriesId != Id)
        {
            throw new InvalidOperationException("角色與系列的 owner、series 或附加實例關係不一致。");
        }

        return attachedCharacter;
    }

    internal void MarkMutationAggregateComplete()
    {
        _isMutationAggregateComplete = true;
    }

    private void EnsureIdentityKeyAvailable(
        string value,
        int maximumLength,
        Guid? ignoredIdentityKeyId = null)
    {
        var normalizedValue = SeriesIdentityNormalizer.NormalizeKey(value, nameof(value), maximumLength);
        if (_identityKeys.Any(key =>
                key.Id != ignoredIdentityKeyId
                && key.OwnerId == OwnerId
                && key.SeriesId == Id
                && string.Equals(key.NormalizedValue, normalizedValue, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("同一系列內的 canonical name 與 alias 不可使用相同的識別文字。");
        }
    }

    private void EnsureCanonicalRelationship(
        SeriesCharacter character,
        SeriesCharacterIdentityKey canonicalKey)
    {
        if (canonicalKey.Id != character.CanonicalIdentityKeyId
            || canonicalKey.OwnerId != OwnerId
            || canonicalKey.SeriesId != Id
            || canonicalKey.CharacterId != character.Id
            || canonicalKey.Kind != SeriesCharacterIdentityKeyKind.Canonical)
        {
            throw new InvalidOperationException("角色與 canonical identity key 的關係無效。");
        }
    }

    private void Touch(DateTimeOffset? now = null)
    {
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
        ConcurrencyStamp = Guid.NewGuid();
    }

    private void EnsureMutationAggregateComplete()
    {
        if (!_isMutationAggregateComplete)
        {
            throw new InvalidOperationException(
                "Series mutations require a complete mutation aggregate with books, characters, and identity keys loaded.");
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
