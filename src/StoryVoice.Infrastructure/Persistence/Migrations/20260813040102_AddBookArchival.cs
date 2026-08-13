using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookArchival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "books",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_books_owner_archive_created",
                table: "books",
                columns: new[] { "OwnerId", "IsArchived", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM books WHERE "IsArchived") THEN
                        RAISE EXCEPTION 'Cannot roll back book archival while archived books exist.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_books_owner_archive_created",
                table: "books");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "books");
        }
    }
}
