using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesNarrativeVoiceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NarrativeVoiceMode",
                table: "story_series",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "IndependentNarrator");

            migrationBuilder.AddCheckConstraint(
                name: "CK_story_series_narrative_voice_mode",
                table: "story_series",
                sql: "\"NarrativeVoiceMode\" IN ('IndependentNarrator', 'PointOfViewInnerMonologue')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_story_series_pov_mode_requires_character",
                table: "story_series",
                sql: "\"NarrativeVoiceMode\" <> 'PointOfViewInnerMonologue' OR \"PointOfViewCharacterId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_story_series_narrative_voice_mode",
                table: "story_series");

            migrationBuilder.DropCheckConstraint(
                name: "CK_story_series_pov_mode_requires_character",
                table: "story_series");

            migrationBuilder.DropColumn(
                name: "NarrativeVoiceMode",
                table: "story_series");
        }
    }
}
