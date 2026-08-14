using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(StoryVoiceDbContext))]
    [Migration("20260814094000_AllowPendingSeriesBookOnboarding")]
    public partial class AllowPendingSeriesBookOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(PendingSeriesBookOnboardingSql.Up);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(PendingSeriesBookOnboardingSql.Down);
    }

    internal static class PendingSeriesBookOnboardingSql
    {
        internal const string Up =
            """
            DO $migration$
            DECLARE
                current_definition text;
                cohort_pattern text := $pattern$(FROM "series_books" AS series_book\s+WHERE series_book\."OwnerId" = batch\."OwnerId"\s+AND series_book\."SeriesId" = batch\."SeriesId")(\s+AND NOT EXISTS \(\s+SELECT 1\s+FROM "series_cast_rebuild_members" AS member\s+WHERE member\."OwnerId" = series_book\."OwnerId"\s+AND member\."SeriesId" = series_book\."SeriesId"\s+AND member\."BatchId" = batch\."Id"\s+AND member\."SeriesBookId" = series_book\."Id"\s+AND member\."BookId" = series_book\."BookId"\s+AND member\."MembershipRevision" = series_book\."MembershipRevision"\)\))$pattern$;
                replacement_fragment text := E'\\1\n                    AND series_book."MembershipRevision" <= batch."CohortMembershipRevision"\\2';
            BEGIN
                SELECT pg_get_functiondef(procedure.oid)
                INTO current_definition
                FROM pg_proc AS procedure
                INNER JOIN pg_namespace AS schema_name ON schema_name.oid = procedure.pronamespace
                WHERE procedure.proname = 'assert_cast_epoch_integrity'
                    AND schema_name.nspname = 'public';

                IF current_definition IS NULL
                    OR regexp_count(current_definition, cohort_pattern, 1, 'n') <> 1
                THEN
                    RAISE EXCEPTION
                        'Unexpected assert_cast_epoch_integrity definition while enabling pending series-book onboarding.';
                END IF;

                EXECUTE regexp_replace(
                    rtrim(current_definition, ';'),
                    cohort_pattern,
                    replacement_fragment,
                    'n');
            END
            $migration$;
            """;

        internal const string Down =
            """
            DO $migration$
            DECLARE
                current_definition text;
                cohort_pattern text := $pattern$(FROM "series_books" AS series_book\s+WHERE series_book\."OwnerId" = batch\."OwnerId"\s+AND series_book\."SeriesId" = batch\."SeriesId")(\s+AND series_book\."MembershipRevision" <= batch\."CohortMembershipRevision")(\s+AND NOT EXISTS \(\s+SELECT 1\s+FROM "series_cast_rebuild_members" AS member\s+WHERE member\."OwnerId" = series_book\."OwnerId"\s+AND member\."SeriesId" = series_book\."SeriesId"\s+AND member\."BatchId" = batch\."Id"\s+AND member\."SeriesBookId" = series_book\."Id"\s+AND member\."BookId" = series_book\."BookId"\s+AND member\."MembershipRevision" = series_book\."MembershipRevision"\)\))$pattern$;
                replacement_fragment text := E'\\1\\3';
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "story_series" AS series
                    INNER JOIN "series_cast_rebuild_batches" AS batch
                        ON batch."OwnerId" = series."OwnerId"
                        AND batch."SeriesId" = series."Id"
                        AND batch."DraftCastRevisionId" = series."ActiveCastRevisionId"
                        AND batch."Status" = 'Activated'
                    INNER JOIN "series_books" AS series_book
                        ON series_book."OwnerId" = batch."OwnerId"
                        AND series_book."SeriesId" = batch."SeriesId"
                        AND series_book."MembershipRevision" > batch."CohortMembershipRevision")
                THEN
                    RAISE EXCEPTION
                        'Cannot roll back pending series-book onboarding while an active epoch has pending memberships.';
                END IF;

                SELECT pg_get_functiondef(procedure.oid)
                INTO current_definition
                FROM pg_proc AS procedure
                INNER JOIN pg_namespace AS schema_name ON schema_name.oid = procedure.pronamespace
                WHERE procedure.proname = 'assert_cast_epoch_integrity'
                    AND schema_name.nspname = 'public';

                IF current_definition IS NULL
                    OR regexp_count(current_definition, cohort_pattern, 1, 'n') <> 1
                THEN
                    RAISE EXCEPTION
                        'Unexpected assert_cast_epoch_integrity definition while reverting pending series-book onboarding.';
                END IF;

                EXECUTE regexp_replace(
                    rtrim(current_definition, ';'),
                    cohort_pattern,
                    replacement_fragment,
                    'n');
            END
            $migration$;
            """;
    }
}
