using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContentBookId",
                table: "books",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "book_extractive_summaries",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Generator = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExcerptsJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_extractive_summaries", x => x.BookId);
                    table.ForeignKey(
                        name: "FK_book_extractive_summaries_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_book_extractive_summaries_books_ContentBookId",
                        column: x => x.ContentBookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reading_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_notes_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reading_notes_chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_books_ContentBookId",
                table: "books",
                column: "ContentBookId");

            migrationBuilder.CreateIndex(
                name: "IX_book_extractive_summaries_ContentBookId",
                table: "book_extractive_summaries",
                column: "ContentBookId");

            migrationBuilder.CreateIndex(
                name: "IX_book_extractive_summaries_OwnerId_ContentBookId",
                table: "book_extractive_summaries",
                columns: new[] { "OwnerId", "ContentBookId" });

            migrationBuilder.CreateIndex(
                name: "IX_reading_notes_BookId",
                table: "reading_notes",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_notes_ChapterId",
                table: "reading_notes",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_notes_OwnerId_BookId_UpdatedAt",
                table: "reading_notes",
                columns: new[] { "OwnerId", "BookId", "UpdatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_books_books_ContentBookId",
                table: "books",
                column: "ContentBookId",
                principalTable: "books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_books_books_ContentBookId",
                table: "books");

            migrationBuilder.DropTable(
                name: "book_extractive_summaries");

            migrationBuilder.DropTable(
                name: "reading_notes");

            migrationBuilder.DropIndex(
                name: "IX_books_ContentBookId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "ContentBookId",
                table: "books");
        }
    }
}
