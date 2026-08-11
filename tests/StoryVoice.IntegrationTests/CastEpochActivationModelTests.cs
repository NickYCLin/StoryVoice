using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class CastEpochActivationModelTests
{
    private const string PreviousMigration = "20260811040925_AddCastRebuildPersistence";

    [Fact]
    public void Model_has_one_batch_per_draft_pointer_lookup_indexes_and_no_pointer_navigations()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var batch = Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(SeriesCastRebuildBatch)));

        var draftIndex = Assert.Single(
            batch.GetIndexes(),
            index => index.GetDatabaseName() == "UX_rebuild_batches_draft_cast");
        Assert.True(draftIndex.IsUnique);
        Assert.Equal(
            [
                nameof(SeriesCastRebuildBatch.OwnerId),
                nameof(SeriesCastRebuildBatch.SeriesId),
                nameof(SeriesCastRebuildBatch.DraftCastRevisionId)
            ],
            draftIndex.Properties.Select(property => property.Name));
        Assert.DoesNotContain(
            batch.GetIndexes(),
            index => index.GetDatabaseName() == "IX_rebuild_batches_draft_cast");

        var series = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(StoryVoice.Domain.Series.StorySeries)));
        var seriesBook = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(StoryVoice.Domain.Series.SeriesBook)));
        var member = Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(SeriesCastRebuildMember)));
        var activeJobIndex = Assert.Single(
            seriesBook.GetIndexes(),
            index => index.GetDatabaseName() == "IX_series_books_active_job");
        Assert.Equal(
            [nameof(StoryVoice.Domain.Series.SeriesBook.ActiveNarrationJobId)],
            activeJobIndex.Properties.Select(property => property.Name));
        Assert.Equal("\"ActiveNarrationJobId\" IS NOT NULL", activeJobIndex.GetFilter());
        var previousJobIndex = Assert.Single(
            member.GetIndexes(),
            index => index.GetDatabaseName() == "IX_rebuild_members_previous_job");
        Assert.Equal(
            [nameof(SeriesCastRebuildMember.PreviousActiveNarrationJobId)],
            previousJobIndex.Properties.Select(property => property.Name));
        Assert.Equal("\"PreviousActiveNarrationJobId\" IS NOT NULL", previousJobIndex.GetFilter());
        Assert.DoesNotContain(
            series.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(
                property => property.Name == nameof(StoryVoice.Domain.Series.StorySeries.ActiveCastRevisionId)));
        Assert.DoesNotContain(
            seriesBook.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(
                property => property.Name == nameof(StoryVoice.Domain.Series.SeriesBook.ActiveNarrationJobId)));
    }

    [Fact]
    public void Additive_migration_contains_deferred_fences_full_trigger_set_and_first_down_guard()
    {
        using var db = CreateContext();
        var migrations = db.Database.GetMigrations().ToArray();
        var previous = Assert.Single(migrations, migration => migration == PreviousMigration);
        var current = Assert.Single(
            migrations,
            migration => migration.EndsWith("_AddAtomicCastEpochActivation", StringComparison.Ordinal));
        Assert.True(Array.IndexOf(migrations, current) > Array.IndexOf(migrations, previous));
        Assert.False(db.Database.HasPendingModelChanges());

        var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript(previous, current);
        Assert.Contains("CREATE UNIQUE INDEX \"UX_rebuild_batches_draft_cast\"", up, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX \"IX_series_books_active_job\" ON series_books (\"ActiveNarrationJobId\") WHERE \"ActiveNarrationJobId\" IS NOT NULL",
            up,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX \"IX_rebuild_members_previous_job\" ON series_cast_rebuild_members (\"PreviousActiveNarrationJobId\") WHERE \"PreviousActiveNarrationJobId\" IS NOT NULL",
            up,
            StringComparison.Ordinal);
        Assert.Contains("FK_story_series_active_cast", up, StringComparison.Ordinal);
        Assert.Contains("FK_series_books_active_job", up, StringComparison.Ordinal);
        Assert.Equal(2, Count(up, "DEFERRABLE INITIALLY DEFERRED;", StringComparison.Ordinal));
        Assert.Contains("CREATE FUNCTION is_safe_narration_audio", up, StringComparison.Ordinal);
        Assert.Contains("CREATE FUNCTION assert_cast_epoch_integrity", up, StringComparison.Ordinal);
        Assert.Contains("CREATE FUNCTION check_cast_epoch_integrity", up, StringComparison.Ordinal);
        foreach (var trigger in TriggerNames)
        {
            Assert.Contains($"CREATE CONSTRAINT TRIGGER \"{trigger}\"", up, StringComparison.Ordinal);
        }

        Assert.Contains("AFTER INSERT OR DELETE ON \"narration_jobs\"", up, StringComparison.Ordinal);
        const string updateMarker = "AFTER UPDATE OF";
        var updateMarkerIndex = up.IndexOf(updateMarker, StringComparison.Ordinal);
        Assert.True(updateMarkerIndex >= 0);
        var updateColumnsStart = updateMarkerIndex + updateMarker.Length;
        var updateColumnsEnd = up.IndexOf("ON \"narration_jobs\"", updateColumnsStart, StringComparison.Ordinal);
        Assert.True(updateColumnsEnd > updateColumnsStart);
        var updateColumns = up[updateColumnsStart..updateColumnsEnd]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(column => column.Trim().Trim('"'))
            .ToArray();
        Assert.Equal(
            [
                "Id",
                "OwnerId",
                "BookId",
                "SeriesId",
                "CastRevisionId",
                "RebuildBatchId",
                "RebuildMemberId",
                "Mode",
                "Status",
                "Visibility",
                "AudioRelativePath",
                "AudioBytes"
            ],
            updateColumns);
        Assert.DoesNotContain(
            updateColumns,
            column => column is "ProgressPercent"
                or "LeaseOwner"
                or "LeaseExpiresAt"
                or "UpdatedAt"
                or "ConcurrencyStamp");
        Assert.DoesNotContain(
            "AFTER INSERT OR UPDATE OR DELETE ON \"narration_jobs\"",
            up,
            StringComparison.Ordinal);

        foreach (var constraint in ConstraintNames)
        {
            Assert.Contains(constraint, up, StringComparison.Ordinal);
        }

        Assert.Contains("PERFORM assert_cast_epoch_integrity", up, StringComparison.Ordinal);
        Assert.Contains("series_books", up, StringComparison.Ordinal);
        Assert.Contains("PreviousActiveNarrationJobId", up, StringComparison.Ordinal);

        var down = migrator.GenerateScript(current, previous);
        var guard = down.IndexOf(
            "Cannot roll back atomic cast epoch activation while active pointers or activated artifacts exist.",
            StringComparison.Ordinal);
        var firstDrop = down.IndexOf("DROP ", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        Assert.True(firstDrop > guard);
        Assert.Contains("CREATE INDEX \"IX_rebuild_batches_draft_cast\"", down, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX \"IX_series_books_active_job\"", down, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX \"IX_rebuild_members_previous_job\"", down, StringComparison.Ordinal);
        Assert.Contains("CREATE FUNCTION check_rebuild_artifact_visibility()", down, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER \"CT_rebuild_artifact_member\"", down, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER \"CT_rebuild_artifact_job\"", down, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER \"CT_rebuild_artifact_batch\"", down, StringComparison.Ordinal);

        var idempotent = migrator.GenerateScript(null, null, MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains(current, idempotent, StringComparison.Ordinal);
        Assert.Contains("FK_story_series_active_cast", idempotent, StringComparison.Ordinal);
        Assert.Contains("FK_series_books_active_job", idempotent, StringComparison.Ordinal);
        Assert.Contains("assert_cast_epoch_integrity", idempotent, StringComparison.Ordinal);

        var names = TriggerNames.Concat(ConstraintNames).Concat(
        [
            "FK_story_series_active_cast",
            "FK_series_books_active_job",
            "UX_rebuild_batches_draft_cast",
            "IX_series_books_active_job",
            "IX_rebuild_members_previous_job",
            "is_safe_narration_audio",
            "assert_cast_epoch_integrity",
            "check_cast_epoch_integrity"
        ]);
        Assert.All(names, name => Assert.InRange(Encoding.UTF8.GetByteCount(name), 1, 63));
    }

    private static readonly string[] TriggerNames =
    [
        "CT_cast_epoch_series",
        "CT_cast_epoch_series_book",
        "CT_cast_epoch_revision",
        "CT_cast_epoch_batch",
        "CT_cast_epoch_member",
        "CT_cast_epoch_job",
        "CT_cast_epoch_job_update"
    ];

    private static readonly string[] ConstraintNames =
    [
        "CK_cast_epoch_active_pointer",
        "CK_cast_epoch_revision_state",
        "CK_cast_epoch_batch_chain",
        "CK_cast_epoch_full_cohort",
        "CK_cast_epoch_member_state",
        "CK_cast_epoch_current_pointer",
        "CK_cast_epoch_previous_artifact",
        "CK_rebuild_artifact_membership",
        "CK_rebuild_artifact_visibility"
    ];

    private static StoryVoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=storyvoice_cast_epoch_model;Username=storyvoice;Password=unused")
            .Options;
        return new StoryVoiceDbContext(options);
    }

    private static int Count(string value, string search, StringComparison comparison)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, comparison)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }
}
