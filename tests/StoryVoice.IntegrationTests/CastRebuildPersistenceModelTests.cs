using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class CastRebuildPersistenceModelTests
{
    [Fact]
    public void Npgsql_model_maps_client_keys_bounded_fields_and_field_backed_aggregates()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var revision = RequiredEntity<NarrationCastRevision>(model);
        var assignment = RequiredEntity<NarrationCastAssignment>(model);
        var batch = RequiredEntity<SeriesCastRebuildBatch>(model);
        var member = RequiredEntity<SeriesCastRebuildMember>(model);
        var job = RequiredEntity<NarrationJob>(model);

        Assert.Equal("narration_cast_revisions", revision.GetTableName());
        Assert.Equal("narration_cast_assignments", assignment.GetTableName());
        Assert.Equal("series_cast_rebuild_batches", batch.GetTableName());
        Assert.Equal("series_cast_rebuild_members", member.GetTableName());

        AssertClientKey(revision, nameof(NarrationCastRevision.Id));
        AssertClientKey(assignment, nameof(NarrationCastAssignment.Id));
        AssertClientKey(batch, nameof(SeriesCastRebuildBatch.Id));
        AssertClientKey(member, nameof(SeriesCastRebuildMember.Id));

        AssertProperty(revision, nameof(NarrationCastRevision.Fingerprint), 64, nullable: false);
        Assert.Equal("character(64)", revision.FindProperty(nameof(NarrationCastRevision.Fingerprint))!.GetColumnType());
        AssertProperty(revision, nameof(NarrationCastRevision.Status), 20, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.NarratorProvider), 50, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.NarratorProviderVersion), 50, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.NarratorVoice), 200, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.NarratorRate), 50, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.NarratorPitch), 50, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.NarratorVolume), 50, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.CompositionVersion), 50, nullable: false);
        AssertProperty(revision, nameof(NarrationCastRevision.FfmpegProfile), 200, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.CanonicalNameSnapshot), 200, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.VoiceProvider), 50, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.ProviderVersion), 50, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.Voice), 200, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.Rate), 50, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.Pitch), 50, nullable: false);
        AssertProperty(assignment, nameof(NarrationCastAssignment.Volume), 50, nullable: false);
        AssertProperty(batch, nameof(SeriesCastRebuildBatch.Status), 30, nullable: false);
        AssertProperty(member, nameof(SeriesCastRebuildMember.Status), 30, nullable: false);
        AssertProperty(job, nameof(NarrationJob.Visibility), 30, nullable: false);

        var visibility = job.FindProperty(nameof(NarrationJob.Visibility))!;
        Assert.Equal(typeof(string), visibility.GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(NarrationArtifactVisibility.Published.ToString(), visibility.GetDefaultValue()?.ToString());
        Assert.Equal(typeof(string), revision.FindProperty(nameof(NarrationCastRevision.Status))!
            .GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(typeof(string), batch.FindProperty(nameof(SeriesCastRebuildBatch.Status))!
            .GetTypeMapping().Converter?.ProviderClrType);
        Assert.Equal(typeof(string), member.FindProperty(nameof(SeriesCastRebuildMember.Status))!
            .GetTypeMapping().Converter?.ProviderClrType);

        AssertField(revision.FindNavigation(nameof(NarrationCastRevision.Assignments))!, "_assignments");
        AssertField(batch.FindNavigation(nameof(SeriesCastRebuildBatch.Members))!, "_members");
        AssertField(batch.FindProperty(nameof(SeriesCastRebuildBatch.Status))!, "_status");
        AssertField(batch.FindProperty(nameof(SeriesCastRebuildBatch.UpdatedAt))!, "_updatedAt");
        AssertField(member.FindProperty(nameof(SeriesCastRebuildMember.Status))!, "_status");
        AssertField(member.FindProperty(nameof(SeriesCastRebuildMember.StagedNarrationJobId))!, "_stagedNarrationJobId");

        Assert.NotNull(typeof(StoryVoiceDbContext).GetProperty(nameof(StoryVoiceDbContext.NarrationCastRevisions)));
        Assert.NotNull(typeof(StoryVoiceDbContext).GetProperty(nameof(StoryVoiceDbContext.NarrationCastAssignments)));
        Assert.NotNull(typeof(StoryVoiceDbContext).GetProperty(nameof(StoryVoiceDbContext.SeriesCastRebuildBatches)));
        Assert.NotNull(typeof(StoryVoiceDbContext).GetProperty(nameof(StoryVoiceDbContext.SeriesCastRebuildMembers)));
    }

    [Fact]
    public void Npgsql_model_has_exact_checks_owner_fences_keys_and_indexes()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var revision = RequiredEntity<NarrationCastRevision>(model);
        var assignment = RequiredEntity<NarrationCastAssignment>(model);
        var batch = RequiredEntity<SeriesCastRebuildBatch>(model);
        var member = RequiredEntity<SeriesCastRebuildMember>(model);
        var job = RequiredEntity<NarrationJob>(model);
        var seriesBook = RequiredEntity<SeriesBook>(model);

        AssertCheck(revision, "CK_ncast_revs_revision", "\"RevisionNumber\" >= 1");
        AssertCheck(revision, "CK_ncast_revs_status", "\"Status\" IN ('Draft', 'Active', 'Historical')");
        AssertCheck(revision, "CK_ncast_revs_pauses", "\"DefaultSpeakerPauseMs\" >= 0");
        AssertCheck(revision, "CK_ncast_revs_epoch", "\"EpochNumber\" IS NOT NULL");
        AssertCheck(revision, "CK_ncast_revs_fingerprint", "^[0-9a-f]{64}$");
        AssertCheck(revision, "CK_ncast_revs_chronology", "\"ActivatedAt\" >= \"CreatedAt\"");
        AssertCheck(batch, "CK_rebuild_batches_cohort", "\"CohortMembershipRevision\" >= 1");
        AssertCheck(batch, "CK_rebuild_batches_revisions", "\"BaseActiveCastRevisionId\" IS NULL");
        AssertCheck(batch, "CK_rebuild_batches_status", "ReadyToActivate");
        AssertCheck(batch, "CK_rebuild_batches_chronology", "\"UpdatedAt\" >= \"CreatedAt\"");
        AssertCheck(member, "CK_rebuild_members_revision", "\"MembershipRevision\" >= 1");
        AssertCheck(member, "CK_rebuild_members_status", "Pending");
        AssertCheck(member, "CK_rebuild_members_pointer", "\"Status\" = 'Pending' AND \"StagedNarrationJobId\" IS NULL");
        AssertCheck(member, "CK_rebuild_members_pointer", "\"Status\" IN ('Building', 'Ready') AND \"StagedNarrationJobId\" IS NOT NULL");
        AssertCheck(member, "CK_rebuild_members_distinct_jobs", "\"StagedNarrationJobId\" <> \"PreviousActiveNarrationJobId\"");
        AssertCheck(job, "CK_njobs_visibility", "'Staged', 'Published', 'Historical'");
        AssertCheck(job, "CK_njobs_correlations", "\"SpeechPlanRevisionId\" IS NOT NULL");
        AssertCheck(job, "CK_njobs_correlations", "\"Mode\" = 'SingleVoice' AND \"Visibility\" <> 'Staged'");
        AssertCheck(job, "CK_njobs_published_audio", "\"Status\" = 'Completed'");

        AssertKey(revision, "AK_ncast_revs_scope", nameof(NarrationCastRevision.OwnerId), nameof(NarrationCastRevision.SeriesId), nameof(NarrationCastRevision.Id));
        AssertKey(batch, "AK_rebuild_batches_scope", nameof(SeriesCastRebuildBatch.OwnerId), nameof(SeriesCastRebuildBatch.SeriesId), nameof(SeriesCastRebuildBatch.Id));
        AssertKey(batch, "AK_rebuild_batches_job_cast", nameof(SeriesCastRebuildBatch.OwnerId), nameof(SeriesCastRebuildBatch.SeriesId), nameof(SeriesCastRebuildBatch.Id), nameof(SeriesCastRebuildBatch.DraftCastRevisionId));
        AssertKey(member, "AK_rebuild_members_scope", nameof(SeriesCastRebuildMember.OwnerId), nameof(SeriesCastRebuildMember.SeriesId), nameof(SeriesCastRebuildMember.BatchId), nameof(SeriesCastRebuildMember.Id));
        AssertKey(
            seriesBook,
            "AK_series_books_rebuild_scope",
            nameof(SeriesBook.OwnerId),
            nameof(SeriesBook.SeriesId),
            nameof(SeriesBook.Id),
            nameof(SeriesBook.BookId),
            nameof(SeriesBook.MembershipRevision));

        AssertIndex(revision, "UX_ncast_revs_number", true, null, nameof(NarrationCastRevision.OwnerId), nameof(NarrationCastRevision.SeriesId), nameof(NarrationCastRevision.RevisionNumber));
        AssertIndex(revision, "UX_ncast_revs_fingerprint", true, null, nameof(NarrationCastRevision.OwnerId), nameof(NarrationCastRevision.SeriesId), nameof(NarrationCastRevision.Fingerprint));
        AssertIndex(revision, "UX_ncast_revs_active", true, "\"Status\" = 'Active'", nameof(NarrationCastRevision.OwnerId), nameof(NarrationCastRevision.SeriesId));
        AssertIndex(revision, "UX_ncast_revs_epoch", true, "\"EpochNumber\" IS NOT NULL", nameof(NarrationCastRevision.OwnerId), nameof(NarrationCastRevision.SeriesId), nameof(NarrationCastRevision.EpochNumber));
        AssertIndex(assignment, "UX_ncast_asgn_character", true, null, nameof(NarrationCastAssignment.OwnerId), nameof(NarrationCastAssignment.SeriesId), nameof(NarrationCastAssignment.CastRevisionId), nameof(NarrationCastAssignment.CharacterId));
        AssertIndex(member, "UX_rebuild_members_series_book", true, null, nameof(SeriesCastRebuildMember.OwnerId), nameof(SeriesCastRebuildMember.SeriesId), nameof(SeriesCastRebuildMember.BatchId), nameof(SeriesCastRebuildMember.SeriesBookId));
        AssertIndex(member, "UX_rebuild_members_book", true, null, nameof(SeriesCastRebuildMember.OwnerId), nameof(SeriesCastRebuildMember.SeriesId), nameof(SeriesCastRebuildMember.BatchId), nameof(SeriesCastRebuildMember.BookId));
        AssertIndex(job, "UX_njobs_owner_book_id", true, null, nameof(NarrationJob.OwnerId), nameof(NarrationJob.BookId), nameof(NarrationJob.Id));
        AssertIndex(job, "UX_njobs_member_artifact", true, null, nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId), nameof(NarrationJob.RebuildBatchId), nameof(NarrationJob.RebuildMemberId), nameof(NarrationJob.BookId), nameof(NarrationJob.Id));
        AssertIndex(job, "UX_njobs_multi_member", true, "\"Mode\" = 'MultiCharacter'", nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId), nameof(NarrationJob.RebuildBatchId), nameof(NarrationJob.RebuildMemberId));
        AssertIndex(job, "IX_njobs_batch_cast", false, null, nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId), nameof(NarrationJob.RebuildBatchId), nameof(NarrationJob.CastRevisionId));

        AssertForeignKey(revision, typeof(StorySeries), DeleteBehavior.Cascade, "FK_ncast_revs_series_scope", nameof(NarrationCastRevision.OwnerId), nameof(NarrationCastRevision.SeriesId));
        AssertForeignKey(assignment, typeof(NarrationCastRevision), DeleteBehavior.Cascade, "FK_ncast_asgn_revision_scope", nameof(NarrationCastAssignment.OwnerId), nameof(NarrationCastAssignment.SeriesId), nameof(NarrationCastAssignment.CastRevisionId));
        AssertForeignKey(assignment, typeof(SeriesCharacter), DeleteBehavior.Restrict, "FK_ncast_asgn_character_scope", nameof(NarrationCastAssignment.OwnerId), nameof(NarrationCastAssignment.SeriesId), nameof(NarrationCastAssignment.CharacterId));
        AssertForeignKey(batch, typeof(StorySeries), DeleteBehavior.Cascade, "FK_rebuild_batches_series_scope", nameof(SeriesCastRebuildBatch.OwnerId), nameof(SeriesCastRebuildBatch.SeriesId));
        AssertForeignKey(member, typeof(SeriesCastRebuildBatch), DeleteBehavior.Cascade, "FK_rebuild_members_batch_scope", nameof(SeriesCastRebuildMember.OwnerId), nameof(SeriesCastRebuildMember.SeriesId), nameof(SeriesCastRebuildMember.BatchId));
        AssertForeignKey(
            member,
            typeof(SeriesBook),
            DeleteBehavior.Restrict,
            "FK_rebuild_members_series_book",
            nameof(SeriesCastRebuildMember.OwnerId),
            nameof(SeriesCastRebuildMember.SeriesId),
            nameof(SeriesCastRebuildMember.SeriesBookId),
            nameof(SeriesCastRebuildMember.BookId),
            nameof(SeriesCastRebuildMember.MembershipRevision));
        AssertForeignKey(job, typeof(StorySeries), DeleteBehavior.Restrict, "FK_njobs_series_scope", nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId));
        AssertForeignKey(job, typeof(NarrationCastRevision), DeleteBehavior.Restrict, "FK_njobs_cast_scope", nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId), nameof(NarrationJob.CastRevisionId));
        AssertForeignKey(job, typeof(SeriesCastRebuildMember), DeleteBehavior.Restrict, "FK_njobs_rebuild_member_scope", nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId), nameof(NarrationJob.RebuildBatchId), nameof(NarrationJob.RebuildMemberId));
        AssertForeignKey(job, typeof(SeriesCastRebuildBatch), DeleteBehavior.Restrict, "FK_njobs_batch_cast", nameof(NarrationJob.OwnerId), nameof(NarrationJob.SeriesId), nameof(NarrationJob.RebuildBatchId), nameof(NarrationJob.CastRevisionId));
        Assert.DoesNotContain(
            job.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(NarrationJob.SpeechPlanRevisionId)));

        var newEntityTypes = new[] { revision, assignment, batch, member, job };
        var names = newEntityTypes
            .SelectMany(entity => entity.GetKeys().Select(key => key.GetName())
                .Concat(entity.GetIndexes().Select(index => index.GetDatabaseName()))
                .Concat(entity.GetForeignKeys().Select(foreignKey => foreignKey.GetConstraintName()))
                .Concat(entity.GetCheckConstraints().Select(check => check.Name)))
            .Where(name => name is not null)
            .Cast<string>()
            .Concat(new[]
            {
                "FK_rebuild_members_book_owner",
                "FK_rebuild_members_staged_job",
                "FK_rebuild_members_previous_job",
                "CT_rebuild_artifact_member",
                "CT_rebuild_artifact_job",
                "CT_rebuild_artifact_batch",
                "CK_rebuild_artifact_visibility"
            });
        Assert.All(names, name => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(name), 1, 63));
    }

    [Fact]
    public void Migration_is_after_AddSeriesCast_has_no_pending_model_changes_and_contains_raw_fences_and_first_down_guard()
    {
        using var db = CreateContext();
        var migrations = db.Database.GetMigrations().ToArray();
        var previous = Assert.Single(migrations, migration => migration.EndsWith("_AddSeriesCast", StringComparison.Ordinal));
        var current = Assert.Single(migrations, migration => migration.EndsWith("_AddCastRebuildPersistence", StringComparison.Ordinal));
        Assert.True(Array.IndexOf(migrations, current) > Array.IndexOf(migrations, previous));
        Assert.False(db.Database.HasPendingModelChanges());

        var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript(previous, current)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("ADD \"Visibility\" character varying(30) NOT NULL DEFAULT 'Published'", up, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE narration_cast_revisions", up, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE narration_cast_assignments", up, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE series_cast_rebuild_batches", up, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE series_cast_rebuild_members", up, StringComparison.Ordinal);
        Assert.Contains("FK_rebuild_members_book_owner", up, StringComparison.Ordinal);
        Assert.Contains("FK_rebuild_members_staged_job", up, StringComparison.Ordinal);
        Assert.Contains("FK_rebuild_members_previous_job", up, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"UX_njobs_member_artifact\"", up, StringComparison.Ordinal);
        Assert.Contains(
            "FOREIGN KEY (\"OwnerId\", \"SeriesId\", \"BatchId\", \"Id\", \"BookId\", \"StagedNarrationJobId\")",
            up,
            StringComparison.Ordinal);
        Assert.Contains(
            "REFERENCES \"narration_jobs\" (\"OwnerId\", \"SeriesId\", \"RebuildBatchId\", \"RebuildMemberId\", \"BookId\", \"Id\")",
            up,
            StringComparison.Ordinal);
        Assert.Contains("ON DELETE NO ACTION\nDEFERRABLE INITIALLY DEFERRED", up, StringComparison.Ordinal);
        Assert.Contains("CREATE FUNCTION check_rebuild_artifact_visibility()", up, StringComparison.Ordinal);
        Assert.Contains("CK_rebuild_artifact_visibility", up, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER \"CT_rebuild_artifact_member\"", up, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER \"CT_rebuild_artifact_job\"", up, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER \"CT_rebuild_artifact_batch\"", up, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            "(?is)FOREIGN KEY\\s*\\([^)]*\"SpeechPlanRevisionId\"[^)]*\\)",
            up);

        var down = migrator.GenerateScript(current, previous);
        var guard = down.IndexOf("\"Mode\" IS DISTINCT FROM 'SingleVoice'", StringComparison.Ordinal);
        var firstDrop = down.IndexOf("DROP ", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        Assert.True(firstDrop > guard);
        Assert.Contains("\"Visibility\" IS DISTINCT FROM 'Published'", down, StringComparison.Ordinal);
        Assert.Contains("Cannot roll back cast rebuild persistence while dependent rows exist", down, StringComparison.Ordinal);

        var idempotent = migrator.GenerateScript(null, null, MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("FK_rebuild_members_book_owner", idempotent, StringComparison.Ordinal);
        Assert.Contains("FK_rebuild_members_staged_job", idempotent, StringComparison.Ordinal);
        Assert.Contains("FK_rebuild_members_previous_job", idempotent, StringComparison.Ordinal);
        Assert.Contains("UX_njobs_member_artifact", idempotent, StringComparison.Ordinal);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", idempotent, StringComparison.Ordinal);
        Assert.Contains("check_rebuild_artifact_visibility", idempotent, StringComparison.Ordinal);
        Assert.Contains("Cannot roll back cast rebuild persistence while dependent rows exist", down, StringComparison.Ordinal);
    }

    private static StoryVoiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoryVoiceDbContext>()
            .UseNpgsql("Host=localhost;Database=storyvoice_cast_rebuild_model;Username=storyvoice;Password=unused")
            .Options;
        return new StoryVoiceDbContext(options);
    }

    private static IEntityType RequiredEntity<TEntity>(IModel model) =>
        Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(typeof(TEntity)));

    private static void AssertClientKey(IEntityType entity, string propertyName)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(propertyName));
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    private static void AssertProperty(IEntityType entity, string name, int maximumLength, bool nullable)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entity.FindProperty(name));
        Assert.Equal(maximumLength, property.GetMaxLength());
        Assert.Equal(nullable, property.IsNullable);
    }

    private static void AssertField(IPropertyBase property, string fieldName)
    {
        Assert.Equal(fieldName, property.GetFieldName());
        Assert.Equal(PropertyAccessMode.Field, property.GetPropertyAccessMode());
    }

    private static void AssertCheck(IEntityType entity, string name, string sqlFragment)
    {
        var check = Assert.Single(entity.GetCheckConstraints(), candidate => candidate.Name == name);
        Assert.Contains(sqlFragment, check.Sql, StringComparison.Ordinal);
    }

    private static void AssertKey(IEntityType entity, string name, params string[] properties)
    {
        var key = Assert.Single(entity.GetKeys(), candidate => candidate.GetName() == name);
        Assert.Equal(properties, key.Properties.Select(property => property.Name));
    }

    private static void AssertIndex(
        IEntityType entity,
        string name,
        bool unique,
        string? filter,
        params string[] properties)
    {
        var index = Assert.Single(entity.GetIndexes(), candidate => candidate.GetDatabaseName() == name);
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(filter, index.GetFilter());
        Assert.Equal(properties, index.Properties.Select(property => property.Name));
    }

    private static void AssertForeignKey(
        IEntityType entity,
        Type principalType,
        DeleteBehavior deleteBehavior,
        string constraintName,
        params string[] properties)
    {
        var foreignKey = Assert.Single(
            entity.GetForeignKeys(),
            candidate => candidate.GetConstraintName() == constraintName);
        Assert.Equal(principalType, foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
        Assert.Equal(properties, foreignKey.Properties.Select(property => property.Name));
    }
}
