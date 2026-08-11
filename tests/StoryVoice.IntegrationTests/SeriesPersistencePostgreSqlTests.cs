using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.Series;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryVoice.IntegrationTests;

public sealed class SeriesPersistencePostgreSqlTests
{
    private const string PreviousMigration = "20260810234952_AddNarrationModeCompatibility";

    [Fact]
    public async Task Repository_loads_complete_owner_scoped_aggregate_and_fresh_context_mutation_round_trips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var firstBook = Book.Create(ownerA, "Synthetic volume one", "Synthetic author", "en", "one.txt");
        var secondBook = Book.Create(ownerA, "Synthetic volume two", "Synthetic author", "en", "two.txt");
        var foreignBook = Book.Create(ownerB, "Foreign synthetic volume", "Synthetic author", "en", "foreign.txt");

        await using (var setup = CreateContext(connectionString))
        {
            await setup.Database.MigrateAsync(cancellationToken);
            setup.Users.AddRange(CreateUser(ownerA, "owner-a"), CreateUser(ownerB, "owner-b"));
            setup.Books.AddRange(firstBook, secondBook, foreignBook);
            await setup.SaveChangesAsync(cancellationToken);
        }

        var seriesA = CreateSeries(ownerA, "Synthetic series A");
        seriesA.AddBook(firstBook, "Volume one", 1);
        var alice = seriesA.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "provider-a", "voice-a", "rate-a", "pitch-a", "volume-a", "Synthetic notes");
        seriesA.AddAlias(alice.Id, "Captain");
        await using (var provider = CreateProvider(connectionString, ownerA))
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IStorySeriesRepository>();
            await repository.AddAsync(seriesA, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }

        var seriesB = CreateSeries(ownerB, "Synthetic series B");
        seriesB.AddBook(foreignBook, "Foreign volume", 1);
        seriesB.AddCharacter(
            "Bob", SeriesCharacterRole.Main, "provider-b", "voice-b", "rate-b", "pitch-b", "volume-b", null);
        await using (var provider = CreateProvider(connectionString, ownerB))
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IStorySeriesRepository>();
            await repository.AddAsync(seriesB, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using (var provider = CreateProvider(connectionString, ownerA))
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IStorySeriesRepository>();
            Assert.Null(await repository.GetForMutationAsync(seriesB.Id, cancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.AddAsync(CreateSeries(ownerB, "Wrong owner aggregate"), cancellationToken));

            var loaded = Assert.IsType<StorySeries>(
                await repository.GetForMutationAsync(seriesA.Id, cancellationToken));
            Assert.Single(loaded.Books);
            Assert.Single(loaded.Characters);
            Assert.Equal(2, loaded.IdentityKeys.Count);
            Assert.Throws<InvalidOperationException>(() => loaded.AddAlias(alice.Id, " captain "));

            var db = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
            var trackedSecondBook = await db.Books.SingleAsync(
                book => book.Id == secondBook.Id,
                cancellationToken);
            var secondMembership = loaded.AddBook(trackedSecondBook, "Volume two", 2);
            loaded.AddAlias(alice.Id, "Navigator");
            Assert.Equal(2, secondMembership.MembershipRevision);
            await repository.SaveChangesAsync(cancellationToken);
        }

        await using (var rootOnlyContext = CreateContext(connectionString))
        {
            var rootOnly = await rootOnlyContext.StorySeries.SingleAsync(
                series => series.Id == seriesA.Id,
                cancellationToken);
            var exception = Assert.Throws<InvalidOperationException>(() => rootOnly.AddCharacter(
                "Root-only mutation",
                SeriesCharacterRole.Minor,
                "provider-a",
                "voice-c",
                "rate-a",
                "pitch-a",
                "volume-a",
                null));
            Assert.Contains("complete mutation aggregate", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var provider = CreateProvider(connectionString, ownerA))
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IStorySeriesRepository>();
            var reloaded = Assert.IsType<StorySeries>(
                await repository.GetForMutationAsync(seriesA.Id, cancellationToken));
            Assert.Equal(2, reloaded.Books.Count);
            Assert.Equal(new[] { 1, 2 }, reloaded.Books.OrderBy(book => book.SortOrder).Select(book => book.MembershipRevision));
            var reloadedCharacter = Assert.Single(reloaded.Characters);
            Assert.Equal(alice.CanonicalIdentityKeyId, reloadedCharacter.CanonicalIdentityKeyId);
            Assert.Equal(3, reloaded.IdentityKeys.Count);
            Assert.Contains(reloaded.IdentityKeys, key => key.NormalizedValue == "NAVIGATOR");
        }
    }

    [Fact]
    public async Task PostgreSql_rejects_namespace_duplicates_membership_duplicates_and_owner_series_pollution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var firstBook = Book.Create(ownerA, "Synthetic first book", "Synthetic author", "en", "first.txt");
        var secondBook = Book.Create(ownerA, "Synthetic second book", "Synthetic author", "en", "second.txt");
        var foreignBook = Book.Create(ownerB, "Synthetic foreign book", "Synthetic author", "en", "foreign.txt");

        var seriesA = CreateSeries(ownerA, "Synthetic series A");
        var firstMembership = seriesA.AddBook(firstBook, "Volume one", 1);
        var alice = seriesA.AddCharacter(
            "Alice", SeriesCharacterRole.Main, "provider-a", "voice-a", "rate-a", "pitch-a", "volume-a", null);
        var aliceAlias = seriesA.AddAlias(alice.Id, "Captain");
        var bob = seriesA.AddCharacter(
            "Bob", SeriesCharacterRole.Supporting, "provider-a", "voice-b", "rate-b", "pitch-b", "volume-b", null);
        var bobCanonical = seriesA.IdentityKeys.Single(key => key.Id == bob.CanonicalIdentityKeyId);

        var otherSeries = CreateSeries(ownerA, "Synthetic series A2");
        var otherCharacter = otherSeries.AddCharacter(
            "Carol", SeriesCharacterRole.Main, "provider-a", "voice-c", "rate-c", "pitch-c", "volume-c", null);
        var otherCanonical = otherSeries.IdentityKeys.Single(key => key.Id == otherCharacter.CanonicalIdentityKeyId);

        var foreignSeries = CreateSeries(ownerB, "Synthetic series B");
        var foreignCharacter = foreignSeries.AddCharacter(
            "Dana", SeriesCharacterRole.Main, "provider-b", "voice-d", "rate-d", "pitch-d", "volume-d", null);
        var foreignCanonical = foreignSeries.IdentityKeys.Single(key => key.Id == foreignCharacter.CanonicalIdentityKeyId);

        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync(cancellationToken);
        db.Users.AddRange(CreateUser(ownerA, "constraint-owner-a"), CreateUser(ownerB, "constraint-owner-b"));
        db.Books.AddRange(firstBook, secondBook, foreignBook);
        await db.SaveChangesAsync(cancellationToken);
        db.StorySeries.AddRange(seriesA, otherSeries, foreignSeries);
        await db.SaveChangesAsync(cancellationToken);

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO series_character_identity_keys
                    ("Id", "OwnerId", "SeriesId", "CharacterId", "Kind", "Value", "NormalizedValue")
                VALUES
                    ({Guid.NewGuid()}, {ownerA}, {seriesA.Id}, {alice.Id}, 'Alias', 'alice', 'ALICE');
                """, cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "UX_char_keys_series_value");

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO series_books
                    ("Id", "OwnerId", "SeriesId", "BookId", "VolumeLabel", "SortOrder", "MembershipRevision", "ActiveNarrationJobId")
                VALUES
                    ({Guid.NewGuid()}, {ownerA}, {seriesA.Id}, {firstBook.Id}, 'Duplicate book', 2, 2, NULL);
                """, cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "UX_series_books_owner_book");

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO series_books
                    ("Id", "OwnerId", "SeriesId", "BookId", "VolumeLabel", "SortOrder", "MembershipRevision", "ActiveNarrationJobId")
                VALUES
                    ({Guid.NewGuid()}, {ownerA}, {otherSeries.Id}, {firstBook.Id}, 'Cross-series duplicate book', 1, 1, NULL);
                """, cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "UX_series_books_owner_book");

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO series_books
                    ("Id", "OwnerId", "SeriesId", "BookId", "VolumeLabel", "SortOrder", "MembershipRevision", "ActiveNarrationJobId")
                VALUES
                    ({Guid.NewGuid()}, {ownerA}, {seriesA.Id}, {secondBook.Id}, 'Duplicate sort', {firstMembership.SortOrder}, 2, NULL);
                """, cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "IX_series_books_OwnerId_SeriesId_SortOrder");

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO series_books
                    ("Id", "OwnerId", "SeriesId", "BookId", "VolumeLabel", "SortOrder", "MembershipRevision", "ActiveNarrationJobId")
                VALUES
                    ({Guid.NewGuid()}, {ownerA}, {seriesA.Id}, {foreignBook.Id}, 'Cross owner', 2, 2, NULL);
                """, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_series_books_books_OwnerId_BookId");

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO series_character_identity_keys
                    ("Id", "OwnerId", "SeriesId", "CharacterId", "Kind", "Value", "NormalizedValue")
                VALUES
                    ({Guid.NewGuid()}, {ownerA}, {seriesA.Id}, {otherCharacter.Id}, 'Alias', 'Pollution', 'POLLUTION');
                """, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_char_keys_character_scope");

        await AssertCanonicalPointerRejectedAsync(db, alice.Id, aliceAlias.Id, cancellationToken);
        await AssertCanonicalPointerRejectedAsync(db, alice.Id, bobCanonical.Id, cancellationToken);
        await AssertCanonicalPointerRejectedAsync(db, alice.Id, otherCanonical.Id, cancellationToken);
        await AssertCanonicalPointerRejectedAsync(db, alice.Id, foreignCanonical.Id, cancellationToken);
    }

    [Fact]
    public async Task Add_series_cast_down_fails_closed_with_phase_b_rows_and_is_reversible_when_empty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        var ownerId = Guid.NewGuid();

        await using (var db = CreateContext(connectionString))
        {
            await db.Database.MigrateAsync(cancellationToken);
            db.Users.Add(CreateUser(ownerId, "down-guard-owner"));
            db.StorySeries.Add(CreateSeries(ownerId, "Synthetic rollback guard"));
            await db.SaveChangesAsync(cancellationToken);

            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken));
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
            Assert.Contains("Phase-B series rows exist", exception.MessageText, StringComparison.Ordinal);
        }

        Assert.True(await TableExistsAsync(connectionString, "story_series", cancellationToken));

        await using (var cleanup = CreateContext(connectionString))
        {
            await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM story_series", cancellationToken);
            await cleanup.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
        }

        Assert.False(await TableExistsAsync(connectionString, "story_series", cancellationToken));

        await using (var reupgrade = CreateContext(connectionString))
        {
            await reupgrade.Database.MigrateAsync(cancellationToken);
        }

        Assert.True(await TableExistsAsync(connectionString, "story_series", cancellationToken));
    }

    private static StorySeries CreateSeries(Guid ownerId, string name) =>
        StorySeries.Create(
            ownerId,
            name,
            "provider-narrator",
            "voice-narrator",
            "rate-narrator",
            "pitch-narrator",
            "volume-narrator",
            350);

    private static ApplicationUser CreateUser(Guid id, string name) =>
        new()
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.invalid",
            NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.INVALID",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

    private static StoryVoiceDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new StoryVoiceDbContext(options);
    }

    private static ServiceProvider CreateProvider(string connectionString, Guid ownerId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ICurrentUser>(_ => new FixedCurrentUser(ownerId));
        services.AddStoryVoiceInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static async Task AssertCanonicalPointerRejectedAsync(
        StoryVoiceDbContext db,
        Guid characterId,
        Guid identityKeyId,
        CancellationToken cancellationToken) =>
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE series_characters
                SET "CanonicalIdentityKeyId" = {identityKeyId}
                WHERE "Id" = {characterId};
                """, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_series_characters_canonical_identity_key");

    private static async Task AssertPostgresErrorAsync(
        Func<Task<int>> action,
        string sqlState,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    private static async Task<bool> TableExistsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass(@tableName) IS NOT NULL", connection);
        command.Parameters.AddWithValue("tableName", $"public.{tableName}");
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed record FixedCurrentUser(Guid UserId) : ICurrentUser;
}
