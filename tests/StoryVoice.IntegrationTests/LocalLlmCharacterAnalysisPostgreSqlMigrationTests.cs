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
    private const string CurrentMigration = "20260814122000_AddLocalLlmCharacterAnalyses";
    private const string BeforeOwnerScopeMigration = "20260814170000_AddSeriesNarrativeVoiceMode";
    private const string OwnerScopeMigration = "20260816154216_EnforceLocalLlmAnalysisOwnerScope";

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

    [Fact]
    public async Task Owner_scope_migration_enforces_composite_foreign_keys_without_claiming_legacy_books()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();

        await using var db = CreateContext(connectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeOwnerScopeMigration, cancellationToken);

        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var targetBook = Book.Create(ownerA, "Target", "StoryVoice", "zh-TW", "target.txt");
        var contentBook = Book.Create(ownerA, "Content", "StoryVoice", "zh-TW", "content.txt");
        var retainedLegacyBook = Book.Create(ownerA, "Retained legacy", "StoryVoice", "zh-TW", "legacy.txt");
        db.Users.AddRange(CreateUser(ownerA, "owner-a"), CreateUser(ownerB, "owner-b"));
        db.Books.AddRange(targetBook, contentBook, retainedLegacyBook);
        await db.SaveChangesAsync(cancellationToken);
        await SetBookOwnerToNullAsync(connectionString, retainedLegacyBook.Id, cancellationToken);

        await migrator.MigrateAsync(OwnerScopeMigration, cancellationToken);
        Assert.Equal(1, await CountOwnerlessBookAsync(connectionString, retainedLegacyBook.Id, cancellationToken));
        Assert.Equal(2, await CountOwnerScopeForeignKeysAsync(connectionString, cancellationToken));

        await InsertAnalysisAsync(connectionString, ownerA, targetBook.Id, contentBook.Id, cancellationToken);
        var targetOwnerReassignment = await Assert.ThrowsAsync<PostgresException>(() =>
            SetBookOwnerAsync(connectionString, targetBook.Id, ownerB, cancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, targetOwnerReassignment.SqlState);
        Assert.Equal("FK_book_local_llm_analysis_target_owner_scope", targetOwnerReassignment.ConstraintName);

        var contentOwnerReassignment = await Assert.ThrowsAsync<PostgresException>(() =>
            SetBookOwnerAsync(connectionString, contentBook.Id, ownerB, cancellationToken));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, contentOwnerReassignment.SqlState);
        Assert.Equal("FK_book_local_llm_analysis_content_owner_scope", contentOwnerReassignment.ConstraintName);

        await migrator.MigrateAsync(BeforeOwnerScopeMigration, cancellationToken);
        await migrator.MigrateAsync(OwnerScopeMigration, cancellationToken);
        Assert.Equal(2, await CountOwnerScopeForeignKeysAsync(connectionString, cancellationToken));
    }

    private static ApplicationUser CreateUser(Guid id, string suffix) => new()
    {
        Id = id,
        UserName = $"owner-scope-{suffix}",
        NormalizedUserName = $"OWNER-SCOPE-{suffix.ToUpperInvariant()}",
        Email = $"owner-scope-{suffix}@example.invalid",
        NormalizedEmail = $"OWNER-SCOPE-{suffix.ToUpperInvariant()}@EXAMPLE.INVALID",
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };

    private static async Task SetBookOwnerToNullAsync(
        string connectionString,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE books SET \"OwnerId\" = NULL WHERE \"Id\" = @bookId",
            connection);
        command.Parameters.AddWithValue("bookId", bookId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
    }

    private static async Task<long> CountOwnerlessBookAsync(
        string connectionString,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM books WHERE \"Id\" = @bookId AND \"OwnerId\" IS NULL",
            connection);
        command.Parameters.AddWithValue("bookId", bookId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<long> CountOwnerScopeForeignKeysAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM pg_constraint
            WHERE conrelid = 'book_local_llm_character_analyses'::regclass
              AND contype = 'f'
              AND conname IN (
                  'FK_book_local_llm_analysis_target_owner_scope',
                  'FK_book_local_llm_analysis_content_owner_scope')
            """,
            connection);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task SetBookOwnerAsync(
        string connectionString,
        Guid bookId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE books SET \"OwnerId\" = @ownerId WHERE \"Id\" = @bookId",
            connection);
        command.Parameters.AddWithValue("bookId", bookId);
        command.Parameters.AddWithValue("ownerId", ownerId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
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
