using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtomicCastEpochActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rebuild_batches_draft_cast",
                table: "series_cast_rebuild_batches");

            migrationBuilder.CreateIndex(
                name: "UX_rebuild_batches_draft_cast",
                table: "series_cast_rebuild_batches",
                columns: new[] { "OwnerId", "SeriesId", "DraftCastRevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rebuild_members_previous_job",
                table: "series_cast_rebuild_members",
                column: "PreviousActiveNarrationJobId",
                filter: "\"PreviousActiveNarrationJobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_series_books_active_job",
                table: "series_books",
                column: "ActiveNarrationJobId",
                filter: "\"ActiveNarrationJobId\" IS NOT NULL");

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "CT_rebuild_artifact_member" ON "series_cast_rebuild_members";
                DROP TRIGGER IF EXISTS "CT_rebuild_artifact_job" ON "narration_jobs";
                DROP TRIGGER IF EXISTS "CT_rebuild_artifact_batch" ON "series_cast_rebuild_batches";
                DROP FUNCTION IF EXISTS check_rebuild_artifact_visibility();

                ALTER TABLE "story_series"
                ADD CONSTRAINT "FK_story_series_active_cast"
                FOREIGN KEY ("OwnerId", "Id", "ActiveCastRevisionId")
                REFERENCES "narration_cast_revisions" ("OwnerId", "SeriesId", "Id")
                ON DELETE NO ACTION
                DEFERRABLE INITIALLY DEFERRED;

                ALTER TABLE "series_books"
                ADD CONSTRAINT "FK_series_books_active_job"
                FOREIGN KEY ("OwnerId", "BookId", "ActiveNarrationJobId")
                REFERENCES "narration_jobs" ("OwnerId", "BookId", "Id")
                ON DELETE NO ACTION
                DEFERRABLE INITIALLY DEFERRED;

                CREATE FUNCTION is_safe_narration_audio(audio_path text, audio_bytes bigint)
                RETURNS boolean
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                AS $function$
                    SELECT COALESCE(
                        audio_bytes >= 1
                        AND audio_path IS NOT NULL
                        AND char_length(audio_path) BETWEEN 1 AND 1000
                        AND btrim(audio_path) <> ''
                        AND replace(audio_path, E'\\', '/') NOT LIKE '/%'
                        AND replace(audio_path, E'\\', '/') !~ '^[A-Za-z]:'
                        AND replace(audio_path, E'\\', '/') !~ '(^|/)\.\.?(/|$)',
                        FALSE)
                $function$;

                CREATE FUNCTION assert_cast_epoch_integrity(target_owner_id uuid, target_series_id uuid)
                RETURNS void
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    active_revision_id uuid;
                BEGIN
                    SELECT series."ActiveCastRevisionId"
                    INTO active_revision_id
                    FROM "story_series" AS series
                    WHERE series."OwnerId" = target_owner_id
                        AND series."Id" = target_series_id;

                    IF NOT FOUND THEN
                        RETURN;
                    END IF;

                    -- Let the exact deferred pointer FKs report their own stable names before
                    -- the semantic graph checks run.  Without these guards, the custom trigger
                    -- can mask a cross-scope/missing pointer as an unrelated check violation.
                    IF active_revision_id IS NOT NULL AND NOT EXISTS (
                        SELECT 1
                        FROM "narration_cast_revisions" AS revision
                        WHERE revision."OwnerId" = target_owner_id
                            AND revision."SeriesId" = target_series_id
                            AND revision."Id" = active_revision_id)
                    THEN
                        RETURN;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "series_books" AS series_book
                        WHERE series_book."OwnerId" = target_owner_id
                            AND series_book."SeriesId" = target_series_id
                            AND series_book."ActiveNarrationJobId" IS NOT NULL
                            AND NOT EXISTS (
                                SELECT 1
                                FROM "narration_jobs" AS active_job
                                WHERE active_job."OwnerId" = series_book."OwnerId"
                                    AND active_job."BookId" = series_book."BookId"
                                    AND active_job."Id" = series_book."ActiveNarrationJobId"))
                    THEN
                        RETURN;
                    END IF;

                    IF (active_revision_id IS NULL AND (
                            EXISTS (
                                SELECT 1
                                FROM "series_cast_rebuild_batches" AS batch
                                WHERE batch."OwnerId" = target_owner_id
                                    AND batch."SeriesId" = target_series_id
                                    AND batch."Status" = 'Activated')
                            OR EXISTS (
                                SELECT 1
                                FROM "narration_cast_revisions" AS revision
                                WHERE revision."OwnerId" = target_owner_id
                                    AND revision."SeriesId" = target_series_id
                                    AND revision."Status" = 'Active')))
                        OR (active_revision_id IS NOT NULL AND (
                            (SELECT count(*)
                             FROM "series_cast_rebuild_batches" AS batch
                             WHERE batch."OwnerId" = target_owner_id
                                 AND batch."SeriesId" = target_series_id
                                 AND batch."Status" = 'Activated'
                                 AND batch."DraftCastRevisionId" = active_revision_id) <> 1
                            OR EXISTS (
                                SELECT 1
                                FROM "narration_cast_revisions" AS revision
                                WHERE revision."OwnerId" = target_owner_id
                                    AND revision."SeriesId" = target_series_id
                                    AND revision."Status" = 'Active'
                                    AND revision."Id" <> active_revision_id)))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_active_pointer',
                            MESSAGE = 'The active cast pointer is inconsistent with the activated graph.';
                    END IF;

                    IF (active_revision_id IS NOT NULL AND EXISTS (
                            SELECT 1
                            FROM "series_cast_rebuild_batches" AS batch
                            INNER JOIN "narration_cast_revisions" AS revision
                                ON revision."OwnerId" = batch."OwnerId"
                                AND revision."SeriesId" = batch."SeriesId"
                                AND revision."Id" = batch."DraftCastRevisionId"
                            WHERE batch."OwnerId" = target_owner_id
                                AND batch."SeriesId" = target_series_id
                                AND batch."Status" = 'Activated'
                                AND ((batch."DraftCastRevisionId" = active_revision_id
                                        AND revision."Status" <> 'Active')
                                    OR (batch."DraftCastRevisionId" <> active_revision_id
                                        AND revision."Status" <> 'Historical'))))
                        OR EXISTS (
                            SELECT 1
                            FROM "narration_cast_revisions" AS revision
                            WHERE revision."OwnerId" = target_owner_id
                                AND revision."SeriesId" = target_series_id
                                AND revision."Status" IN ('Active', 'Historical')
                                AND (SELECT count(*)
                                     FROM "series_cast_rebuild_batches" AS batch
                                     WHERE batch."OwnerId" = revision."OwnerId"
                                         AND batch."SeriesId" = revision."SeriesId"
                                         AND batch."DraftCastRevisionId" = revision."Id"
                                         AND batch."Status" = 'Activated') <> 1)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_revision_state',
                            MESSAGE = 'Cast revision state is inconsistent with the activated graph.';
                    END IF;

                    IF EXISTS (
                            SELECT 1
                            FROM "series_cast_rebuild_batches" AS batch
                            INNER JOIN "narration_cast_revisions" AS draft
                                ON draft."OwnerId" = batch."OwnerId"
                                AND draft."SeriesId" = batch."SeriesId"
                                AND draft."Id" = batch."DraftCastRevisionId"
                            LEFT JOIN "narration_cast_revisions" AS base
                                ON base."OwnerId" = batch."OwnerId"
                                AND base."SeriesId" = batch."SeriesId"
                                AND base."Id" = batch."BaseActiveCastRevisionId"
                            WHERE batch."OwnerId" = target_owner_id
                                AND batch."SeriesId" = target_series_id
                                AND batch."Status" = 'Activated'
                                AND (draft."EpochNumber" IS NULL
                                    OR draft."ActivatedAt" IS NULL
                                    OR (batch."BaseActiveCastRevisionId" IS NULL
                                        AND draft."EpochNumber" <> 1)
                                    OR (batch."BaseActiveCastRevisionId" IS NOT NULL
                                        AND (draft."EpochNumber" <= 1
                                            OR base."Id" IS NULL
                                            OR base."Status" <> 'Historical'
                                            OR base."EpochNumber" IS NULL
                                            OR draft."EpochNumber" <> base."EpochNumber" + 1))))
                        OR (active_revision_id IS NOT NULL AND (
                            SELECT current_revision."EpochNumber"
                            FROM "narration_cast_revisions" AS current_revision
                            WHERE current_revision."OwnerId" = target_owner_id
                                AND current_revision."SeriesId" = target_series_id
                                AND current_revision."Id" = active_revision_id)
                            IS DISTINCT FROM (
                                SELECT max(revision."EpochNumber")
                                FROM "narration_cast_revisions" AS revision
                                WHERE revision."OwnerId" = target_owner_id
                                    AND revision."SeriesId" = target_series_id
                                    AND revision."Status" IN ('Active', 'Historical')))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_batch_chain',
                            MESSAGE = 'The activated cast epoch chain is incoherent.';
                    END IF;

                    IF active_revision_id IS NOT NULL AND EXISTS (
                        SELECT 1
                        FROM "series_cast_rebuild_batches" AS batch
                        WHERE batch."OwnerId" = target_owner_id
                            AND batch."SeriesId" = target_series_id
                            AND batch."Status" = 'Activated'
                            AND batch."DraftCastRevisionId" = active_revision_id
                            AND ((SELECT count(*)
                                  FROM "series_cast_rebuild_members" AS member
                                  WHERE member."OwnerId" = batch."OwnerId"
                                      AND member."SeriesId" = batch."SeriesId"
                                      AND member."BatchId" = batch."Id") = 0
                                OR EXISTS (
                                    SELECT 1
                                    FROM "series_books" AS series_book
                                    WHERE series_book."OwnerId" = batch."OwnerId"
                                        AND series_book."SeriesId" = batch."SeriesId"
                                        AND NOT EXISTS (
                                            SELECT 1
                                            FROM "series_cast_rebuild_members" AS member
                                            WHERE member."OwnerId" = series_book."OwnerId"
                                                AND member."SeriesId" = series_book."SeriesId"
                                                AND member."BatchId" = batch."Id"
                                                AND member."SeriesBookId" = series_book."Id"
                                                AND member."BookId" = series_book."BookId"
                                                AND member."MembershipRevision" = series_book."MembershipRevision"))
                                OR EXISTS (
                                    SELECT 1
                                    FROM "series_cast_rebuild_members" AS member
                                    WHERE member."OwnerId" = batch."OwnerId"
                                        AND member."SeriesId" = batch."SeriesId"
                                        AND member."BatchId" = batch."Id"
                                        AND NOT EXISTS (
                                            SELECT 1
                                            FROM "series_books" AS series_book
                                            WHERE series_book."OwnerId" = member."OwnerId"
                                                AND series_book."SeriesId" = member."SeriesId"
                                                AND series_book."Id" = member."SeriesBookId"
                                                AND series_book."BookId" = member."BookId"
                                                AND series_book."MembershipRevision" = member."MembershipRevision"))
                                OR batch."CohortMembershipRevision" IS DISTINCT FROM (
                                    SELECT max(member."MembershipRevision")
                                    FROM "series_cast_rebuild_members" AS member
                                    WHERE member."OwnerId" = batch."OwnerId"
                                        AND member."SeriesId" = batch."SeriesId"
                                        AND member."BatchId" = batch."Id")))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_full_cohort',
                            MESSAGE = 'The current activated batch does not exactly match the current cohort.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "series_cast_rebuild_batches" AS batch
                        INNER JOIN "series_cast_rebuild_members" AS member
                            ON member."OwnerId" = batch."OwnerId"
                            AND member."SeriesId" = batch."SeriesId"
                            AND member."BatchId" = batch."Id"
                        WHERE batch."OwnerId" = target_owner_id
                            AND batch."SeriesId" = target_series_id
                            AND batch."Status" = 'Activated'
                            AND (member."Status" <> 'Ready'
                                OR member."StagedNarrationJobId" IS NULL))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_member_state',
                            MESSAGE = 'An activated batch contains a non-ready member.';
                    END IF;

                    IF active_revision_id IS NOT NULL AND EXISTS (
                        SELECT 1
                        FROM "series_cast_rebuild_batches" AS batch
                        INNER JOIN "series_cast_rebuild_members" AS member
                            ON member."OwnerId" = batch."OwnerId"
                            AND member."SeriesId" = batch."SeriesId"
                            AND member."BatchId" = batch."Id"
                        INNER JOIN "series_books" AS series_book
                            ON series_book."OwnerId" = member."OwnerId"
                            AND series_book."SeriesId" = member."SeriesId"
                            AND series_book."Id" = member."SeriesBookId"
                        WHERE batch."OwnerId" = target_owner_id
                            AND batch."SeriesId" = target_series_id
                            AND batch."Status" = 'Activated'
                            AND batch."DraftCastRevisionId" = active_revision_id
                            AND series_book."ActiveNarrationJobId" IS DISTINCT FROM member."StagedNarrationJobId")
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_current_pointer',
                            MESSAGE = 'A current series book does not point to its current activated artifact.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "narration_jobs" AS job
                        WHERE job."OwnerId" = target_owner_id
                            AND job."SeriesId" = target_series_id
                            AND job."Mode" = 'MultiCharacter'
                            AND NOT EXISTS (
                                SELECT 1
                                FROM "series_cast_rebuild_members" AS member
                                WHERE member."OwnerId" = job."OwnerId"
                                    AND member."SeriesId" = job."SeriesId"
                                    AND member."BatchId" = job."RebuildBatchId"
                                    AND member."Id" = job."RebuildMemberId"
                                    AND member."BookId" = job."BookId"
                                    AND member."StagedNarrationJobId" = job."Id"))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_rebuild_artifact_membership',
                            MESSAGE = 'A multi-character artifact is not the exact job named by its rebuild member.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "series_cast_rebuild_batches" AS batch
                        INNER JOIN "series_cast_rebuild_members" AS member
                            ON member."OwnerId" = batch."OwnerId"
                            AND member."SeriesId" = batch."SeriesId"
                            AND member."BatchId" = batch."Id"
                        INNER JOIN "narration_jobs" AS job
                            ON job."OwnerId" = member."OwnerId"
                            AND job."SeriesId" = member."SeriesId"
                            AND job."RebuildBatchId" = member."BatchId"
                            AND job."RebuildMemberId" = member."Id"
                            AND job."BookId" = member."BookId"
                            AND job."Id" = member."StagedNarrationJobId"
                        WHERE batch."OwnerId" = target_owner_id
                            AND batch."SeriesId" = target_series_id
                            AND ((batch."Status" <> 'Activated' AND job."Visibility" <> 'Staged')
                                OR (batch."Status" = 'Activated'
                                    AND batch."DraftCastRevisionId" = active_revision_id
                                    AND (job."Visibility" <> 'Published'
                                        OR job."Status" <> 'Completed'
                                        OR NOT is_safe_narration_audio(job."AudioRelativePath", job."AudioBytes")))
                                OR (batch."Status" = 'Activated'
                                    AND batch."DraftCastRevisionId" <> active_revision_id
                                    AND (job."Visibility" <> 'Historical'
                                        OR job."Status" <> 'Completed'
                                        OR NOT is_safe_narration_audio(job."AudioRelativePath", job."AudioBytes")))))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_rebuild_artifact_visibility',
                            MESSAGE = 'Rebuild artifact visibility does not match the final cast epoch state.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "series_cast_rebuild_batches" AS batch
                        INNER JOIN "series_cast_rebuild_members" AS member
                            ON member."OwnerId" = batch."OwnerId"
                            AND member."SeriesId" = batch."SeriesId"
                            AND member."BatchId" = batch."Id"
                        LEFT JOIN "narration_jobs" AS previous_job
                            ON previous_job."Id" = member."PreviousActiveNarrationJobId"
                        WHERE batch."OwnerId" = target_owner_id
                            AND batch."SeriesId" = target_series_id
                            AND batch."Status" = 'Activated'
                            AND ((member."PreviousActiveNarrationJobId" IS NOT NULL
                                    AND (previous_job."Id" IS NULL
                                        OR previous_job."OwnerId" <> member."OwnerId"
                                        OR previous_job."BookId" <> member."BookId"
                                        OR previous_job."Status" <> 'Completed'
                                        OR previous_job."Visibility" <> 'Historical'
                                        OR NOT is_safe_narration_audio(
                                            previous_job."AudioRelativePath",
                                            previous_job."AudioBytes")))
                                OR (batch."BaseActiveCastRevisionId" IS NULL
                                    AND member."PreviousActiveNarrationJobId" IS NOT NULL
                                    AND (previous_job."Mode" <> 'SingleVoice'
                                        OR previous_job."SeriesId" IS NOT NULL
                                        OR previous_job."CastRevisionId" IS NOT NULL
                                        OR previous_job."RebuildBatchId" IS NOT NULL
                                        OR previous_job."RebuildMemberId" IS NOT NULL))
                                OR (batch."BaseActiveCastRevisionId" IS NOT NULL
                                    AND (member."PreviousActiveNarrationJobId" IS NULL
                                        OR previous_job."Mode" <> 'MultiCharacter'
                                        OR previous_job."SeriesId" <> batch."SeriesId"
                                        OR previous_job."CastRevisionId" <> batch."BaseActiveCastRevisionId"
                                        OR NOT EXISTS (
                                            SELECT 1
                                            FROM "series_cast_rebuild_batches" AS predecessor_batch
                                            INNER JOIN "series_cast_rebuild_members" AS predecessor_member
                                                ON predecessor_member."OwnerId" = predecessor_batch."OwnerId"
                                                AND predecessor_member."SeriesId" = predecessor_batch."SeriesId"
                                                AND predecessor_member."BatchId" = predecessor_batch."Id"
                                            WHERE predecessor_batch."OwnerId" = batch."OwnerId"
                                                AND predecessor_batch."SeriesId" = batch."SeriesId"
                                                AND predecessor_batch."Status" = 'Activated'
                                                AND predecessor_batch."DraftCastRevisionId" = batch."BaseActiveCastRevisionId"
                                                AND predecessor_member."SeriesBookId" = member."SeriesBookId"
                                                AND predecessor_member."BookId" = member."BookId"
                                                AND predecessor_member."StagedNarrationJobId" = member."PreviousActiveNarrationJobId")))))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'CK_cast_epoch_previous_artifact',
                            MESSAGE = 'An activated member has an invalid predecessor artifact.';
                    END IF;
                END
                $function$;

                CREATE FUNCTION check_cast_epoch_integrity()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    impacted record;
                    changed_job_ids uuid[];
                BEGIN
                    IF TG_TABLE_NAME = 'story_series' THEN
                        IF TG_OP <> 'INSERT' THEN
                            PERFORM assert_cast_epoch_integrity(OLD."OwnerId", OLD."Id");
                        END IF;
                        IF TG_OP <> 'DELETE' THEN
                            PERFORM assert_cast_epoch_integrity(NEW."OwnerId", NEW."Id");
                        END IF;
                    ELSIF TG_TABLE_NAME IN (
                        'series_books',
                        'narration_cast_revisions',
                        'series_cast_rebuild_batches',
                        'series_cast_rebuild_members')
                    THEN
                        IF TG_OP <> 'INSERT' THEN
                            PERFORM assert_cast_epoch_integrity(OLD."OwnerId", OLD."SeriesId");
                        END IF;
                        IF TG_OP <> 'DELETE' THEN
                            PERFORM assert_cast_epoch_integrity(NEW."OwnerId", NEW."SeriesId");
                        END IF;
                    ELSIF TG_TABLE_NAME = 'narration_jobs' THEN
                        IF TG_OP = 'INSERT' THEN
                            changed_job_ids := ARRAY[NEW."Id"];
                        ELSIF TG_OP = 'DELETE' THEN
                            changed_job_ids := ARRAY[OLD."Id"];
                        ELSE
                            changed_job_ids := ARRAY[OLD."Id", NEW."Id"];
                        END IF;

                        IF TG_OP <> 'INSERT' AND OLD."SeriesId" IS NOT NULL THEN
                            PERFORM assert_cast_epoch_integrity(OLD."OwnerId", OLD."SeriesId");
                        END IF;
                        IF TG_OP <> 'DELETE' AND NEW."SeriesId" IS NOT NULL THEN
                            PERFORM assert_cast_epoch_integrity(NEW."OwnerId", NEW."SeriesId");
                        END IF;

                        FOR impacted IN
                            SELECT series_book."OwnerId", series_book."SeriesId"
                            FROM "series_books" AS series_book
                            WHERE series_book."ActiveNarrationJobId" = ANY(changed_job_ids)
                            UNION
                            SELECT member."OwnerId", member."SeriesId"
                            FROM "series_cast_rebuild_members" AS member
                            WHERE member."PreviousActiveNarrationJobId" = ANY(changed_job_ids)
                        LOOP
                            PERFORM assert_cast_epoch_integrity(impacted."OwnerId", impacted."SeriesId");
                        END LOOP;
                    END IF;

                    RETURN NULL;
                END
                $function$;

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_series"
                AFTER INSERT OR UPDATE OR DELETE ON "story_series"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_series_book"
                AFTER INSERT OR UPDATE OR DELETE ON "series_books"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_revision"
                AFTER INSERT OR UPDATE OR DELETE ON "narration_cast_revisions"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_batch"
                AFTER INSERT OR UPDATE OR DELETE ON "series_cast_rebuild_batches"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_member"
                AFTER INSERT OR UPDATE OR DELETE ON "series_cast_rebuild_members"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_job"
                AFTER INSERT OR DELETE ON "narration_jobs"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                CREATE CONSTRAINT TRIGGER "CT_cast_epoch_job_update"
                AFTER UPDATE OF
                    "Id", "OwnerId", "BookId", "SeriesId", "CastRevisionId",
                    "RebuildBatchId", "RebuildMemberId", "Mode", "Status",
                    "Visibility", "AudioRelativePath", "AudioBytes"
                ON "narration_jobs"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION check_cast_epoch_integrity();

                DO $migration$
                DECLARE
                    existing_series record;
                BEGIN
                    FOR existing_series IN
                        SELECT series."OwnerId", series."Id"
                        FROM "story_series" AS series
                    LOOP
                        PERFORM assert_cast_epoch_integrity(
                            existing_series."OwnerId",
                            existing_series."Id");
                    END LOOP;
                END
                $migration$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                            SELECT 1
                            FROM "story_series"
                            WHERE "ActiveCastRevisionId" IS NOT NULL)
                        OR EXISTS (
                            SELECT 1
                            FROM "series_books"
                            WHERE "ActiveNarrationJobId" IS NOT NULL)
                        OR EXISTS (
                            SELECT 1
                            FROM "series_cast_rebuild_batches"
                            WHERE "Status" = 'Activated')
                        OR EXISTS (
                            SELECT 1
                            FROM "narration_cast_revisions"
                            WHERE "Status" IN ('Active', 'Historical'))
                        OR EXISTS (
                            SELECT 1
                            FROM "narration_jobs"
                            WHERE "Mode" = 'MultiCharacter'
                                AND "Visibility" IN ('Published', 'Historical'))
                    THEN
                        RAISE EXCEPTION 'Cannot roll back atomic cast epoch activation while active pointers or activated artifacts exist.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "CT_cast_epoch_series" ON "story_series";
                DROP TRIGGER IF EXISTS "CT_cast_epoch_series_book" ON "series_books";
                DROP TRIGGER IF EXISTS "CT_cast_epoch_revision" ON "narration_cast_revisions";
                DROP TRIGGER IF EXISTS "CT_cast_epoch_batch" ON "series_cast_rebuild_batches";
                DROP TRIGGER IF EXISTS "CT_cast_epoch_member" ON "series_cast_rebuild_members";
                DROP TRIGGER IF EXISTS "CT_cast_epoch_job" ON "narration_jobs";
                DROP TRIGGER IF EXISTS "CT_cast_epoch_job_update" ON "narration_jobs";
                DROP FUNCTION IF EXISTS check_cast_epoch_integrity();
                DROP FUNCTION IF EXISTS assert_cast_epoch_integrity(uuid, uuid);
                DROP FUNCTION IF EXISTS is_safe_narration_audio(text, bigint);

                ALTER TABLE "story_series"
                DROP CONSTRAINT IF EXISTS "FK_story_series_active_cast";
                ALTER TABLE "series_books"
                DROP CONSTRAINT IF EXISTS "FK_series_books_active_job";
                """);

            migrationBuilder.DropIndex(
                name: "IX_rebuild_members_previous_job",
                table: "series_cast_rebuild_members");

            migrationBuilder.DropIndex(
                name: "IX_series_books_active_job",
                table: "series_books");

            migrationBuilder.DropIndex(
                name: "UX_rebuild_batches_draft_cast",
                table: "series_cast_rebuild_batches");

            migrationBuilder.CreateIndex(
                name: "IX_rebuild_batches_draft_cast",
                table: "series_cast_rebuild_batches",
                columns: new[] { "OwnerId", "SeriesId", "DraftCastRevisionId" });

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
    }
}
