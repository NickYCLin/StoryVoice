using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCastRebuildPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CastRevisionId",
                table: "narration_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RebuildBatchId",
                table: "narration_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RebuildMemberId",
                table: "narration_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "narration_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpeechPlanRevisionId",
                table: "narration_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "narration_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_series_books_rebuild_scope",
                table: "series_books",
                columns: new[] { "OwnerId", "SeriesId", "Id", "BookId", "MembershipRevision" });

            migrationBuilder.CreateTable(
                name: "narration_cast_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EpochNumber = table.Column<int>(type: "integer", nullable: true),
                    NarratorProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorProviderVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorVoice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NarratorRate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorPitch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorVolume = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefaultSpeakerPauseMs = table.Column<int>(type: "integer", nullable: false),
                    ChapterPauseMs = table.Column<int>(type: "integer", nullable: false),
                    CompositionVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FfmpegProfile = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_narration_cast_revisions", x => x.Id);
                    table.UniqueConstraint("AK_ncast_revs_scope", x => new { x.OwnerId, x.SeriesId, x.Id });
                    table.CheckConstraint("CK_ncast_revs_chronology", "\"ActivatedAt\" IS NULL OR \"ActivatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_ncast_revs_epoch", "(\"Status\" = 'Draft' AND \"EpochNumber\" IS NULL AND \"ActivatedAt\" IS NULL) OR (\"Status\" IN ('Active', 'Historical') AND \"EpochNumber\" IS NOT NULL AND \"EpochNumber\" >= 1 AND \"ActivatedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_ncast_revs_fingerprint", "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ncast_revs_pauses", "\"DefaultSpeakerPauseMs\" >= 0 AND \"DefaultSpeakerPauseMs\" <= 60000 AND \"ChapterPauseMs\" >= 0 AND \"ChapterPauseMs\" <= 60000");
                    table.CheckConstraint("CK_ncast_revs_revision", "\"RevisionNumber\" >= 1");
                    table.CheckConstraint("CK_ncast_revs_status", "\"Status\" IN ('Draft', 'Active', 'Historical')");
                    table.ForeignKey(
                        name: "FK_ncast_revs_series_scope",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "narration_cast_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    CastRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VoiceProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Voice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Pitch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Volume = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_narration_cast_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ncast_asgn_character_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.CharacterId },
                        principalTable: "series_characters",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ncast_asgn_revision_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.CastRevisionId },
                        principalTable: "narration_cast_revisions",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_cast_rebuild_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseActiveCastRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftCastRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CohortMembershipRevision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_cast_rebuild_batches", x => x.Id);
                    table.UniqueConstraint("AK_rebuild_batches_job_cast", x => new { x.OwnerId, x.SeriesId, x.Id, x.DraftCastRevisionId });
                    table.UniqueConstraint("AK_rebuild_batches_scope", x => new { x.OwnerId, x.SeriesId, x.Id });
                    table.CheckConstraint("CK_rebuild_batches_chronology", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_rebuild_batches_cohort", "\"CohortMembershipRevision\" >= 1");
                    table.CheckConstraint("CK_rebuild_batches_revisions", "\"BaseActiveCastRevisionId\" IS NULL OR \"BaseActiveCastRevisionId\" <> \"DraftCastRevisionId\"");
                    table.CheckConstraint("CK_rebuild_batches_status", "\"Status\" IN ('Draft', 'Building', 'ReadyToActivate', 'Activated', 'Failed')");
                    table.ForeignKey(
                        name: "FK_rebuild_batches_base_cast",
                        columns: x => new { x.OwnerId, x.SeriesId, x.BaseActiveCastRevisionId },
                        principalTable: "narration_cast_revisions",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rebuild_batches_draft_cast",
                        columns: x => new { x.OwnerId, x.SeriesId, x.DraftCastRevisionId },
                        principalTable: "narration_cast_revisions",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rebuild_batches_series_scope",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_cast_rebuild_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipRevision = table.Column<int>(type: "integer", nullable: false),
                    StagedNarrationJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousActiveNarrationJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_cast_rebuild_members", x => x.Id);
                    table.UniqueConstraint("AK_rebuild_members_scope", x => new { x.OwnerId, x.SeriesId, x.BatchId, x.Id });
                    table.CheckConstraint("CK_rebuild_members_distinct_jobs", "\"StagedNarrationJobId\" IS NULL OR \"PreviousActiveNarrationJobId\" IS NULL OR \"StagedNarrationJobId\" <> \"PreviousActiveNarrationJobId\"");
                    table.CheckConstraint("CK_rebuild_members_pointer", "(\"Status\" = 'Pending' AND \"StagedNarrationJobId\" IS NULL) OR (\"Status\" IN ('Building', 'Ready') AND \"StagedNarrationJobId\" IS NOT NULL) OR \"Status\" = 'Failed'");
                    table.CheckConstraint("CK_rebuild_members_revision", "\"MembershipRevision\" >= 1");
                    table.CheckConstraint("CK_rebuild_members_status", "\"Status\" IN ('Pending', 'Building', 'Ready', 'Failed')");
                    table.ForeignKey(
                        name: "FK_rebuild_members_batch_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.BatchId },
                        principalTable: "series_cast_rebuild_batches",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rebuild_members_series_book",
                        columns: x => new { x.OwnerId, x.SeriesId, x.SeriesBookId, x.BookId, x.MembershipRevision },
                        principalTable: "series_books",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id", "BookId", "MembershipRevision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_njobs_batch_cast",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "RebuildBatchId", "CastRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_njobs_cast_scope",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "CastRevisionId" });

            migrationBuilder.CreateIndex(
                name: "UX_njobs_member_artifact",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "RebuildBatchId", "RebuildMemberId", "BookId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_njobs_multi_member",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "RebuildBatchId", "RebuildMemberId" },
                unique: true,
                filter: "\"Mode\" = 'MultiCharacter'");

            migrationBuilder.CreateIndex(
                name: "UX_njobs_owner_book_id",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "BookId", "Id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_njobs_correlations",
                table: "narration_jobs",
                sql: "(\"Mode\" = 'SingleVoice' AND \"Visibility\" <> 'Staged' AND \"SeriesId\" IS NULL AND \"CastRevisionId\" IS NULL AND \"SpeechPlanRevisionId\" IS NULL AND \"RebuildBatchId\" IS NULL AND \"RebuildMemberId\" IS NULL) OR (\"Mode\" = 'MultiCharacter' AND \"SeriesId\" IS NOT NULL AND \"CastRevisionId\" IS NOT NULL AND \"SpeechPlanRevisionId\" IS NOT NULL AND \"RebuildBatchId\" IS NOT NULL AND \"RebuildMemberId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_njobs_published_audio",
                table: "narration_jobs",
                sql: "\"Mode\" <> 'MultiCharacter' OR \"Visibility\" = 'Staged' OR (\"Visibility\" IN ('Published', 'Historical') AND \"Status\" = 'Completed' AND \"AudioRelativePath\" IS NOT NULL AND \"AudioBytes\" IS NOT NULL AND \"AudioBytes\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_njobs_visibility",
                table: "narration_jobs",
                sql: "\"Visibility\" IN ('Staged', 'Published', 'Historical')");

            migrationBuilder.CreateIndex(
                name: "IX_narration_cast_assignments_OwnerId_SeriesId_CharacterId",
                table: "narration_cast_assignments",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "UX_ncast_asgn_character",
                table: "narration_cast_assignments",
                columns: new[] { "OwnerId", "SeriesId", "CastRevisionId", "CharacterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ncast_revs_active",
                table: "narration_cast_revisions",
                columns: new[] { "OwnerId", "SeriesId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_ncast_revs_epoch",
                table: "narration_cast_revisions",
                columns: new[] { "OwnerId", "SeriesId", "EpochNumber" },
                unique: true,
                filter: "\"EpochNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ncast_revs_fingerprint",
                table: "narration_cast_revisions",
                columns: new[] { "OwnerId", "SeriesId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ncast_revs_number",
                table: "narration_cast_revisions",
                columns: new[] { "OwnerId", "SeriesId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rebuild_batches_base_cast",
                table: "series_cast_rebuild_batches",
                columns: new[] { "OwnerId", "SeriesId", "BaseActiveCastRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_rebuild_batches_draft_cast",
                table: "series_cast_rebuild_batches",
                columns: new[] { "OwnerId", "SeriesId", "DraftCastRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_rebuild_members_series_book",
                table: "series_cast_rebuild_members",
                columns: new[] { "OwnerId", "SeriesId", "SeriesBookId", "BookId", "MembershipRevision" });

            migrationBuilder.CreateIndex(
                name: "UX_rebuild_members_book",
                table: "series_cast_rebuild_members",
                columns: new[] { "OwnerId", "SeriesId", "BatchId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_rebuild_members_series_book",
                table: "series_cast_rebuild_members",
                columns: new[] { "OwnerId", "SeriesId", "BatchId", "SeriesBookId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_njobs_batch_cast",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "RebuildBatchId", "CastRevisionId" },
                principalTable: "series_cast_rebuild_batches",
                principalColumns: new[] { "OwnerId", "SeriesId", "Id", "DraftCastRevisionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_njobs_cast_scope",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "CastRevisionId" },
                principalTable: "narration_cast_revisions",
                principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_njobs_rebuild_member_scope",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId", "RebuildBatchId", "RebuildMemberId" },
                principalTable: "series_cast_rebuild_members",
                principalColumns: new[] { "OwnerId", "SeriesId", "BatchId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_njobs_series_scope",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "SeriesId" },
                principalTable: "story_series",
                principalColumns: new[] { "OwnerId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_cast_rebuild_members"
                ADD CONSTRAINT "FK_rebuild_members_book_owner"
                FOREIGN KEY ("OwnerId", "BookId")
                REFERENCES "books" ("OwnerId", "Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_cast_rebuild_members"
                ADD CONSTRAINT "FK_rebuild_members_staged_job"
                FOREIGN KEY ("OwnerId", "SeriesId", "BatchId", "Id", "BookId", "StagedNarrationJobId")
                REFERENCES "narration_jobs" ("OwnerId", "SeriesId", "RebuildBatchId", "RebuildMemberId", "BookId", "Id")
                ON DELETE NO ACTION
                DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_cast_rebuild_members"
                ADD CONSTRAINT "FK_rebuild_members_previous_job"
                FOREIGN KEY ("OwnerId", "BookId", "PreviousActiveNarrationJobId")
                REFERENCES "narration_jobs" ("OwnerId", "BookId", "Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION check_rebuild_artifact_visibility()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "series_cast_rebuild_members" AS member
                        INNER JOIN "series_cast_rebuild_batches" AS batch
                            ON batch."OwnerId" = member."OwnerId"
                            AND batch."SeriesId" = member."SeriesId"
                            AND batch."Id" = member."BatchId"
                        INNER JOIN "narration_jobs" AS job
                            ON job."OwnerId" = member."OwnerId"
                            AND job."SeriesId" = member."SeriesId"
                            AND job."RebuildBatchId" = member."BatchId"
                            AND job."RebuildMemberId" = member."Id"
                            AND job."BookId" = member."BookId"
                            AND job."Id" = member."StagedNarrationJobId"
                        WHERE member."StagedNarrationJobId" IS NOT NULL
                            AND (
                                (batch."Status" = 'Activated'
                                    AND job."Visibility" NOT IN ('Published', 'Historical'))
                                OR (batch."Status" <> 'Activated'
                                    AND job."Visibility" <> 'Staged')))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_rebuild_artifact_visibility',
                            MESSAGE = 'Rebuild artifact visibility does not match the final batch lifecycle state.';
                    END IF;

                    RETURN NULL;
                END
                $function$;

                CREATE CONSTRAINT TRIGGER "CT_rebuild_artifact_member"
                AFTER INSERT OR UPDATE OR DELETE ON "series_cast_rebuild_members"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_rebuild_artifact_visibility();

                CREATE CONSTRAINT TRIGGER "CT_rebuild_artifact_job"
                AFTER INSERT OR UPDATE OR DELETE ON "narration_jobs"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_rebuild_artifact_visibility();

                CREATE CONSTRAINT TRIGGER "CT_rebuild_artifact_batch"
                AFTER INSERT OR UPDATE OR DELETE ON "series_cast_rebuild_batches"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_rebuild_artifact_visibility();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "narration_cast_revisions")
                        OR EXISTS (SELECT 1 FROM "narration_cast_assignments")
                        OR EXISTS (SELECT 1 FROM "series_cast_rebuild_batches")
                        OR EXISTS (SELECT 1 FROM "series_cast_rebuild_members")
                        OR EXISTS (
                            SELECT 1
                            FROM "narration_jobs"
                            WHERE "Mode" IS DISTINCT FROM 'SingleVoice'
                                OR "Visibility" IS DISTINCT FROM 'Published'
                                OR "SeriesId" IS NOT NULL
                                OR "CastRevisionId" IS NOT NULL
                                OR "SpeechPlanRevisionId" IS NOT NULL
                                OR "RebuildBatchId" IS NOT NULL
                                OR "RebuildMemberId" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Cannot roll back cast rebuild persistence while dependent rows exist.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "CT_rebuild_artifact_member" ON "series_cast_rebuild_members";
                DROP TRIGGER IF EXISTS "CT_rebuild_artifact_job" ON "narration_jobs";
                DROP TRIGGER IF EXISTS "CT_rebuild_artifact_batch" ON "series_cast_rebuild_batches";
                DROP FUNCTION IF EXISTS check_rebuild_artifact_visibility();

                ALTER TABLE "series_cast_rebuild_members"
                DROP CONSTRAINT IF EXISTS "FK_rebuild_members_previous_job";
                ALTER TABLE "series_cast_rebuild_members"
                DROP CONSTRAINT IF EXISTS "FK_rebuild_members_staged_job";
                ALTER TABLE "series_cast_rebuild_members"
                DROP CONSTRAINT IF EXISTS "FK_rebuild_members_book_owner";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_njobs_batch_cast",
                table: "narration_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_njobs_cast_scope",
                table: "narration_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_njobs_rebuild_member_scope",
                table: "narration_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_njobs_series_scope",
                table: "narration_jobs");

            migrationBuilder.DropTable(
                name: "narration_cast_assignments");

            migrationBuilder.DropTable(
                name: "series_cast_rebuild_members");

            migrationBuilder.DropTable(
                name: "series_cast_rebuild_batches");

            migrationBuilder.DropTable(
                name: "narration_cast_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_series_books_rebuild_scope",
                table: "series_books");

            migrationBuilder.DropIndex(
                name: "IX_njobs_batch_cast",
                table: "narration_jobs");

            migrationBuilder.DropIndex(
                name: "IX_njobs_cast_scope",
                table: "narration_jobs");

            migrationBuilder.DropIndex(
                name: "UX_njobs_member_artifact",
                table: "narration_jobs");

            migrationBuilder.DropIndex(
                name: "UX_njobs_multi_member",
                table: "narration_jobs");

            migrationBuilder.DropIndex(
                name: "UX_njobs_owner_book_id",
                table: "narration_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_njobs_correlations",
                table: "narration_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_njobs_published_audio",
                table: "narration_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_njobs_visibility",
                table: "narration_jobs");

            migrationBuilder.DropColumn(
                name: "CastRevisionId",
                table: "narration_jobs");

            migrationBuilder.DropColumn(
                name: "RebuildBatchId",
                table: "narration_jobs");

            migrationBuilder.DropColumn(
                name: "RebuildMemberId",
                table: "narration_jobs");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "narration_jobs");

            migrationBuilder.DropColumn(
                name: "SpeechPlanRevisionId",
                table: "narration_jobs");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "narration_jobs");
        }
    }
}
