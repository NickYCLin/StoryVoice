using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using StoryVoice.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryVoice.IntegrationTests;

public sealed class BookArchivalPostgreSqlMigrationTests
{
    private const string PreviousMigration = "20260812093225_AddCharacterStatusAndAudioDuration";
    private const string CurrentMigration = "20260813040102_AddBookArchival";

    [Fact]
    public async Task Archival_migration_defaults_existing_books_and_blocks_down_while_archived_rows_exist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        var bookId = Guid.NewGuid();

        await using var db = CreateContext(connectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO books
                ("Id", "Title", "Author", "Language", "OriginalFileName", "FileType", "Status", "CreatedAt")
            VALUES
                ({bookId}, 'archive migration probe', 'StoryVoice', 'en', 'archive-probe.txt', 'txt', 'Uploaded', {DateTimeOffset.UtcNow});
            """, cancellationToken);

        await migrator.MigrateAsync(CurrentMigration, cancellationToken);
        Assert.False(await ReadArchivedAsync(connectionString, bookId, cancellationToken));

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE books SET \"IsArchived\" = TRUE WHERE \"Id\" = {bookId}",
            cancellationToken);
        var blockedDown = await Assert.ThrowsAsync<PostgresException>(() =>
            migrator.MigrateAsync(PreviousMigration, cancellationToken));
        Assert.Equal(PostgresErrorCodes.RaiseException, blockedDown.SqlState);
        Assert.Contains("Cannot roll back book archival", blockedDown.MessageText, StringComparison.Ordinal);
        Assert.True(await IsArchivedColumnPresentAsync(connectionString, cancellationToken));

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE books SET \"IsArchived\" = FALSE WHERE \"Id\" = {bookId}",
            cancellationToken);
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        Assert.False(await IsArchivedColumnPresentAsync(connectionString, cancellationToken));
    }

    private static StoryVoiceDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static async Task<bool> ReadArchivedAsync(
        string connectionString,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT \"IsArchived\" FROM books WHERE \"Id\" = @bookId",
            connection);
        command.Parameters.AddWithValue("bookId", bookId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> IsArchivedColumnPresentAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                    AND table_name = 'books'
                    AND column_name = 'IsArchived')
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
