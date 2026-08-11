using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class SeriesPersistenceModelTests
{
    [Fact]
    public void Npgsql_model_has_bounded_series_storage_and_no_required_ef_canonical_cycle()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var series = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(StorySeries)));
        var bookMembership = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(SeriesBook)));
        var character = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(SeriesCharacter)));
        var identityKey = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(SeriesCharacterIdentityKey)));
        var book = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(Book)));

        AssertProperty(series, nameof(StorySeries.Name), 200, nullable: false);
        AssertProperty(series, nameof(StorySeries.NormalizedName), 200, nullable: false);
        AssertProperty(series, nameof(StorySeries.NarratorProvider), 50, nullable: false);
        AssertProperty(series, nameof(StorySeries.NarratorVoice), 200, nullable: false);
        AssertProperty(series, nameof(StorySeries.NarratorRate), 50, nullable: false);
        AssertProperty(series, nameof(StorySeries.NarratorPitch), 50, nullable: false);
        AssertProperty(series, nameof(StorySeries.NarratorVolume), 50, nullable: false);
        AssertProperty(bookMembership, nameof(SeriesBook.VolumeLabel), 100, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.CanonicalName), 200, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.NormalizedName), 200, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.VoiceProvider), 50, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.Voice), 200, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.Rate), 50, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.Pitch), 50, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.Volume), 50, nullable: false);
        AssertProperty(character, nameof(SeriesCharacter.Notes), 2_000, nullable: true);
        AssertProperty(identityKey, nameof(SeriesCharacterIdentityKey.Value), 200, nullable: false);
        AssertProperty(identityKey, nameof(SeriesCharacterIdentityKey.NormalizedValue), 200, nullable: false);

        var canonicalKind = Assert.IsAssignableFrom<IProperty>(
            character.FindProperty("CanonicalIdentityKeyKind"));
        Assert.Equal("'Canonical'", canonicalKind.GetComputedColumnSql());
        Assert.True(canonicalKind.GetIsStored());
        Assert.DoesNotContain(
            character.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(
                property => property.Name == nameof(SeriesCharacter.CanonicalIdentityKeyId)));

        var identityOwnerRelationship = Assert.Single(
            identityKey.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(SeriesCharacter));
        Assert.True(identityOwnerRelationship.IsRequired);
        Assert.Equal(
            new[] { nameof(SeriesCharacterIdentityKey.OwnerId), nameof(SeriesCharacterIdentityKey.SeriesId), nameof(SeriesCharacterIdentityKey.CharacterId) },
            identityOwnerRelationship.Properties.Select(property => property.Name));

        Assert.Contains(
            series.GetKeys(),
            key => key.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(StorySeries.OwnerId), nameof(StorySeries.Id) }));
        Assert.Contains(
            character.GetKeys(),
            key => key.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(SeriesCharacter.OwnerId), nameof(SeriesCharacter.SeriesId), nameof(SeriesCharacter.Id) }));
        Assert.Contains(
            book.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Book.OwnerId), nameof(Book.Id) }));
    }

    [Fact]
    public void Add_series_cast_is_newest_has_no_pending_model_changes_and_generates_idempotent_sql()
    {
        using var db = CreateContext();
        var migrations = db.Database.GetMigrations().ToArray();

        Assert.EndsWith("_AddSeriesCast", migrations[^1], StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());

        var script = db.GetService<IMigrator>().GenerateScript(
            fromMigration: null,
            toMigration: null,
            MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE story_series", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE series_books", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE series_characters", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE series_character_identity_keys", script, StringComparison.Ordinal);
        Assert.Contains("FK_series_characters_canonical_identity_key", script, StringComparison.Ordinal);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", script, StringComparison.Ordinal);
        Assert.Contains("FK_series_books_books_OwnerId_BookId", script, StringComparison.Ordinal);
        Assert.Contains("DO $EF$", script, StringComparison.Ordinal);
    }

    private static StoryVoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=storyvoice_series_model;Username=storyvoice;Password=unused")
            .Options;
        return new StoryVoiceDbContext(options);
    }

    private static void AssertProperty(
        IEntityType entity,
        string propertyName,
        int maximumLength,
        bool nullable)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(propertyName));
        Assert.Equal(maximumLength, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
    }
}
