using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoryVoice.IntegrationTests;

public sealed class CastRebuildPersistencePostgreSqlTests
{
    private const string PreviousMigration = "20260811013001_AddSeriesCast";
    private const string CurrentMigration = "20260811040925_AddCastRebuildPersistence";

    [Fact]
    public async Task Upgrade_backfills_legacy_jobs_and_preserves_old_writer_defaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var connectionString = postgres.GetConnectionString();
        var ownerId = Guid.NewGuid();
        var book = Book.Create(ownerId, "Synthetic legacy book", "Synthetic author", "en", "legacy.txt");
        var legacyJobId = Guid.NewGuid();
        var oldWriterJobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = CreateContext(connectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, cancellationToken);
        db.Users.Add(CreateUser(ownerId, "cast-upgrade-owner"));
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);
        await InsertLegacyJobAsync(
            db,
            legacyJobId,
            ownerId,
            book.Id,
            "cast-upgrade-legacy",
            now,
            cancellationToken);

        await migrator.MigrateAsync(CurrentMigration, cancellationToken);

        db.ChangeTracker.Clear();
        var upgraded = await db.NarrationJobs.AsNoTracking()
            .SingleAsync(job => job.Id == legacyJobId, cancellationToken);
        Assert.Equal(NarrationMode.SingleVoice, upgraded.Mode);
        Assert.Equal(NarrationArtifactVisibility.Published, upgraded.Visibility);
        Assert.Null(upgraded.SeriesId);
        Assert.Null(upgraded.CastRevisionId);
        Assert.Null(upgraded.SpeechPlanRevisionId);
        Assert.Null(upgraded.RebuildBatchId);
        Assert.Null(upgraded.RebuildMemberId);

        await InsertLegacyJobAsync(
            db,
            oldWriterJobId,
            ownerId,
            book.Id,
            "cast-upgrade-old-writer",
            now.AddMinutes(1),
            cancellationToken);

        db.ChangeTracker.Clear();
        var oldWriter = await db.NarrationJobs.AsNoTracking()
            .SingleAsync(job => job.Id == oldWriterJobId, cancellationToken);
        Assert.Equal(NarrationMode.SingleVoice, oldWriter.Mode);
        Assert.Equal(NarrationArtifactVisibility.Published, oldWriter.Visibility);
        Assert.Null(oldWriter.SeriesId);
        Assert.Null(oldWriter.CastRevisionId);
        Assert.Null(oldWriter.SpeechPlanRevisionId);
        Assert.Null(oldWriter.RebuildBatchId);
        Assert.Null(oldWriter.RebuildMemberId);
    }

    [Fact]
    public async Task Fresh_context_materializes_cast_and_rebuild_then_one_save_accepts_exact_deferred_cycle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var graph = await CreateGraphAsync(
            postgres.GetConnectionString(),
            Guid.NewGuid(),
            "cast-roundtrip",
            includePointerCandidates: false,
            cancellationToken);

        await using (var reload = CreateContext(graph.ConnectionString))
        {
            var revision = await reload.NarrationCastRevisions.AsNoTracking()
                .Include(candidate => candidate.Assignments)
                .SingleAsync(candidate => candidate.Id == graph.RevisionId, cancellationToken);
            Assert.Equal(graph.RevisionId, revision.Id);
            Assert.Equal(graph.OwnerId, revision.OwnerId);
            Assert.Equal(graph.SeriesId, revision.SeriesId);
            Assert.Equal(7, revision.RevisionNumber);
            Assert.Equal(graph.Fingerprint, revision.Fingerprint);
            Assert.Equal(NarrationCastRevisionStatus.Draft, revision.Status);
            Assert.Null(revision.EpochNumber);
            Assert.Equal("synthetic-provider", revision.NarratorProvider);
            Assert.Equal("provider-v1", revision.NarratorProviderVersion);
            Assert.Equal("synthetic-narrator", revision.NarratorVoice);
            Assert.Equal("+0%", revision.NarratorRate);
            Assert.Equal("+0Hz", revision.NarratorPitch);
            Assert.Equal("+0%", revision.NarratorVolume);
            Assert.Equal(250, revision.DefaultSpeakerPauseMs);
            Assert.Equal(500, revision.ChapterPauseMs);
            Assert.Equal("composition-v1", revision.CompositionVersion);
            Assert.Equal("mp3-128k", revision.FfmpegProfile);
            Assert.Equal(graph.CreatedAt, revision.CreatedAt);
            Assert.Null(revision.ActivatedAt);

            var assignment = Assert.Single(revision.Assignments);
            Assert.Equal(graph.AssignmentId, assignment.Id);
            Assert.Equal(graph.OwnerId, assignment.OwnerId);
            Assert.Equal(graph.SeriesId, assignment.SeriesId);
            Assert.Equal(graph.RevisionId, assignment.CastRevisionId);
            Assert.Equal(graph.CharacterId, assignment.CharacterId);
            Assert.Equal("Synthetic Hero", assignment.CanonicalNameSnapshot);
            Assert.Equal("synthetic-provider", assignment.VoiceProvider);
            Assert.Equal("provider-v1", assignment.ProviderVersion);
            Assert.Equal("synthetic-hero-voice", assignment.Voice);
            Assert.Equal("+5%", assignment.Rate);
            Assert.Equal("+1Hz", assignment.Pitch);
            Assert.Equal("-2%", assignment.Volume);

            var batch = await reload.SeriesCastRebuildBatches.AsNoTracking()
                .Include(candidate => candidate.Members)
                .SingleAsync(candidate => candidate.Id == graph.BatchId, cancellationToken);
            Assert.Equal(graph.BatchId, batch.Id);
            Assert.Equal(graph.OwnerId, batch.OwnerId);
            Assert.Equal(graph.SeriesId, batch.SeriesId);
            Assert.Null(batch.BaseActiveCastRevisionId);
            Assert.Equal(graph.RevisionId, batch.DraftCastRevisionId);
            Assert.Equal(1, batch.CohortMembershipRevision);
            Assert.Equal(SeriesCastRebuildBatchStatus.Draft, batch.Status);
            Assert.Equal(graph.CreatedAt, batch.CreatedAt);
            Assert.Equal(graph.CreatedAt, batch.UpdatedAt);

            var member = Assert.Single(batch.Members);
            Assert.Equal(graph.MemberId, member.Id);
            Assert.Equal(graph.OwnerId, member.OwnerId);
            Assert.Equal(graph.SeriesId, member.SeriesId);
            Assert.Equal(graph.BatchId, member.BatchId);
            Assert.Equal(graph.SeriesBookId, member.SeriesBookId);
            Assert.Equal(graph.BookId, member.BookId);
            Assert.Equal(1, member.MembershipRevision);
            Assert.Null(member.StagedNarrationJobId);
            Assert.Null(member.PreviousActiveNarrationJobId);
            Assert.Equal(SeriesCastRebuildMemberStatus.Pending, member.Status);
        }

        Guid stagedJobId;
        var speechPlanRevisionId = Guid.NewGuid();
        await using (var createCycle = CreateContext(graph.ConnectionString))
        {
            var batch = await createCycle.SeriesCastRebuildBatches
                .Include(candidate => candidate.Members)
                .SingleAsync(candidate => candidate.Id == graph.BatchId, cancellationToken);
            batch.StartBuilding(graph.CreatedAt.AddMinutes(1));
            var member = Assert.Single(batch.Members);
            var stagedJob = CreateMultiCharacterStagedJob(
                graph.OwnerId,
                graph.BookId,
                graph.SeriesId,
                graph.RevisionId,
                speechPlanRevisionId,
                graph.BatchId,
                graph.MemberId,
                "roundtrip-staged-source",
                graph.CreatedAt.AddMinutes(1));
            stagedJobId = stagedJob.Id;
            AttachStagedJob(member, stagedJobId);

            await using var transaction = await createCycle.Database.BeginTransactionAsync(cancellationToken);
            createCycle.NarrationJobs.Add(stagedJob);
            Assert.Equal(3, await createCycle.SaveChangesAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await using (var proof = CreateContext(graph.ConnectionString))
        {
            var member = await proof.SeriesCastRebuildMembers.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == graph.MemberId, cancellationToken);
            Assert.Equal(SeriesCastRebuildMemberStatus.Building, member.Status);
            Assert.Equal(stagedJobId, member.StagedNarrationJobId);

            var job = await proof.NarrationJobs.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == stagedJobId, cancellationToken);
            Assert.Equal(graph.OwnerId, job.OwnerId);
            Assert.Equal(graph.BookId, job.BookId);
            Assert.Equal(graph.BookId, job.ContentBookId);
            Assert.Equal(NarrationMode.MultiCharacter, job.Mode);
            Assert.Equal(NarrationArtifactVisibility.Staged, job.Visibility);
            Assert.Equal(graph.SeriesId, job.SeriesId);
            Assert.Equal(graph.RevisionId, job.CastRevisionId);
            Assert.Equal(speechPlanRevisionId, job.SpeechPlanRevisionId);
            Assert.Equal(graph.BatchId, job.RebuildBatchId);
            Assert.Equal(graph.MemberId, job.RebuildMemberId);
            Assert.Equal(NarrationJobStatus.Queued, job.Status);
        }
    }

    [Fact]
    public async Task PostgreSql_named_constraints_reject_cross_scope_exact_pointer_invalid_state_and_duplicates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var graphA = await CreateGraphAsync(
            postgres.GetConnectionString(),
            ownerA,
            "constraint-owner-a",
            includePointerCandidates: true,
            cancellationToken);
        var graphB = await CreateGraphAsync(
            postgres.GetConnectionString(),
            ownerB,
            "constraint-owner-b",
            includePointerCandidates: false,
            cancellationToken);

        await StagePointerCandidatesAsync(graphA, cancellationToken);

        await using var db = CreateContext(graphA.ConnectionString);
        var now = graphA.CreatedAt.AddMinutes(5);

        // UX_rebuild_batches_draft_cast allows only one batch per draft cast revision, so each
        // throwaway batch created below (purely to exercise a different constraint) needs its own
        // revision rather than reusing graphA.RevisionId, which graphA's own primary batch already
        // claims as its draft.
        var freshDraftRevisionCounter = 0;
        async Task<Guid> FreshDraftRevisionAsync()
        {
            freshDraftRevisionCounter++;
            var freshId = Guid.NewGuid();
            await InsertRevisionAsync(
                db,
                freshId,
                graphA.OwnerId,
                graphA.SeriesId,
                9100 + freshDraftRevisionCounter,
                new string((char)('0' + freshDraftRevisionCounter), 64),
                now,
                cancellationToken);
            return freshId;
        }

        var legacyJobId = Guid.NewGuid();
        await InsertLegacyJobAsync(
            db,
            legacyJobId,
            graphA.OwnerId,
            graphA.BookId,
            "constraint-legacy",
            now,
            cancellationToken);

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE series_cast_rebuild_members
                SET "StagedNarrationJobId" = {graphA.SiblingJobId}
                WHERE "Id" = {graphA.MemberId};
                """, cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_artifact_membership");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE series_cast_rebuild_members
                SET "StagedNarrationJobId" = {graphA.OtherBookJobId}
                WHERE "Id" = {graphA.MemberId};
                """, cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_artifact_membership");

        await AssertPostgresErrorAsync(
            () => InsertRevisionAsync(db, Guid.NewGuid(), graphA.OwnerId, graphB.SeriesId, 100, new string('a', 64), now, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_ncast_revs_series_scope");
        await AssertPostgresErrorAsync(
            () => InsertAssignmentAsync(db, Guid.NewGuid(), graphA.OwnerId, graphA.SeriesId, graphB.RevisionId, graphA.CharacterId, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_ncast_asgn_revision_scope");
        await AssertPostgresErrorAsync(
            () => InsertAssignmentAsync(db, Guid.NewGuid(), graphA.OwnerId, graphA.SeriesId, graphA.RevisionId, graphB.CharacterId, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_ncast_asgn_character_scope");
        await AssertPostgresErrorAsync(
            () => InsertBatchAsync(db, Guid.NewGuid(), graphA.OwnerId, graphA.SeriesId, null, graphB.RevisionId, now, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_rebuild_batches_draft_cast");
        var baseCastFenceDraftRevisionId = await FreshDraftRevisionAsync();
        await AssertPostgresErrorAsync(
            () => InsertBatchAsync(db, Guid.NewGuid(), graphA.OwnerId, graphA.SeriesId, graphB.RevisionId, baseCastFenceDraftRevisionId, now, cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_rebuild_batches_base_cast");
        await AssertPostgresErrorAsync(
            () => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                graphB.BatchId,
                graphA.SeriesBookId,
                graphA.BookId,
                null,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_rebuild_members_batch_scope");

        var seriesBookFenceBatchId = Guid.NewGuid();
        await InsertBatchAsync(db, seriesBookFenceBatchId, graphA.OwnerId, graphA.SeriesId, null, await FreshDraftRevisionAsync(), now, cancellationToken);
        await AssertPostgresErrorAsync(
            () => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                seriesBookFenceBatchId,
                graphB.SeriesBookId,
                graphA.BookId,
                null,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_rebuild_members_series_book");

        var bookCoherenceBatchId = Guid.NewGuid();
        await InsertBatchAsync(db, bookCoherenceBatchId, graphA.OwnerId, graphA.SeriesId, null, await FreshDraftRevisionAsync(), now, cancellationToken);
        await AssertPostgresErrorAsync(
            () => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                bookCoherenceBatchId,
                graphA.SeriesBookId,
                graphA.OtherBookId,
                null,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_rebuild_members_series_book");

        await using (var rawBookFence = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE series_cast_rebuild_members DROP CONSTRAINT \"FK_rebuild_members_series_book\";",
                cancellationToken);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                bookCoherenceBatchId,
                graphA.SeriesBookId,
                graphB.BookId,
                null,
                cancellationToken));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal("FK_rebuild_members_book_owner", exception.ConstraintName);
            await rawBookFence.RollbackAsync(cancellationToken);
        }

        var previousPointerBatchId = Guid.NewGuid();
        await InsertBatchAsync(db, previousPointerBatchId, graphA.OwnerId, graphA.SeriesId, null, await FreshDraftRevisionAsync(), now, cancellationToken);
        await AssertPostgresErrorAsync(
            () => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                previousPointerBatchId,
                graphA.SeriesBookId,
                graphA.BookId,
                graphA.OtherBookJobId,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_rebuild_members_previous_job");

        var jobFenceBatchId = Guid.NewGuid();
        var jobFenceMemberId = Guid.NewGuid();
        var jobFenceBatchDraftRevisionId = await FreshDraftRevisionAsync();
        await InsertBatchAsync(db, jobFenceBatchId, graphA.OwnerId, graphA.SeriesId, null, jobFenceBatchDraftRevisionId, now, cancellationToken);
        await InsertMemberAsync(
            db,
            jobFenceMemberId,
            graphA.OwnerId,
            graphA.SeriesId,
            jobFenceBatchId,
            graphA.SeriesBookId,
            graphA.BookId,
            null,
            cancellationToken);
        var sameSeriesWrongRevisionId = Guid.NewGuid();
        await InsertRevisionAsync(
            db,
            sameSeriesWrongRevisionId,
            graphA.OwnerId,
            graphA.SeriesId,
            8,
            new string('c', 64),
            now,
            cancellationToken);
        await AssertPostgresErrorAsync(
            () => InsertMultiCharacterJobAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.BookId,
                graphA.SeriesId,
                sameSeriesWrongRevisionId,
                Guid.NewGuid(),
                jobFenceBatchId,
                jobFenceMemberId,
                "wrong-batch-cast",
                now,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_njobs_batch_cast");
        await AssertPostgresErrorAsync(
            () => InsertMultiCharacterJobAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.BookId,
                graphA.SeriesId,
                jobFenceBatchDraftRevisionId,
                Guid.NewGuid(),
                jobFenceBatchId,
                Guid.NewGuid(),
                "wrong-member-scope",
                now,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "FK_njobs_rebuild_member_scope");

        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"Visibility\" = 'Invisible' WHERE \"Id\" = {legacyJobId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_njobs_visibility");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"Visibility\" = 'Staged' WHERE \"Id\" = {legacyJobId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_njobs_correlations");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE series_cast_rebuild_batches SET \"Status\" = 'Invalid' WHERE \"Id\" = {graphA.BatchId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_batches_status");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_cast_revisions SET \"EpochNumber\" = 0 WHERE \"Id\" = {graphA.RevisionId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_ncast_revs_epoch");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_cast_revisions SET \"Fingerprint\" = {new string('A', 64)} WHERE \"Id\" = {graphA.RevisionId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_ncast_revs_fingerprint");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE narration_cast_revisions
                SET "Status" = 'Active', "EpochNumber" = 1, "ActivatedAt" = {graphA.CreatedAt.AddSeconds(-1)}
                WHERE "Id" = {graphA.RevisionId};
                """, cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_ncast_revs_chronology");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_cast_revisions SET \"DefaultSpeakerPauseMs\" = 60001 WHERE \"Id\" = {graphA.RevisionId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_ncast_revs_pauses");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE series_cast_rebuild_batches SET \"UpdatedAt\" = {graphA.CreatedAt.AddSeconds(-1)} WHERE \"Id\" = {graphA.BatchId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_batches_chronology");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE series_cast_rebuild_members SET \"Status\" = 'Pending' WHERE \"Id\" = {graphA.MemberId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_members_pointer");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE series_cast_rebuild_members SET \"PreviousActiveNarrationJobId\" = {graphA.PrimaryJobId!.Value} WHERE \"Id\" = {graphA.MemberId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_members_distinct_jobs");
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"SeriesId\" = {graphA.SeriesId} WHERE \"Id\" = {legacyJobId}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_njobs_correlations");
        await AssertPostgresErrorAsync(
            () => InsertAssignmentAsync(db, Guid.NewGuid(), graphA.OwnerId, graphA.SeriesId, graphA.RevisionId, graphA.CharacterId, cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "UX_ncast_asgn_character");
        await AssertPostgresErrorAsync(
            () => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                graphA.BatchId,
                graphA.SeriesBookId,
                graphA.OtherBookId,
                null,
                cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "UX_rebuild_members_series_book");
        await AssertPostgresErrorAsync(
            () => InsertMemberAsync(
                db,
                Guid.NewGuid(),
                graphA.OwnerId,
                graphA.SeriesId,
                graphA.BatchId,
                graphA.OtherSeriesBookId,
                graphA.BookId,
                null,
                cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "UX_rebuild_members_book");

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE narration_jobs
            SET "Status" = 'Completed',
                "ProgressPercent" = 100,
                "AudioRelativePath" = 'staged/constraint.mp3',
                "AudioBytes" = 123,
                "CompletedAt" = {now},
                "UpdatedAt" = {now}
            WHERE "Id" = {graphA.PrimaryJobId!.Value};
            """, cancellationToken);
        await AssertPostgresErrorAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"Visibility\" = 'Published' WHERE \"Id\" = {graphA.PrimaryJobId!.Value}",
                cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "CK_rebuild_artifact_visibility");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE series_cast_rebuild_members SET \"Status\" = 'Ready' WHERE \"Id\" = {graphA.MemberId}",
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE series_cast_rebuild_batches SET \"Status\" = 'ReadyToActivate', \"UpdatedAt\" = {now} WHERE \"Id\" = {graphA.BatchId}",
            cancellationToken);
        // graphA's series has two series books (the pointer-candidate machinery earlier in this test
        // needs the second one for cross-book FK checks), but graphA.BatchId only ever staged a
        // member for the first book. assert_cast_epoch_integrity's CK_cast_epoch_full_cohort trigger
        // now correctly rejects activating a batch that doesn't cover every current series book —
        // exactly what a real PostgreSqlCastEpochActivationPublisher.ActivateOnceAsync call would
        // also refuse. Assert that rejection instead of pretending a partial-cohort batch can go live.
        await using (var activation = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"Visibility\" = 'Published' WHERE \"Id\" = {graphA.PrimaryJobId!.Value}",
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE series_cast_rebuild_batches SET \"Status\" = 'Activated', \"UpdatedAt\" = {now} WHERE \"Id\" = {graphA.BatchId}",
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE series_books SET \"ActiveNarrationJobId\" = {graphA.PrimaryJobId!.Value} WHERE \"Id\" = {graphA.SeriesBookId}",
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_cast_revisions SET \"Status\" = 'Active', \"EpochNumber\" = 1, \"ActivatedAt\" = {now} WHERE \"Id\" = {graphA.RevisionId}",
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE story_series SET \"ActiveCastRevisionId\" = {graphA.RevisionId}, \"UpdatedAt\" = {now} WHERE \"Id\" = {graphA.SeriesId}",
                cancellationToken);
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => activation.CommitAsync(cancellationToken));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("CK_cast_epoch_full_cohort", exception.ConstraintName);
        }

        Assert.Equal(
            NarrationArtifactVisibility.Staged,
            await db.NarrationJobs
                .Where(job => job.Id == graphA.PrimaryJobId.Value)
                .Select(job => job.Visibility)
                .SingleAsync(cancellationToken));
        Assert.Equal(
            SeriesCastRebuildBatchStatus.ReadyToActivate,
            await db.SeriesCastRebuildBatches
                .Where(batch => batch.Id == graphA.BatchId)
                .Select(batch => batch.Status)
                .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task Deferred_staged_cycle_can_be_deleted_job_first_in_one_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var graph = await CreateGraphAsync(
            postgres.GetConnectionString(),
            Guid.NewGuid(),
            "cast-cycle-delete",
            includePointerCandidates: true,
            cancellationToken);
        await StagePointerCandidatesAsync(graph, cancellationToken);

        await using var db = CreateContext(graph.ConnectionString);
        await using (var orphanDelete = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM narration_jobs WHERE \"Id\" = {graph.PrimaryJobId!.Value}",
                cancellationToken);
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => orphanDelete.CommitAsync(cancellationToken));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal("FK_rebuild_members_staged_job", exception.ConstraintName);
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM narration_jobs WHERE \"Id\" = {graph.PrimaryJobId!.Value}",
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM series_cast_rebuild_batches WHERE \"Id\" = {graph.BatchId}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        Assert.False(await db.NarrationJobs.AnyAsync(job => job.Id == graph.PrimaryJobId!.Value, cancellationToken));
        Assert.False(await db.SeriesCastRebuildMembers.AnyAsync(member => member.Id == graph.MemberId, cancellationToken));
        Assert.False(await db.SeriesCastRebuildBatches.AnyAsync(batch => batch.Id == graph.BatchId, cancellationToken));
    }

    [Fact]
    public async Task Down_fails_atomically_with_new_rows_succeeds_for_compatible_rows_and_guards_historical_visibility()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var postgres = await StartPostgresAsync(cancellationToken);
        var graph = await CreateGraphAsync(
            postgres.GetConnectionString(),
            Guid.NewGuid(),
            "cast-down",
            includePointerCandidates: false,
            cancellationToken,
            targetMigration: CurrentMigration);
        var legacyJobId = Guid.NewGuid();

        await using (var db = CreateContext(graph.ConnectionString))
        {
            await InsertLegacyJobAsync(
                db,
                legacyJobId,
                graph.OwnerId,
                graph.BookId,
                "down-compatible-legacy",
                graph.CreatedAt.AddMinutes(1),
                cancellationToken);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                db.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken));
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
            Assert.Contains("dependent rows exist", exception.MessageText, StringComparison.Ordinal);
        }

        await using (var atomicProof = CreateContext(graph.ConnectionString))
        {
            Assert.Contains(CurrentMigration, await atomicProof.Database.GetAppliedMigrationsAsync(cancellationToken));
            Assert.Equal(1, await atomicProof.NarrationCastRevisions.CountAsync(cancellationToken));
            Assert.Equal(1, await atomicProof.SeriesCastRebuildBatches.CountAsync(cancellationToken));
            Assert.Equal(1, await atomicProof.SeriesCastRebuildMembers.CountAsync(cancellationToken));

            await atomicProof.Database.ExecuteSqlRawAsync("DELETE FROM narration_cast_assignments", cancellationToken);
            await atomicProof.Database.ExecuteSqlRawAsync("DELETE FROM series_cast_rebuild_members", cancellationToken);
            await atomicProof.Database.ExecuteSqlRawAsync("DELETE FROM series_cast_rebuild_batches", cancellationToken);
            await atomicProof.Database.ExecuteSqlRawAsync("DELETE FROM narration_cast_revisions", cancellationToken);
            await atomicProof.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken);
        }

        Assert.False(await ColumnExistsAsync(graph.ConnectionString, "narration_jobs", "Visibility", cancellationToken));
        Assert.Equal(1, await CountJobsAsync(graph.ConnectionString, cancellationToken));

        await using (var reupgrade = CreateContext(graph.ConnectionString))
        {
            await reupgrade.GetService<IMigrator>().MigrateAsync(CurrentMigration, cancellationToken);
            var retained = await reupgrade.NarrationJobs.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == legacyJobId, cancellationToken);
            Assert.Equal(NarrationArtifactVisibility.Published, retained.Visibility);
            Assert.Null(retained.SeriesId);
            Assert.Null(retained.CastRevisionId);
            Assert.Null(retained.SpeechPlanRevisionId);
            Assert.Null(retained.RebuildBatchId);
            Assert.Null(retained.RebuildMemberId);

            await reupgrade.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE narration_jobs SET \"Visibility\" = 'Historical' WHERE \"Id\" = {legacyJobId}",
                cancellationToken);
            var historicalGuard = await Assert.ThrowsAsync<PostgresException>(() =>
                reupgrade.GetService<IMigrator>().MigrateAsync(PreviousMigration, cancellationToken));
            Assert.Equal(PostgresErrorCodes.RaiseException, historicalGuard.SqlState);
            Assert.Contains("dependent rows exist", historicalGuard.MessageText, StringComparison.Ordinal);
        }

        await using var finalProof = CreateContext(graph.ConnectionString);
        Assert.Contains(CurrentMigration, await finalProof.Database.GetAppliedMigrationsAsync(cancellationToken));
        Assert.Equal(
            NarrationArtifactVisibility.Historical,
            (await finalProof.NarrationJobs.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == legacyJobId, cancellationToken)).Visibility);
    }

    private static async Task<GraphFixture> CreateGraphAsync(
        string connectionString,
        Guid ownerId,
        string prefix,
        bool includePointerCandidates,
        CancellationToken cancellationToken,
        string? targetMigration = null)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var createdAt = new DateTimeOffset(utcNow.Ticks - (utcNow.Ticks % 10), utcNow.Offset);
        var firstBook = Book.Create(ownerId, $"{prefix} first book", "Synthetic author", "en", $"{prefix}-first.txt");
        var secondBook = Book.Create(ownerId, $"{prefix} second book", "Synthetic author", "en", $"{prefix}-second.txt");
        var series = StorySeries.Create(
            ownerId,
            $"{prefix} series",
            "synthetic-provider",
            "synthetic-narrator",
            "+0%",
            "+0Hz",
            "+0%",
            250);
        var firstMembership = series.AddBook(firstBook, "Volume one", 1);
        var secondMembership = series.AddBook(secondBook, "Volume two", 2);
        var character = series.AddCharacter(
            "Synthetic Hero",
            SeriesCharacterRole.Main,
            "synthetic-provider",
            "synthetic-hero-voice",
            "+5%",
            "+1Hz",
            "-2%",
            "Synthetic notes");
        var revisionId = Guid.NewGuid();
        var assignment = NarrationCastAssignment.Create(
            Guid.NewGuid(),
            ownerId,
            series.Id,
            revisionId,
            character.Id,
            character.CanonicalName,
            "synthetic-provider",
            "provider-v1",
            "synthetic-hero-voice",
            "+5%",
            "+1Hz",
            "-2%");
        var revision = NarrationCastRevision.Create(
            revisionId,
            ownerId,
            series.Id,
            7,
            "synthetic-provider",
            "provider-v1",
            "synthetic-narrator",
            "+0%",
            "+0Hz",
            "+0%",
            250,
            500,
            "composition-v1",
            "mp3-128k",
            createdAt,
            [assignment]);
        var batchId = Guid.NewGuid();
        var member = SeriesCastRebuildMember.Create(
            Guid.NewGuid(),
            ownerId,
            series.Id,
            batchId,
            firstMembership.Id,
            firstBook.Id,
            firstMembership.MembershipRevision,
            null);
        var batch = SeriesCastRebuildBatch.Create(
            batchId,
            ownerId,
            series.Id,
            null,
            revision.Id,
            firstMembership.MembershipRevision,
            createdAt,
            [member]);

        Guid? siblingBatchId = null;
        Guid? siblingMemberId = null;
        Guid? otherBookBatchId = null;
        Guid? otherBookMemberId = null;
        SeriesCastRebuildBatch? siblingBatch = null;
        SeriesCastRebuildBatch? otherBookBatch = null;
        NarrationCastRevision? siblingRevision = null;
        NarrationCastRevision? otherBookRevision = null;
        if (includePointerCandidates)
        {
            // UX_rebuild_batches_draft_cast enforces one batch per (OwnerId, SeriesId,
            // DraftCastRevisionId), so each pointer-candidate batch needs its own revision
            // rather than reusing the primary `revision`.
            var siblingRevisionId = Guid.NewGuid();
            var siblingAssignment = NarrationCastAssignment.Create(
                Guid.NewGuid(),
                ownerId,
                series.Id,
                siblingRevisionId,
                character.Id,
                character.CanonicalName,
                "synthetic-provider",
                "provider-v1",
                "synthetic-hero-voice",
                "+5%",
                "+1Hz",
                "-2%");
            siblingRevision = NarrationCastRevision.Create(
                siblingRevisionId,
                ownerId,
                series.Id,
                9008,
                "synthetic-provider",
                "provider-v1",
                "synthetic-narrator",
                "+1%",
                "+0Hz",
                "+0%",
                250,
                500,
                "composition-v1",
                "mp3-128k",
                createdAt,
                [siblingAssignment]);

            var otherBookRevisionId = Guid.NewGuid();
            var otherBookAssignment = NarrationCastAssignment.Create(
                Guid.NewGuid(),
                ownerId,
                series.Id,
                otherBookRevisionId,
                character.Id,
                character.CanonicalName,
                "synthetic-provider",
                "provider-v1",
                "synthetic-hero-voice",
                "+5%",
                "+1Hz",
                "-2%");
            otherBookRevision = NarrationCastRevision.Create(
                otherBookRevisionId,
                ownerId,
                series.Id,
                9009,
                "synthetic-provider",
                "provider-v1",
                "synthetic-narrator",
                "+2%",
                "+0Hz",
                "+0%",
                250,
                500,
                "composition-v1",
                "mp3-128k",
                createdAt,
                [otherBookAssignment]);

            siblingBatchId = Guid.NewGuid();
            siblingMemberId = Guid.NewGuid();
            var siblingMember = SeriesCastRebuildMember.Create(
                siblingMemberId.Value,
                ownerId,
                series.Id,
                siblingBatchId.Value,
                firstMembership.Id,
                firstBook.Id,
                firstMembership.MembershipRevision,
                null);
            siblingBatch = SeriesCastRebuildBatch.Create(
                siblingBatchId.Value,
                ownerId,
                series.Id,
                null,
                siblingRevisionId,
                firstMembership.MembershipRevision,
                createdAt,
                [siblingMember]);

            otherBookBatchId = Guid.NewGuid();
            otherBookMemberId = Guid.NewGuid();
            var otherBookMember = SeriesCastRebuildMember.Create(
                otherBookMemberId.Value,
                ownerId,
                series.Id,
                otherBookBatchId.Value,
                secondMembership.Id,
                secondBook.Id,
                secondMembership.MembershipRevision,
                null);
            otherBookBatch = SeriesCastRebuildBatch.Create(
                otherBookBatchId.Value,
                ownerId,
                series.Id,
                null,
                otherBookRevisionId,
                secondMembership.MembershipRevision,
                createdAt,
                [otherBookMember]);
        }

        await using var db = CreateContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);
        db.Users.Add(CreateUser(ownerId, prefix));
        db.Books.AddRange(firstBook, secondBook);
        await db.SaveChangesAsync(cancellationToken);
        db.StorySeries.Add(series);
        db.NarrationCastRevisions.Add(revision);
        db.SeriesCastRebuildBatches.Add(batch);
        if (siblingRevision is not null)
        {
            db.NarrationCastRevisions.Add(siblingRevision);
        }

        if (otherBookRevision is not null)
        {
            db.NarrationCastRevisions.Add(otherBookRevision);
        }

        if (siblingBatch is not null)
        {
            db.SeriesCastRebuildBatches.Add(siblingBatch);
        }

        if (otherBookBatch is not null)
        {
            db.SeriesCastRebuildBatches.Add(otherBookBatch);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new GraphFixture(
            connectionString,
            ownerId,
            series.Id,
            firstBook.Id,
            secondBook.Id,
            firstMembership.Id,
            secondMembership.Id,
            character.Id,
            revision.Id,
            assignment.Id,
            revision.Fingerprint,
            batch.Id,
            member.Id,
            createdAt,
            siblingBatchId,
            siblingMemberId,
            otherBookBatchId,
            otherBookMemberId,
            null,
            null,
            null);
    }

    private static async Task StagePointerCandidatesAsync(GraphFixture graph, CancellationToken cancellationToken)
    {
        Assert.NotNull(graph.SiblingBatchId);
        Assert.NotNull(graph.SiblingMemberId);
        Assert.NotNull(graph.OtherBookBatchId);
        Assert.NotNull(graph.OtherBookMemberId);

        await using var db = CreateContext(graph.ConnectionString);
        var batchIds = new[] { graph.BatchId, graph.SiblingBatchId.Value, graph.OtherBookBatchId.Value };
        var batches = await db.SeriesCastRebuildBatches
            .Include(batch => batch.Members)
            .Where(batch => batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);
        var jobs = new List<NarrationJob>();
        foreach (var batchId in batchIds)
        {
            var batch = batches[batchId];
            batch.StartBuilding(graph.CreatedAt.AddMinutes(1));
            var member = Assert.Single(batch.Members);
            var job = CreateMultiCharacterStagedJob(
                graph.OwnerId,
                member.BookId,
                graph.SeriesId,
                batch.DraftCastRevisionId,
                Guid.NewGuid(),
                batch.Id,
                member.Id,
                $"pointer-{batch.Id:N}",
                graph.CreatedAt.AddMinutes(1));
            AttachStagedJob(member, job.Id);
            jobs.Add(job);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.NarrationJobs.AddRange(jobs);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        graph.PrimaryJobId = jobs.Single(job => job.RebuildBatchId == graph.BatchId).Id;
        graph.SiblingJobId = jobs.Single(job => job.RebuildBatchId == graph.SiblingBatchId).Id;
        graph.OtherBookJobId = jobs.Single(job => job.RebuildBatchId == graph.OtherBookBatchId).Id;
    }

    private static NarrationJob CreateMultiCharacterStagedJob(
        Guid ownerId,
        Guid bookId,
        Guid seriesId,
        Guid castRevisionId,
        Guid speechPlanRevisionId,
        Guid batchId,
        Guid memberId,
        string sourceHash,
        DateTimeOffset rightsAttestedAt)
    {
        var method = Assert.IsAssignableFrom<MethodInfo>(typeof(NarrationJob).GetMethod(
            "CreateMultiCharacterStaged",
            BindingFlags.Static | BindingFlags.NonPublic));
        return Assert.IsType<NarrationJob>(method.Invoke(
            null,
            [
                ownerId,
                bookId,
                bookId,
                seriesId,
                castRevisionId,
                speechPlanRevisionId,
                batchId,
                memberId,
                sourceHash,
                rightsAttestedAt
            ]));
    }

    private static void AttachStagedJob(SeriesCastRebuildMember member, Guid stagedJobId)
    {
        var method = Assert.IsAssignableFrom<MethodInfo>(typeof(SeriesCastRebuildMember).GetMethod(
            "AttachStagedJob",
            BindingFlags.Instance | BindingFlags.NonPublic));
        method.Invoke(member, [stagedJobId]);
    }

    private static Task<int> InsertLegacyJobAsync(
        StoryVoiceDbContext db,
        Guid jobId,
        Guid ownerId,
        Guid bookId,
        string sourceHash,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO narration_jobs
                ("Id", "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate",
                 "Status", "ProgressPercent", "Attempts", "CancellationRequested", "LeaseOwner",
                 "LeaseExpiresAt", "NextAttemptAt", "ErrorCode", "AudioRelativePath", "AudioBytes",
                 "RightsAttestedAt", "CreatedAt", "UpdatedAt", "CompletedAt", "ConcurrencyStamp")
            VALUES
                ({jobId}, {ownerId}, {bookId}, {bookId}, {sourceHash},
                 'synthetic-voice', '+0%', 'Queued', 0, 0, FALSE, NULL, NULL, {now},
                 NULL, NULL, NULL, {now}, {now}, {now}, NULL, {Guid.NewGuid()});
            """, cancellationToken);

    private static Task<int> InsertRevisionAsync(
        StoryVoiceDbContext db,
        Guid id,
        Guid ownerId,
        Guid seriesId,
        int revisionNumber,
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO narration_cast_revisions
                ("Id", "OwnerId", "SeriesId", "RevisionNumber", "Fingerprint", "Status", "EpochNumber",
                 "NarratorProvider", "NarratorProviderVersion", "NarratorVoice", "NarratorRate", "NarratorPitch",
                 "NarratorVolume", "DefaultSpeakerPauseMs", "ChapterPauseMs", "CompositionVersion", "FfmpegProfile",
                 "CreatedAt", "ActivatedAt")
            VALUES
                ({id}, {ownerId}, {seriesId}, {revisionNumber}, {fingerprint}, 'Draft', NULL,
                 'provider', 'v1', 'voice', '+0%', '+0Hz', '+0%', 0, 0, 'composition', 'profile', {now}, NULL);
            """, cancellationToken);

    private static Task<int> InsertAssignmentAsync(
        StoryVoiceDbContext db,
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid revisionId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO narration_cast_assignments
                ("Id", "OwnerId", "SeriesId", "CastRevisionId", "CharacterId", "CanonicalNameSnapshot",
                 "VoiceProvider", "ProviderVersion", "Voice", "Rate", "Pitch", "Volume")
            VALUES
                ({id}, {ownerId}, {seriesId}, {revisionId}, {characterId}, 'Synthetic Hero',
                 'provider', 'v1', 'voice', '+0%', '+0Hz', '+0%');
            """, cancellationToken);

    private static Task<int> InsertBatchAsync(
        StoryVoiceDbContext db,
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid? baseRevisionId,
        Guid draftRevisionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO series_cast_rebuild_batches
                ("Id", "OwnerId", "SeriesId", "BaseActiveCastRevisionId", "DraftCastRevisionId",
                 "CohortMembershipRevision", "Status", "CreatedAt", "UpdatedAt")
            VALUES
                ({id}, {ownerId}, {seriesId}, {baseRevisionId}, {draftRevisionId}, 1, 'Draft', {now}, {now});
            """, cancellationToken);

    private static Task<int> InsertMemberAsync(
        StoryVoiceDbContext db,
        Guid id,
        Guid ownerId,
        Guid seriesId,
        Guid batchId,
        Guid seriesBookId,
        Guid bookId,
        Guid? previousJobId,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO series_cast_rebuild_members
                ("Id", "OwnerId", "SeriesId", "BatchId", "SeriesBookId", "BookId", "MembershipRevision",
                 "StagedNarrationJobId", "PreviousActiveNarrationJobId", "Status")
            VALUES
                ({id}, {ownerId}, {seriesId}, {batchId}, {seriesBookId}, {bookId}, 1, NULL, {previousJobId}, 'Pending');
            """, cancellationToken);

    private static Task<int> InsertMultiCharacterJobAsync(
        StoryVoiceDbContext db,
        Guid id,
        Guid ownerId,
        Guid bookId,
        Guid seriesId,
        Guid castRevisionId,
        Guid speechPlanRevisionId,
        Guid batchId,
        Guid memberId,
        string sourceHash,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO narration_jobs
                ("Id", "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate", "Mode", "Visibility",
                 "SeriesId", "CastRevisionId", "SpeechPlanRevisionId", "RebuildBatchId", "RebuildMemberId", "Status",
                 "ProgressPercent", "Attempts", "CancellationRequested", "LeaseOwner", "LeaseExpiresAt", "NextAttemptAt",
                 "ErrorCode", "AudioRelativePath", "AudioBytes", "RightsAttestedAt", "CreatedAt", "UpdatedAt", "CompletedAt",
                 "ConcurrencyStamp")
            VALUES
                ({id}, {ownerId}, {bookId}, {bookId}, {sourceHash}, 'speech-plan', 'per-segment', 'MultiCharacter', 'Staged',
                 {seriesId}, {castRevisionId}, {speechPlanRevisionId}, {batchId}, {memberId}, 'Queued', 0, 0, FALSE, NULL, NULL,
                 {now}, NULL, NULL, NULL, {now}, {now}, {now}, NULL, {Guid.NewGuid()});
            """, cancellationToken);

    private static async Task AssertPostgresErrorAsync(
        Func<Task<int>> action,
        string sqlState,
        string constraintName)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

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
            .AddInterceptors(new HistoricalBooksSchemaCompatibilityInterceptor())
            .Options;
        return new StoryVoiceDbContext(options);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        try
        {
            await postgres.StartAsync(cancellationToken);
            return postgres;
        }
        catch
        {
            await postgres.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        string connectionString,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = @tableName AND column_name = @columnName)",
            connection);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("columnName", columnName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> CountJobsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT COUNT(*)::int FROM narration_jobs", connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed class GraphFixture(
        string connectionString,
        Guid ownerId,
        Guid seriesId,
        Guid bookId,
        Guid otherBookId,
        Guid seriesBookId,
        Guid otherSeriesBookId,
        Guid characterId,
        Guid revisionId,
        Guid assignmentId,
        string fingerprint,
        Guid batchId,
        Guid memberId,
        DateTimeOffset createdAt,
        Guid? siblingBatchId,
        Guid? siblingMemberId,
        Guid? otherBookBatchId,
        Guid? otherBookMemberId,
        Guid? primaryJobId,
        Guid? siblingJobId,
        Guid? otherBookJobId)
    {
        public string ConnectionString { get; } = connectionString;
        public Guid OwnerId { get; } = ownerId;
        public Guid SeriesId { get; } = seriesId;
        public Guid BookId { get; } = bookId;
        public Guid OtherBookId { get; } = otherBookId;
        public Guid SeriesBookId { get; } = seriesBookId;
        public Guid OtherSeriesBookId { get; } = otherSeriesBookId;
        public Guid CharacterId { get; } = characterId;
        public Guid RevisionId { get; } = revisionId;
        public Guid AssignmentId { get; } = assignmentId;
        public string Fingerprint { get; } = fingerprint;
        public Guid BatchId { get; } = batchId;
        public Guid MemberId { get; } = memberId;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public Guid? SiblingBatchId { get; } = siblingBatchId;
        public Guid? SiblingMemberId { get; } = siblingMemberId;
        public Guid? OtherBookBatchId { get; } = otherBookBatchId;
        public Guid? OtherBookMemberId { get; } = otherBookMemberId;
        public Guid? PrimaryJobId { get; set; } = primaryJobId;
        public Guid? SiblingJobId { get; set; } = siblingJobId;
        public Guid? OtherBookJobId { get; set; } = otherBookJobId;
    }
}
