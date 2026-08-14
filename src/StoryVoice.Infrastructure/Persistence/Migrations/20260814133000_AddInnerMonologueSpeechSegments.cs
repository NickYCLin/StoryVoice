using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInnerMonologueSpeechSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_speech_segment_drafts_kind",
                table: "speech_segment_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_speech_segments_kind",
                table: "confirmed_speech_segments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_speech_segment_drafts_inner_monologue_state",
                table: "speech_segment_drafts",
                sql: "\"Kind\" <> 'InnerMonologue' OR (\"CharacterId\" IS NOT NULL AND \"Confidence\" = 100 AND \"DecisionSource\" = 'Rule' AND \"ReviewStatus\" = 'Confirmed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_speech_segment_drafts_kind",
                table: "speech_segment_drafts",
                sql: "\"Kind\" IN ('Narrator', 'Dialogue', 'InnerMonologue')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_speech_segments_inner_monologue_state",
                table: "confirmed_speech_segments",
                sql: "\"Kind\" <> 'InnerMonologue' OR (\"CharacterId\" IS NOT NULL AND \"Confidence\" = 100 AND \"DecisionSource\" = 'Rule')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_speech_segments_kind",
                table: "confirmed_speech_segments",
                sql: "\"Kind\" IN ('Narrator', 'Dialogue', 'InnerMonologue')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM speech_segment_drafts WHERE "Kind" = 'InnerMonologue'
                    ) OR EXISTS (
                        SELECT 1 FROM confirmed_speech_segments WHERE "Kind" = 'InnerMonologue'
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back inner monologue speech segments while dependent rows exist.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_speech_segment_drafts_inner_monologue_state",
                table: "speech_segment_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_speech_segment_drafts_kind",
                table: "speech_segment_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_speech_segments_inner_monologue_state",
                table: "confirmed_speech_segments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_confirmed_speech_segments_kind",
                table: "confirmed_speech_segments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_speech_segment_drafts_kind",
                table: "speech_segment_drafts",
                sql: "\"Kind\" IN ('Narrator', 'Dialogue')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_confirmed_speech_segments_kind",
                table: "confirmed_speech_segments",
                sql: "\"Kind\" IN ('Narrator', 'Dialogue')");
        }
    }
}
