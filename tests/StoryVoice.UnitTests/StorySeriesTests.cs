using System.Reflection;
using System.Runtime.ExceptionServices;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Series;

namespace StoryVoice.UnitTests;

public sealed class StorySeriesTests
{
    private const int MaximumSeriesNameLength = 200;
    private const int MaximumCharacterNameLength = 200;
    private const int MaximumAliasLength = 200;
    private const int MaximumVolumeLabelLength = 100;
    private const int MaximumProviderLength = 50;
    private const int MaximumVoiceLength = 200;
    private const int MaximumParameterLength = 50;
    private const int MaximumNotesLength = 2_000;
    private const int MaximumSpeakerPauseMs = 60_000;
    private const int MaximumSortOrder = 1_000_000;

    [Fact]
    public void Canonical_and_aliases_share_one_normalized_identity_key_namespace()
    {
        var series = CreateSeries(Guid.NewGuid(), "  星際旅人  ");

        var alice = series.AddCharacter(
            "  Alice  ",
            SeriesCharacterRole.Main,
            "edge",
            "zh-TW-HsiaoChenNeural",
            "+0%",
            "+0Hz",
            "+0%",
            "  冷靜、清楚的配音提示  ");

        Assert.Equal("星際旅人", series.Name);
        Assert.Equal("星際旅人", series.NormalizedName);
        Assert.Equal("Alice", alice.CanonicalName);
        Assert.Equal("ALICE", alice.NormalizedName);
        Assert.NotEqual(Guid.Empty, alice.CanonicalIdentityKeyId);
        var canonicalKey = Assert.Single(series.IdentityKeys);
        Assert.Equal(SeriesCharacterIdentityKeyKind.Canonical, canonicalKey.Kind);
        Assert.Equal(alice.CanonicalIdentityKeyId, canonicalKey.Id);
        Assert.Equal(series.OwnerId, canonicalKey.OwnerId);
        Assert.Equal(series.Id, canonicalKey.SeriesId);
        Assert.Equal(alice.Id, canonicalKey.CharacterId);
        Assert.Equal("ALICE", canonicalKey.NormalizedValue);

        Assert.Throws<InvalidOperationException>(() => series.AddAlias(alice, "  alice  "));

        var alias = series.AddAlias(alice, "  The   Captain  ");
        Assert.Equal("The Captain", alias.Value);
        Assert.Equal("THE CAPTAIN", alias.NormalizedValue);
        Assert.Equal(SeriesCharacterIdentityKeyKind.Alias, alias.Kind);

        Assert.Throws<InvalidOperationException>(() => series.RenameCharacter(alice, "the captain"));
        Assert.Equal("Alice", alice.CanonicalName);
        Assert.Equal("ALICE", canonicalKey.NormalizedValue);

        Assert.Throws<InvalidOperationException>(() => series.AddCharacter(
            "the captain",
            SeriesCharacterRole.Supporting,
            "edge",
            "zh-TW-YunJheNeural",
            "+0%",
            "+0Hz",
            "+0%",
            null));

        Assert.Single(series.Characters);
        Assert.Equal(2, series.IdentityKeys.Count);
    }

    [Fact]
    public void Explicit_character_update_changes_cast_and_canonical_key_atomically()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var character = series.AddCharacter(
            "Alice",
            SeriesCharacterRole.Supporting,
            "edge",
            "zh-TW-HsiaoChenNeural",
            "+0%",
            "+0Hz",
            "+0%",
            null);
        series.AddAlias(character.Id, "隊長");

        series.UpdateCharacter(
            character.Id,
            "Alice Prime",
            SeriesCharacterRole.Main,
            "edge",
            "zh-TW-HsiaoYuNeural",
            "-5%",
            "+2Hz",
            "-3%",
            "使用者確認的固定角色聲線");

