using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryVoice.Domain.Collections;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class CollectionsPersistenceModelTests
{
    [Fact]
    public void Npgsql_model_has_bounded_collection_storage_and_owner_scoped_keys()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var collection = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(BookCollection)));
        var membership = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(BookCollectionBook)));
        var share = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(CollectionShare)));

        AssertProperty(collection, nameof(BookCollection.Name), 200, nullable: false);
        AssertProperty(collection, nameof(BookCollection.NormalizedName), 200, nullable: false);
        AssertProperty(collection, nameof(BookCollection.Description), 2_000, nullable: true);
        AssertProperty(membership, nameof(BookCollectionBook.VolumeLabel), 100, nullable: true);
        AssertProperty(share, nameof(CollectionShare.GranteeEmail), 320, nullable: false);

        Assert.Contains(
            collection.GetKeys(),
            key => key.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(BookCollection.OwnerId), nameof(BookCollection.Id) }));
        Assert.Contains(
            collection.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(BookCollection.OwnerId), nameof(BookCollection.NormalizedName) }));
        Assert.Contains(
            membership.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(BookCollectionBook.OwnerId),
                    nameof(BookCollectionBook.CollectionId),
                    nameof(BookCollectionBook.BookId)
                }));
        Assert.Contains(
            membership.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(BookCollectionBook.OwnerId),
                    nameof(BookCollectionBook.CollectionId),
                    nameof(BookCollectionBook.SortOrder)
                }));
        Assert.Contains(
            share.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(CollectionShare.CollectionId), nameof(CollectionShare.GranteeUserId) }));
    }

    [Fact]
    public void Add_book_collections_migration_matches_model_and_scopes_shares_to_grantee()
    {
        using var db = CreateContext();
        Assert.False(db.Database.HasPendingModelChanges());

        var script = db.GetService<IMigrator>().GenerateScript(
            fromMigration: null,
            toMigration: null,
            MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE book_collections", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE book_collection_books", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE collection_shares", script, StringComparison.Ordinal);
        Assert.Contains("FK_book_collection_books_books_OwnerId_BookId", script, StringComparison.Ordinal);
        Assert.Contains("UX_collection_shares_collection_grantee", script, StringComparison.Ordinal);
    }

    private static StoryVoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=storyvoice_collections_model;Username=storyvoice;Password=unused")
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
