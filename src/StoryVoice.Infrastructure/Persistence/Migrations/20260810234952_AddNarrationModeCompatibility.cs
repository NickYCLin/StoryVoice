using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNarrationModeCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "narration_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "SingleVoice");

            migrationBuilder.Sql(
                """
                UPDATE narration_jobs SET "Mode" = 'SingleVoice'
                WHERE "Mode" IS DISTINCT FROM 'SingleVoice';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_narration_jobs_mode",
                table: "narration_jobs",
                sql: "\"Mode\" IN ('SingleVoice', 'MultiCharacter')");

            migrationBuilder.DropIndex(
                name: "IX_narration_jobs_OwnerId_BookId_ContentBookId_SourceHash_Voic~",
                table: "narration_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_OwnerId_BookId_ContentBookId_SourceHash_Voic~",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate" },
                unique: true,
                filter: "\"Mode\" = 'SingleVoice'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM narration_jobs
                        WHERE "Mode" IS DISTINCT FROM 'SingleVoice'
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back narration Mode while non-SingleVoice jobs exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_narration_jobs_OwnerId_BookId_ContentBookId_SourceHash_Voic~",
                table: "narration_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_narration_jobs_mode",
                table: "narration_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_OwnerId_BookId_ContentBookId_SourceHash_Voic~",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "narration_jobs");
        }
    }
}
