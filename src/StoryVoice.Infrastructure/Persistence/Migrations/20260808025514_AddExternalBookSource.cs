using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalBookSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "books",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSourceId",
                table: "books",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProvider",
                table: "books",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceSyncedAt",
                table: "books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "books",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_books_SourceProvider_ExternalSourceId",
                table: "books",
                columns: new[] { "SourceProvider", "ExternalSourceId" },
                unique: true,
                filter: "\"SourceProvider\" IS NOT NULL AND \"ExternalSourceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_books_SourceProvider_ExternalSourceId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "books");

            migrationBuilder.DropColumn(
                name: "ExternalSourceId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "SourceProvider",
                table: "books");

            migrationBuilder.DropColumn(
                name: "SourceSyncedAt",
                table: "books");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "books");
        }
    }
}
