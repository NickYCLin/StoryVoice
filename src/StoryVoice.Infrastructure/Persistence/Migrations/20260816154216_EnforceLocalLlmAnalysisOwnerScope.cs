using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceLocalLlmAnalysisOwnerScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The pre-existing UX_books_owner_id unique index already supplies PostgreSQL's
            // referenced owner boundary. Legacy ownerless books stay unowned and cannot be
            // referenced by non-null owner-scoped analysis rows.
            migrationBuilder.Sql("""
                ALTER TABLE book_local_llm_character_analyses
                ADD CONSTRAINT "FK_book_local_llm_analysis_target_owner_scope"
                FOREIGN KEY ("OwnerId", "BookId")
                REFERENCES books ("OwnerId", "Id")
                ON DELETE NO ACTION;

                ALTER TABLE book_local_llm_character_analyses
                ADD CONSTRAINT "FK_book_local_llm_analysis_content_owner_scope"
                FOREIGN KEY ("OwnerId", "ContentBookId")
                REFERENCES books ("OwnerId", "Id")
                ON DELETE NO ACTION;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE book_local_llm_character_analyses
                DROP CONSTRAINT "FK_book_local_llm_analysis_content_owner_scope";

                ALTER TABLE book_local_llm_character_analyses
                DROP CONSTRAINT "FK_book_local_llm_analysis_target_owner_scope";
                """);
        }
    }
}
