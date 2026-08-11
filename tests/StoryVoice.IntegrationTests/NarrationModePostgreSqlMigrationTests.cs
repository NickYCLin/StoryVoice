using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using StoryVoice.Domain.Books;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryVoice.IntegrationTests;

public sealed class NarrationModePostgreSqlMigrationTests
{
    private const string PreviousMigration = "20260808162001_AddNarrationJobs";
    private const string CompatibilityMigration = "20260810234952_AddNarrationModeCompatibility";

    [Fact]
    public async Task Phase_a_migration_upgrades_legacy_rows_enforces_contract_and_blocks_unsafe_down()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .Build();
        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var db = new StoryVoiceDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);

        var ownerId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = ownerId,
            UserName = "mode-migration-proof",
            NormalizedUserName = "MODE-MIGRATION-PROOF",
            Email = "mode-migration-proof@example.invalid",
            NormalizedEmail = "MODE-MIGRATION-PROOF@EXAMPLE.INVALID",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
        var book = Book.Create(ownerId, "Migration proof", "Proof", "zh-TW", "proof.txt");
        db.Users.Add(user);
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);

        var legacyJobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertLegacyJobAsync(db, legacyJobId, ownerId, book.Id, now, cancellationToken);

        await migrator.MigrateAsync(CompatibilityMigration, cancellationToken);

        Assert.Equal(
            "SingleVoice",
            await ReadModeAsync(postgres.GetConnectionString(), legacyJobId, cancellationToken));

        var defaultedJobId = Guid.NewGuid();
        await InsertClonedJobAsync(
            db,
            legacyJobId,
            defaultedJobId,
            includeMode: false,
            sourceHashSuffix: "-default",
            cancellationToken: cancellationToken);
        Assert.Equal(
            "SingleVoice",
            await ReadModeAsync(postgres.GetConnectionString(), defaultedJobId, cancellationToken));

        var multiCharacterJobId = Guid.NewGuid();
        await InsertClonedJobAsync(
            db,
            legacyJobId,
            multiCharacterJobId,
            includeMode: true,
            mode: "MultiCharacter",
            cancellationToken: cancellationToken);

        var duplicateSingleVoice = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertClonedJobAsync(
                db,
                legacyJobId,
                Guid.NewGuid(),
                includeMode: true,
                mode: "SingleVoice",
                cancellationToken: cancellationToken));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateSingleVoice.SqlState);

        var invalidMode = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"Mode\" = 'InvalidMode' WHERE \"Id\" = {legacyJobId}",
                cancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidMode.SqlState);

        var unsafeDown = await Assert.ThrowsAsync<PostgresException>(() =>
            migrator.MigrateAsync(PreviousMigration, cancellationToken));
        Assert.Equal(PostgresErrorCodes.RaiseException, unsafeDown.SqlState);
        Assert.Contains("non-SingleVoice jobs exist", unsafeDown.MessageText, StringComparison.Ordinal);
        Assert.True(await ModeColumnExistsAsync(postgres.GetConnectionString(), cancellationToken));
        Assert.Equal(
            "SingleVoice",
            await ReadModeAsync(postgres.GetConnectionString(), legacyJobId, cancellationToken));

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM narration_jobs WHERE \"Id\" = {multiCharacterJobId}",
            cancellationToken);
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        Assert.False(await ModeColumnExistsAsync(postgres.GetConnectionString(), cancellationToken));
        Assert.Equal(2, await CountJobsAsync(postgres.GetConnectionString(), cancellationToken));

        await migrator.MigrateAsync(CompatibilityMigration, cancellationToken);
        Assert.True(await ModeColumnExistsAsync(postgres.GetConnectionString(), cancellationToken));
        Assert.Equal(
            2,
            await CountModeAsync(postgres.GetConnectionString(), "SingleVoice", cancellationToken));
    }

    private static Task<int> InsertLegacyJobAsync(
        StoryVoiceDbContext db,
        Guid jobId,
        Guid ownerId,
        Guid bookId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO narration_jobs
                ("Id", "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate",
                 "Status", "ProgressPercent", "Attempts", "CancellationRequested", "LeaseOwner",
                 "LeaseExpiresAt", "NextAttemptAt", "ErrorCode", "AudioRelativePath", "AudioBytes",
                 "RightsAttestedAt", "CreatedAt", "UpdatedAt", "CompletedAt", "ConcurrencyStamp")
            VALUES
                ({jobId}, {ownerId}, {bookId}, {bookId}, 'migration-proof-source',
                 'zh-TW-YunJheNeural', '-5%', 'Queued', 0, 0, FALSE, NULL, NULL, {now},
                 NULL, NULL, NULL, {now}, {now}, {now}, NULL, {Guid.NewGuid()});
            """,
            cancellationToken);

    private static Task<int> InsertClonedJobAsync(
        StoryVoiceDbContext db,
        Guid sourceJobId,
        Guid newJobId,
        bool includeMode,
        string? mode = null,
        string sourceHashSuffix = "",
        CancellationToken cancellationToken = default)
    {
        if (includeMode)
        {
            return db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO narration_jobs
                    ("Id", "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate", "Mode",
                     "Status", "ProgressPercent", "Attempts", "CancellationRequested", "LeaseOwner",
                     "LeaseExpiresAt", "NextAttemptAt", "ErrorCode", "AudioRelativePath", "AudioBytes",
                     "RightsAttestedAt", "CreatedAt", "UpdatedAt", "CompletedAt", "ConcurrencyStamp")
                SELECT {newJobId}, "OwnerId", "BookId", "ContentBookId", "SourceHash" || {sourceHashSuffix},
                       "Voice", "Rate", {mode}, "Status", "ProgressPercent", "Attempts",
                       "CancellationRequested", "LeaseOwner", "LeaseExpiresAt", "NextAttemptAt",
                       "ErrorCode", "AudioRelativePath", "AudioBytes", "RightsAttestedAt", "CreatedAt",
                       "UpdatedAt", "CompletedAt", {Guid.NewGuid()}
                FROM narration_jobs WHERE "Id" = {sourceJobId};
                """,
                cancellationToken);
        }

        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO narration_jobs
                ("Id", "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate",
                 "Status", "ProgressPercent", "Attempts", "CancellationRequested", "LeaseOwner",
                 "LeaseExpiresAt", "NextAttemptAt", "ErrorCode", "AudioRelativePath", "AudioBytes",
                 "RightsAttestedAt", "CreatedAt", "UpdatedAt", "CompletedAt", "ConcurrencyStamp")
            SELECT {newJobId}, "OwnerId", "BookId", "ContentBookId", "SourceHash" || {sourceHashSuffix},
                   "Voice", "Rate", "Status", "ProgressPercent", "Attempts", "CancellationRequested",
                   "LeaseOwner", "LeaseExpiresAt", "NextAttemptAt", "ErrorCode", "AudioRelativePath",
                   "AudioBytes", "RightsAttestedAt", "CreatedAt", "UpdatedAt", "CompletedAt", {Guid.NewGuid()}
            FROM narration_jobs WHERE "Id" = {sourceJobId};
            """,
            cancellationToken);
    }

    private static async Task<string?> ReadModeAsync(
        string connectionString,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT \"Mode\" FROM narration_jobs WHERE \"Id\" = @jobId",
            connection);
        command.Parameters.AddWithValue("jobId", jobId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<bool> ModeColumnExistsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='narration_jobs' AND column_name='Mode')",
            connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> CountJobsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT COUNT(*)::int FROM narration_jobs", connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> CountModeAsync(
        string connectionString,
        string mode,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM narration_jobs WHERE \"Mode\" = @mode",
            connection);
        command.Parameters.AddWithValue("mode", mode);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