        Assert.Equal("Alice Prime", character.CanonicalName);
        Assert.Equal("ALICE PRIME", character.NormalizedName);
        Assert.Equal(SeriesCharacterRole.Main, character.Role);
        Assert.Equal("zh-TW-HsiaoYuNeural", character.Voice);
        Assert.Equal("-5%", character.Rate);
        Assert.Equal("+2Hz", character.Pitch);
        Assert.Equal("-3%", character.Volume);
        Assert.Equal("使用者確認的固定角色聲線", character.Notes);
        var canonicalKey = Assert.Single(
            series.IdentityKeys,
            key => key.Kind == SeriesCharacterIdentityKeyKind.Canonical);
        Assert.Equal("Alice Prime", canonicalKey.Value);
        Assert.Equal("ALICE PRIME", canonicalKey.NormalizedValue);

        var beforeInvalidUpdate = (
            character.CanonicalName,
            character.Role,
            character.Voice,
            character.Rate,
            character.Pitch,
            character.Volume,
            character.Notes,
            character.ConcurrencyStamp,
            series.ConcurrencyStamp);
        Assert.Throws<InvalidOperationException>(() => series.UpdateCharacter(
            character.Id,
            "隊長",
            SeriesCharacterRole.Minor,
            "edge",
            "zh-TW-YunJheNeural",
            "+0%",
            "+0Hz",
            "+0%",
            null));
        Assert.Equal(beforeInvalidUpdate, (
            character.CanonicalName,
            character.Role,
            character.Voice,
            character.Rate,
            character.Pitch,
            character.Volume,
            character.Notes,
            character.ConcurrencyStamp,
            series.ConcurrencyStamp));
    }

    [Fact]
    public void Nfkc_compatible_fullwidth_and_halfwidth_identity_keys_collide_without_mutation()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var character = series.AddCharacter(
            "カタカナ", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var canonicalKey = Assert.Single(series.IdentityKeys);
        var identityKeySnapshot = (
            canonicalKey.Id,
            canonicalKey.OwnerId,
            canonicalKey.SeriesId,
            canonicalKey.CharacterId,
            canonicalKey.Kind,
            canonicalKey.Value,
            canonicalKey.NormalizedValue);

        Assert.Throws<InvalidOperationException>(() => series.AddAlias(character, "ｶﾀｶﾅ"));

        var unchangedCanonicalKey = Assert.Single(series.IdentityKeys);
        Assert.Same(canonicalKey, unchangedCanonicalKey);
        Assert.Equal(identityKeySnapshot, (
            unchangedCanonicalKey.Id,
            unchangedCanonicalKey.OwnerId,
            unchangedCanonicalKey.SeriesId,
            unchangedCanonicalKey.CharacterId,
            unchangedCanonicalKey.Kind,
            unchangedCanonicalKey.Value,
            unchangedCanonicalKey.NormalizedValue));
    }

    [Fact]
    public void Alias_to_alias_normalized_collision_is_rejected_without_changing_identity_keys()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        series.AddAlias(character, "  The   Captain  ");
        var seriesStamp = series.ConcurrencyStamp;
        var identityKeySnapshot = series.IdentityKeys
            .Select(key => (key.Id, key.Value, key.NormalizedValue, key.Kind))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => series.AddAlias(character, "the captain"));

        Assert.Equal(seriesStamp, series.ConcurrencyStamp);
        Assert.Equal(identityKeySnapshot, series.IdentityKeys
            .Select(key => (key.Id, key.Value, key.NormalizedValue, key.Kind))
            .ToArray());
    }

    [Fact]
    public void The_same_normalized_identity_key_is_allowed_in_another_owner_or_series()
    {
        var ownerId = Guid.NewGuid();
        var first = CreateSeries(ownerId, "第一系列");
        var anotherSeries = CreateSeries(ownerId, "第二系列");
        var anotherOwner = CreateSeries(Guid.NewGuid(), "第一系列");

        first.AddCharacter("Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        anotherSeries.AddCharacter(" alice ", SeriesCharacterRole.Supporting, "edge", "voice-b", "+0%", "+0Hz", "+0%", null);
        anotherOwner.AddCharacter("ALICE", SeriesCharacterRole.Minor, "edge", "voice-c", "+0%", "+0Hz", "+0%", null);

        Assert.Single(first.Characters);
        Assert.Single(anotherSeries.Characters);
        Assert.Single(anotherOwner.Characters);
    }

    [Fact]
    public void Empty_ids_and_cross_owner_or_cross_series_relationships_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CreateSeries(Guid.Empty, "系列"));

        var ownerId = Guid.NewGuid();
        var series = CreateSeries(ownerId, "第一系列");
        var otherSeries = CreateSeries(ownerId, "第二系列");
        var otherOwnerSeries = CreateSeries(Guid.NewGuid(), "其他人的系列");
        var otherSeriesCharacter = otherSeries.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var otherOwnerCharacter = otherOwnerSeries.AddCharacter(
            "Bob", SeriesCharacterRole.Main, "edge", "voice-b", "+0%", "+0Hz", "+0%", null);

        Assert.Throws<ArgumentException>(() => series.AddAlias(Guid.Empty, "別名"));
        Assert.Throws<InvalidOperationException>(() => series.AddAlias(otherSeriesCharacter, "別名"));
        Assert.Throws<InvalidOperationException>(() => series.RenameCharacter(otherOwnerCharacter, "新名字"));

        var foreignBook = Book.Create(Guid.NewGuid(), "外人之書", "作者", "zh-TW", "foreign.txt");
        Assert.Throws<InvalidOperationException>(() => series.AddBook(foreignBook, "第一冊", 1));

        var externalBook = Book.CreateExternal(
            ownerId,
            "外部書目",
            "作者",
            "zh-TW",
            "provider",
            "external-id",
            "https://example.com/book",
            null);
        Assert.Throws<InvalidOperationException>(() => series.AddBook(externalBook, "第一冊", 1));

        Assert.Empty(series.Books);
        Assert.Empty(series.Characters);
        Assert.Empty(series.IdentityKeys);
    }

    [Fact]
    public void Detached_same_identity_character_is_rejected_when_adding_alias()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var attachedCharacter = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var detachedCharacter = MaterializeDetachedCharacter(attachedCharacter);
        var canonicalKey = Assert.Single(series.IdentityKeys);
        var seriesStamp = series.ConcurrencyStamp;

        Assert.NotSame(attachedCharacter, detachedCharacter);
        Assert.Equal(attachedCharacter.Id, detachedCharacter.Id);
        Assert.Equal(attachedCharacter.OwnerId, detachedCharacter.OwnerId);
        Assert.Equal(attachedCharacter.SeriesId, detachedCharacter.SeriesId);
        Assert.Throws<InvalidOperationException>(() => series.AddAlias(detachedCharacter, "Captain"));

        Assert.Same(attachedCharacter, Assert.Single(series.Characters));
        Assert.Same(canonicalKey, Assert.Single(series.IdentityKeys));
        Assert.Equal(seriesStamp, series.ConcurrencyStamp);
    }

    [Fact]
    public void Detached_same_identity_character_is_rejected_when_renaming_without_splitting_state()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var attachedCharacter = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var detachedCharacter = MaterializeDetachedCharacter(attachedCharacter);
        var canonicalKey = Assert.Single(series.IdentityKeys);
        var seriesStamp = series.ConcurrencyStamp;
        var attachedCharacterStamp = attachedCharacter.ConcurrencyStamp;
        var detachedCharacterStamp = detachedCharacter.ConcurrencyStamp;

        Assert.NotSame(attachedCharacter, detachedCharacter);
        Assert.Equal(attachedCharacter.Id, detachedCharacter.Id);
        Assert.Equal(attachedCharacter.OwnerId, detachedCharacter.OwnerId);
        Assert.Equal(attachedCharacter.SeriesId, detachedCharacter.SeriesId);
        Assert.Throws<InvalidOperationException>(() => series.RenameCharacter(detachedCharacter, "Alice Prime"));

        Assert.Equal("Alice", attachedCharacter.CanonicalName);
        Assert.Equal("ALICE", attachedCharacter.NormalizedName);
        Assert.Equal(attachedCharacterStamp, attachedCharacter.ConcurrencyStamp);
        Assert.Equal("Alice", detachedCharacter.CanonicalName);
        Assert.Equal("ALICE", detachedCharacter.NormalizedName);
        Assert.Equal(detachedCharacterStamp, detachedCharacter.ConcurrencyStamp);
        Assert.Equal("Alice", canonicalKey.Value);
        Assert.Equal("ALICE", canonicalKey.NormalizedValue);
        Assert.Equal(seriesStamp, series.ConcurrencyStamp);
        Assert.Same(attachedCharacter, Assert.Single(series.Characters));
        Assert.Same(canonicalKey, Assert.Single(series.IdentityKeys));
    }

    [Fact]
    public void Guid_character_overloads_continue_to_resolve_the_attached_character()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);

        var alias = series.AddAlias(character.Id, "Captain");
        series.RenameCharacter(character.Id, "Alice Prime");

        Assert.Equal(character.Id, alias.CharacterId);
        Assert.Equal("CAPTAIN", alias.NormalizedValue);
        Assert.Equal("Alice Prime", character.CanonicalName);
        var canonicalKey = Assert.Single(
            series.IdentityKeys,
            key => key.Kind == SeriesCharacterIdentityKeyKind.Canonical);
        Assert.Equal(character.CanonicalIdentityKeyId, canonicalKey.Id);
        Assert.Equal("ALICE PRIME", canonicalKey.NormalizedValue);
    }

    [Fact]
    public void Adding_another_book_does_not_overwrite_fixed_narrator_or_character_voice_profiles()
    {
        var ownerId = Guid.NewGuid();
        var series = CreateSeries(ownerId, "固定聲線系列");
        var character = series.AddCharacter(
            "Alice",
            SeriesCharacterRole.Main,
            "edge",
            "character-voice",
            "-5%",
            "+2Hz",
            "-3%",
            "溫柔但堅定");
        var firstBook = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");
        var secondBook = Book.Create(ownerId, "第二冊", "作者", "zh-TW", "two.txt");

        var narratorProfile = (
            series.NarratorProvider,
            series.NarratorVoice,
            series.NarratorRate,
            series.NarratorPitch,
            series.NarratorVolume);
        var characterProfile = (
            character.VoiceProvider,
            character.Voice,
            character.Rate,
            character.Pitch,
            character.Volume);

        var firstMembership = series.AddBook(firstBook, "第一冊", 1);
        var secondMembership = series.AddBook(secondBook, "第二冊", 2);

        Assert.Equal(narratorProfile, (
            series.NarratorProvider,
            series.NarratorVoice,
            series.NarratorRate,
            series.NarratorPitch,
            series.NarratorVolume));
        Assert.Equal(characterProfile, (
            character.VoiceProvider,
            character.Voice,
            character.Rate,
            character.Pitch,
            character.Volume));
        Assert.Equal(firstBook.Id, firstMembership.BookId);
        Assert.Equal(secondBook.Id, secondMembership.BookId);
        Assert.Equal(1, firstMembership.MembershipRevision);
        Assert.Equal(2, secondMembership.MembershipRevision);
        Assert.Equal(2, series.Books.Count);
    }

    [Fact]
    public void Duplicate_book_id_is_rejected_without_changing_books_stamps_or_voice_profiles()
    {
        var ownerId = Guid.NewGuid();
        var series = CreateSeries(ownerId, "系列");
        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "character-voice", "-5%", "+2Hz", "-3%", null);
        var book = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");
        var membership = series.AddBook(book, "第一冊", 1);

        AssertFailedBookAdditionDoesNotChangeAggregate(
            series,
            character,
            membership,
            () => series.AddBook(book, "重複冊次", 2));
    }

    [Fact]
    public void Duplicate_sort_order_is_rejected_without_changing_books_stamps_or_voice_profiles()
    {
        var ownerId = Guid.NewGuid();
        var series = CreateSeries(ownerId, "系列");
        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "character-voice", "-5%", "+2Hz", "-3%", null);
        var firstBook = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");
        var secondBook = Book.Create(ownerId, "第二冊", "作者", "zh-TW", "two.txt");
        var membership = series.AddBook(firstBook, "第一冊", 1);

        AssertFailedBookAdditionDoesNotChangeAggregate(
            series,
            character,
            membership,
            () => series.AddBook(secondBook, "第二冊", 1));
    }

    [Fact]
    public void Aggregate_mutations_refresh_timestamps_and_concurrency_stamps()
    {
        var series = CreateSeries(Guid.NewGuid(), "系列");
        var initialSeriesStamp = series.ConcurrencyStamp;

        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var characterStamp = character.ConcurrencyStamp;
        var characterCreatedAt = character.CreatedAt;
        var seriesStampAfterCharacter = series.ConcurrencyStamp;

        series.RenameCharacter(character, "  Alice Prime  ");

        Assert.NotEqual(Guid.Empty, initialSeriesStamp);
        Assert.NotEqual(initialSeriesStamp, seriesStampAfterCharacter);
        Assert.NotEqual(seriesStampAfterCharacter, series.ConcurrencyStamp);
        Assert.NotEqual(Guid.Empty, characterStamp);
        Assert.NotEqual(characterStamp, character.ConcurrencyStamp);
        Assert.Equal("Alice Prime", character.CanonicalName);
        Assert.Equal("ALICE PRIME", character.NormalizedName);
        var canonicalKey = Assert.Single(
            series.IdentityKeys,
            key => key.Id == character.CanonicalIdentityKeyId);
        Assert.Equal("Alice Prime", canonicalKey.Value);
        Assert.Equal("ALICE PRIME", canonicalKey.NormalizedValue);
        Assert.Equal(characterCreatedAt, character.CreatedAt);
        Assert.True(character.UpdatedAt >= character.CreatedAt);
        Assert.True(series.UpdatedAt >= series.CreatedAt);

        var formerCanonicalName = series.AddAlias(character, "alice");
        Assert.Equal("ALICE", formerCanonicalName.NormalizedValue);
    }

    [Fact]
    public void Text_fields_reject_normalized_maximum_plus_one_without_mutating_the_aggregate()
    {
        var ownerId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CreateSeries(
            ownerId,
            new string('Ｓ', MaximumSeriesNameLength + 1)));

        var series = CreateSeries(ownerId, "系列");
        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var book = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");
        var characterCount = series.Characters.Count;
        var identityKeyCount = series.IdentityKeys.Count;
        var bookCount = series.Books.Count;
        var stamp = series.ConcurrencyStamp;

        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            new string('Ｃ', MaximumCharacterNameLength + 1),
            SeriesCharacterRole.Main,
            "edge",
            "voice-b",
            "+0%",
            "+0Hz",
            "+0%",
            null));
        Assert.Throws<ArgumentException>(() => series.AddAlias(
            character.Id,
            new string('Ａ', MaximumAliasLength + 1)));
        Assert.Throws<ArgumentException>(() => series.AddBook(
            book,
            new string('Ｖ', MaximumVolumeLabelLength + 1),
            1));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Provider Overflow",
            SeriesCharacterRole.Main,
            new string('p', MaximumProviderLength + 1),
            "voice-b",
            "+0%",
            "+0Hz",
            "+0%",
            null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Voice Overflow",
            SeriesCharacterRole.Main,
            "edge",
            new string('v', MaximumVoiceLength + 1),
            "+0%",
            "+0Hz",
            "+0%",
            null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Rate Overflow",
            SeriesCharacterRole.Main,
            "edge",
            "voice-b",
            new string('r', MaximumParameterLength + 1),
            "+0Hz",
            "+0%",
            null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Pitch Overflow",
            SeriesCharacterRole.Main,
            "edge",
            "voice-b",
            "+0%",
            new string('p', MaximumParameterLength + 1),
            "+0%",
            null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Volume Overflow",
            SeriesCharacterRole.Main,
            "edge",
            "voice-b",
            "+0%",
            "+0Hz",
            new string('v', MaximumParameterLength + 1),
            null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Notes Overflow",
            SeriesCharacterRole.Main,
            "edge",
            "voice-b",
            "+0%",
            "+0Hz",
            "+0%",
            new string('n', MaximumNotesLength + 1)));

        Assert.Equal(characterCount, series.Characters.Count);
        Assert.Equal(identityKeyCount, series.IdentityKeys.Count);
        Assert.Equal(bookCount, series.Books.Count);
        Assert.Equal(stamp, series.ConcurrencyStamp);
    }

    [Fact]
    public void Maximum_length_values_are_accepted_after_nfkc_normalization()
    {
        var ownerId = Guid.NewGuid();
        var series = StorySeries.Create(
            ownerId,
            new string('Ｓ', MaximumSeriesNameLength),
            new string('p', MaximumProviderLength),
            new string('v', MaximumVoiceLength),
            new string('r', MaximumParameterLength),
            new string('p', MaximumParameterLength),
            new string('v', MaximumParameterLength),
            MaximumSpeakerPauseMs);
        var character = series.AddCharacter(
            new string('Ｃ', MaximumCharacterNameLength),
            SeriesCharacterRole.Main,
            new string('p', MaximumProviderLength),
            new string('v', MaximumVoiceLength),
            new string('r', MaximumParameterLength),
            new string('p', MaximumParameterLength),
            new string('v', MaximumParameterLength),
            new string('n', MaximumNotesLength));
        var alias = series.AddAlias(character.Id, new string('Ａ', MaximumAliasLength));
        var book = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");
        var membership = series.AddBook(book, new string('Ｖ', MaximumVolumeLabelLength), MaximumSortOrder);

        Assert.Equal(MaximumSeriesNameLength, series.Name.Length);
        Assert.Equal(MaximumCharacterNameLength, character.CanonicalName.Length);
        Assert.Equal(MaximumAliasLength, alias.Value.Length);
        Assert.Equal(MaximumVolumeLabelLength, membership.VolumeLabel.Length);
    }

    [Theory]
    [InlineData("bad\0value")]
    [InlineData("bad\u0001value")]
    [InlineData("bad\u200Bvalue")]
    [InlineData("\u200B\u200C\u2060")]
    public void Identifiers_reject_nul_control_format_and_zero_width_only_values(string invalidValue)
    {
        var ownerId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CreateSeries(ownerId, invalidValue));

        var series = CreateSeries(ownerId, "系列");
        var character = series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var book = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");

        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            invalidValue, SeriesCharacterRole.Main, "edge", "voice-b", "+0%", "+0Hz", "+0%", null));
        Assert.Throws<ArgumentException>(() => series.AddAlias(character.Id, invalidValue));
        Assert.Throws<ArgumentException>(() => series.AddBook(book, invalidValue, 1));
    }

    [Fact]
    public void Invalid_enum_empty_voice_fields_and_out_of_range_numbers_are_rejected()
    {
        var ownerId = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => StorySeries.Create(
            ownerId,
            "系列",
            "edge",
            "voice-a",
            "+0%",
            "+0Hz",
            "+0%",
            MaximumSpeakerPauseMs + 1));

        var series = CreateSeries(ownerId, "系列");
        Assert.Throws<ArgumentOutOfRangeException>(() => series.AddCharacter(
            "Alice", (SeriesCharacterRole)99, "edge", "voice-a", "+0%", "+0Hz", "+0%", null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "", "voice-a", "+0%", "+0Hz", "+0%", null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "", "+0%", "+0Hz", "+0%", null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "", "+0Hz", "+0%", null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "", "+0%", null));
        Assert.Throws<ArgumentException>(() => series.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "", null));

        var book = Book.Create(ownerId, "第一冊", "作者", "zh-TW", "one.txt");
        Assert.Throws<ArgumentOutOfRangeException>(() => series.AddBook(book, "第一冊", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => series.AddBook(book, "第一冊", MaximumSortOrder + 1));
        Assert.Throws<ArgumentException>(() => series.AddBook(book, "", 1));
    }

    [Fact]
    public void Active_cast_pointer_switch_is_internal_atomic_and_refreshes_series_state()
    {
        var series = CreateSeries(Guid.NewGuid(), "Atomic cast series");
        var firstRevisionId = Guid.NewGuid();
        var secondRevisionId = Guid.NewGuid();
        var initialStamp = series.ConcurrencyStamp;
        var activatedAt = series.UpdatedAt;

        InvokeInternal(
            series,
            "SwitchActiveCastRevision",
            [null, firstRevisionId, activatedAt]);

        Assert.Equal(firstRevisionId, series.ActiveCastRevisionId);
        Assert.Equal(activatedAt, series.UpdatedAt);
        Assert.NotEqual(initialStamp, series.ConcurrencyStamp);

        AssertSeriesPointerRejectedAtomically<InvalidOperationException>(
            series,
            () => InvokeInternal(
                series,
                "SwitchActiveCastRevision",
                [null, secondRevisionId, activatedAt]));
        AssertSeriesPointerRejectedAtomically<InvalidOperationException>(
            series,
            () => InvokeInternal(
                series,
                "SwitchActiveCastRevision",
                [firstRevisionId, firstRevisionId, activatedAt]));
        AssertSeriesPointerRejectedAtomically<ArgumentException>(
            series,
            () => InvokeInternal(
                series,
                "SwitchActiveCastRevision",
                [Guid.Empty, secondRevisionId, activatedAt]));
        AssertSeriesPointerRejectedAtomically<ArgumentException>(
            series,
            () => InvokeInternal(
                series,
                "SwitchActiveCastRevision",
                [firstRevisionId, Guid.Empty, activatedAt]));
        AssertSeriesPointerRejectedAtomically<ArgumentOutOfRangeException>(
            series,
            () => InvokeInternal(
                series,
                "SwitchActiveCastRevision",
                [firstRevisionId, secondRevisionId, series.UpdatedAt.AddTicks(-1)]));
    }

    [Fact]
    public void Active_job_pointer_switch_is_internal_and_rejects_invalid_inputs_without_mutation()
    {
        var ownerId = Guid.NewGuid();
        var series = CreateSeries(ownerId, "Atomic book pointer series");
        var book = Book.Create(ownerId, "Synthetic book", "Synthetic author", "en", "synthetic.txt");
        var membership = series.AddBook(book, "Volume one", 1);
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();

        AssertSeriesBookPointerRejectedAtomically<ArgumentException>(
            membership,
            () => InvokeInternal(membership, "SwitchActiveNarrationJob", [Guid.Empty, firstJobId]));
        AssertSeriesBookPointerRejectedAtomically<ArgumentException>(
            membership,
            () => InvokeInternal(membership, "SwitchActiveNarrationJob", [null, Guid.Empty]));

        InvokeInternal(membership, "SwitchActiveNarrationJob", [null, firstJobId]);
        Assert.Equal(firstJobId, membership.ActiveNarrationJobId);

        AssertSeriesBookPointerRejectedAtomically<InvalidOperationException>(
            membership,
            () => InvokeInternal(membership, "SwitchActiveNarrationJob", [null, secondJobId]));
        AssertSeriesBookPointerRejectedAtomically<InvalidOperationException>(
            membership,
            () => InvokeInternal(membership, "SwitchActiveNarrationJob", [firstJobId, firstJobId]));
    }

    [Fact]
    public void Point_of_view_character_must_belong_to_the_series_and_can_be_cleared()
    {
        var ownerId = Guid.NewGuid();
        var series = CreateSeries(ownerId, "系列");
        var character = series.AddCharacter(
            "褚冥漾", SeriesCharacterRole.Main, "edge", "voice-a", "+0%", "+0Hz", "+0%", null);
        var otherSeries = CreateSeries(ownerId, "另一個系列");
        var foreignCharacter = otherSeries.AddCharacter(
            "外人", SeriesCharacterRole.Main, "edge", "voice-b", "+0%", "+0Hz", "+0%", null);

        Assert.Throws<InvalidOperationException>(
            () => series.SetPointOfViewCharacter(foreignCharacter.Id));
        Assert.Null(series.PointOfViewCharacterId);

        series.SetPointOfViewCharacter(character.Id);
        Assert.Equal(character.Id, series.PointOfViewCharacterId);

        series.SetPointOfViewCharacter(null);
        Assert.Null(series.PointOfViewCharacterId);
    }

    private static StorySeries CreateSeries(Guid ownerId, string name) =>
        StorySeries.Create(
            ownerId,
            name,
            "edge",
            "zh-TW-YunJheNeural",
            "-5%",
            "+0Hz",
            "+0%",
            350);

    private static SeriesCharacter MaterializeDetachedCharacter(SeriesCharacter source)
    {
        var detachedCharacter = Assert.IsType<SeriesCharacter>(
            Activator.CreateInstance(typeof(SeriesCharacter), nonPublic: true));
        foreach (var property in typeof(SeriesCharacter).GetProperties(
                     BindingFlags.Instance | BindingFlags.Public))
        {
            property.GetSetMethod(nonPublic: true)?.Invoke(
                detachedCharacter,
                [property.GetValue(source)]);
        }

        return detachedCharacter;
    }

    private static void InvokeInternal(object target, string methodName, object?[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method.IsAssembly, $"{methodName} must remain narrowly internal.");
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void AssertSeriesPointerRejectedAtomically<TException>(
        StorySeries series,
        Action action)
        where TException : Exception
    {
        var before = (series.ActiveCastRevisionId, series.UpdatedAt, series.ConcurrencyStamp);
        Assert.Throws<TException>(action);
        Assert.Equal(before, (series.ActiveCastRevisionId, series.UpdatedAt, series.ConcurrencyStamp));
    }

    private static void AssertSeriesBookPointerRejectedAtomically<TException>(
        SeriesBook seriesBook,
        Action action)
        where TException : Exception
    {
        var before = seriesBook.ActiveNarrationJobId;
        Assert.Throws<TException>(action);
        Assert.Equal(before, seriesBook.ActiveNarrationJobId);
    }

    private static void AssertFailedBookAdditionDoesNotChangeAggregate(
        StorySeries series,
        SeriesCharacter character,
        SeriesBook membership,
        Action attemptedAddition)
    {
        var bookCount = series.Books.Count;
        var seriesStamp = series.ConcurrencyStamp;
        var seriesUpdatedAt = series.UpdatedAt;
        var characterStamp = character.ConcurrencyStamp;
        var characterUpdatedAt = character.UpdatedAt;
        var narratorProfile = (
            series.NarratorProvider,
            series.NarratorVoice,
            series.NarratorRate,
            series.NarratorPitch,
            series.NarratorVolume);
        var characterProfile = (
            character.VoiceProvider,
            character.Voice,
            character.Rate,
            character.Pitch,
            character.Volume);

        Assert.Throws<InvalidOperationException>(attemptedAddition);

        Assert.Equal(bookCount, series.Books.Count);
        Assert.Same(membership, Assert.Single(series.Books));
        Assert.Equal(seriesStamp, series.ConcurrencyStamp);
        Assert.Equal(seriesUpdatedAt, series.UpdatedAt);
        Assert.Equal(characterStamp, character.ConcurrencyStamp);
        Assert.Equal(characterUpdatedAt, character.UpdatedAt);
        Assert.Equal(narratorProfile, (
            series.NarratorProvider,
            series.NarratorVoice,
            series.NarratorRate,
            series.NarratorPitch,
            series.NarratorVolume));
        Assert.Equal(characterProfile, (
            character.VoiceProvider,
            character.Voice,
            character.Rate,
            character.Pitch,
            character.Volume));
    }
}
