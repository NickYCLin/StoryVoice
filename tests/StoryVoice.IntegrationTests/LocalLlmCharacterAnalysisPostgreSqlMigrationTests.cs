using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Insights;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryVoice.IntegrationTests;

public sealed class LocalLlmCharacterAnalysisPostgreSqlMigrationTests
{
    private const string PreviousMigration = "20260814094000_AllowPendingSeriesBookOnboarding";
    private const string CurrentMigration = "20260814100000_AddLocalLlmCharacterAnalyses";

    [Fact]
    public async Task Local_LLM_analysis_migration_expands_safely_and_blocks_data_losing_down()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();

        await using var db = CreateContext(connectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);

        var ownerId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = ownerId,
            UserName = "local-llm-migration-proof",
            NormalizedUserName = "LOCAL-LLM-MIGRATION-PROOF",
            Email = "local-llm-migration-proof@example.invalid",
            NormalizedEmail = "LOCAL-LLM-MIGRATION-PROOF@EXAMPLE.INVALID",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        var book = Book.Create(ownerId, "Local LLM migration proof", "StoryVoice", "zh-TW", "proof.txt");
        db.Users.Add(user);
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);

        await migrator.MigrateAsync(CurrentMigration, cancellationToken);
        Assert.True(await AnalysisTableExistsAsync(connectionString, cancellationToken));

        var crossOwnerWrite = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAnalysisAsync(connectionString, Guid.NewGuid(), book.Id, book.Id, cancellationToken));
        Assert.Equal(PostgresErrorCodes.RaiseException, crossOwnerWrite.SqlState);
        Assert.Contains("owner does not match", crossOwnerWrite.MessageText, StringComparison.Ordinal);

        var analysis = BookLocalLlmCharacterAnalysis.Create(
            ownerId,
            book.Id,
            book.Id,
            "fake-local-llm",
            "v1-full-chapter-context",
            "synthetic-source-hash",
            "[]");
        db.BookLocalLlmCharacterAnalyses.Add(analysis);
        await db.SaveChangesAsync(cancellationToken);

        var blockedDown = await Assert.ThrowsAsync<PostgresException>(() =>
            migrator.MigrateAsync(PreviousMigration, cancellationToken));
        Assert.Equal(PostgresErrorCodes.RaiseException, blockedDown.SqlState);
        Assert.Contains("Cannot revert AddLocalLlmCharacterAnalyses", blockedDown.MessageText, StringComparison.Ordinal);
        Assert.True(await AnalysisTableExistsAsync(connectionString, cancellationToken));

        db.BookLocalLlmCharacterAnalyses.Remove(analysis);
        await db.SaveChangesAsync(cancellationToken);
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        Assert.False(await AnalysisTableExistsAsync(connectionString, cancellationToken));

        await migrator.MigrateAsync(CurrentMigration, cancellationToken);
        Assert.True(await AnalysisTableExistsAsync(connectionString, cancellationToken));
    }

    private static StoryVoiceDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static async Task InsertAnalysisAsync(
        string connectionString,
        Guid ownerId,
        Guid bookId,
        Guid contentBookId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO book_local_llm_character_analyses
                ("BookId", "OwnerId", "ContentBookId", "Generator", "Model", "PromptVersion", "SourceHash", "CandidatesJson", "GeneratedAt")
            VALUES
                (@bookId, @ownerId, @contentBookId, 'test', 'test', 'test', 'test', '[]'::jsonb, now())
            """,
            connection);
        command.Parameters.AddWithValue("bookId", bookId);
        command.Parameters.AddWithValue("ownerId", ownerId);
        command.Parameters.AddWithValue("contentBookId", contentBookId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> AnalysisTableExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                    AND table_name = 'book_local_llm_character_analyses')
            """,
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
