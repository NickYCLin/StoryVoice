using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalLlmCharacterAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "book_local_llm_character_analyses",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generator = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CandidatesJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_local_llm_character_analyses", x => x.BookId);
                    table.ForeignKey(
                        name: "FK_book_local_llm_character_analyses_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_book_local_llm_character_analyses_books_ContentBookId",
                        column: x => x.ContentBookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION storyvoice_validate_local_llm_analysis_owner()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM books
                        WHERE "Id" = NEW."BookId"
                          AND "OwnerId" = NEW."OwnerId") THEN
                        RAISE EXCEPTION 'local LLM analysis owner does not match target book';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM books
                        WHERE "Id" = NEW."ContentBookId"
                          AND "OwnerId" = NEW."OwnerId") THEN
                        RAISE EXCEPTION 'local LLM analysis owner does not match content book';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER tr_book_local_llm_character_analyses_owner
                BEFORE INSERT OR UPDATE OF "OwnerId", "BookId", "ContentBookId"
                ON book_local_llm_character_analyses
                FOR EACH ROW
                EXECUTE FUNCTION storyvoice_validate_local_llm_analysis_owner();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_book_local_llm_character_analyses_ContentBookId",
                table: "book_local_llm_character_analyses",
                column: "ContentBookId");

            migrationBuilder.CreateIndex(
                name: "IX_book_local_llm_character_analyses_OwnerId_ContentBookId",
                table: "book_local_llm_character_analyses",
                columns: new[] { "OwnerId", "ContentBookId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM book_local_llm_character_analyses) THEN
                        RAISE EXCEPTION 'Cannot revert AddLocalLlmCharacterAnalyses while review data exists';
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_book_local_llm_character_analyses_owner
                ON book_local_llm_character_analyses;

                DROP FUNCTION IF EXISTS storyvoice_validate_local_llm_analysis_owner();
                """);

            migrationBuilder.DropTable(
                name: "book_local_llm_character_analyses");
        }
    }
}
