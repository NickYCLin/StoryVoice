using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesPointOfViewCharacter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PointOfViewCharacterId",
                table: "story_series",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_series_PointOfViewCharacterId",
                table: "story_series",
                column: "PointOfViewCharacterId");

            migrationBuilder.AddForeignKey(
                name: "FK_story_series_point_of_view_character",
                table: "story_series",
                column: "PointOfViewCharacterId",
                principalTable: "series_characters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_story_series_point_of_view_character",
                table: "story_series");

            migrationBuilder.DropIndex(
                name: "IX_story_series_PointOfViewCharacterId",
                table: "story_series");

            migrationBuilder.DropColumn(
                name: "PointOfViewCharacterId",
                table: "story_series");
        }
    }
}
