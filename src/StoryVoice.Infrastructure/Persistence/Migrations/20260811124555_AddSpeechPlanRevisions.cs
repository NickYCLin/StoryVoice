using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeechPlanRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_chapters_BookId_Id",
                table: "chapters",
                columns: new[] { "BookId", "Id" });

            migrationBuilder.CreateTable(
                name: "chapter_speech_plan_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PlanVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_speech_plan_drafts", x => x.Id);
                    table.UniqueConstraint("AK_chapter_speech_plan_drafts_OwnerId_SeriesId_Id", x => new { x.OwnerId, x.SeriesId, x.Id });
                    table.CheckConstraint("CK_speech_plan_drafts_status", "\"Status\" IN ('Draft', 'NeedsReview', 'ReadyToConfirm', 'Stale')");
                    table.CheckConstraint("CK_speech_plan_drafts_version", "\"PlanVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_speech_plan_drafts_series_scope",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "confirmed_speech_plan_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_speech_plan_revisions", x => x.Id);
                    table.UniqueConstraint("AK_confirmed_speech_plan_revisions_OwnerId_SeriesId_Id", x => new { x.OwnerId, x.SeriesId, x.Id });
                    table.CheckConstraint("CK_confirmed_speech_plans_fingerprint", "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_confirmed_speech_plans_revision", "\"RevisionNumber\" >= 1");
                    table.ForeignKey(
                        name: "FK_confirmed_speech_plans_series_scope",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "speech_segment_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanDraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    Length = table.Column<int>(type: "integer", nullable: false),
                    TextHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    DecisionSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_speech_segment_drafts", x => x.Id);
                    table.CheckConstraint("CK_speech_segment_drafts_confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 100");
                    table.CheckConstraint("CK_speech_segment_drafts_decision_source", "\"DecisionSource\" IN ('Rule', 'LocalModel', 'User')");
                    table.CheckConstraint("CK_speech_segment_drafts_kind", "\"Kind\" IN ('Narrator', 'Dialogue')");
                    table.CheckConstraint("CK_speech_segment_drafts_length", "\"Length\" > 0");
                    table.CheckConstraint("CK_speech_segment_drafts_narrator_no_character", "\"Kind\" <> 'Narrator' OR \"CharacterId\" IS NULL");
                    table.CheckConstraint("CK_speech_segment_drafts_review_status", "\"ReviewStatus\" IN ('Suggested', 'Confirmed', 'Rejected')");
                    table.CheckConstraint("CK_speech_segment_drafts_sort_order", "\"SortOrder\" >= 0");
                    table.CheckConstraint("CK_speech_segment_drafts_source_kind", "\"SourceKind\" IN ('ChapterTitle', 'Body')");
                    table.CheckConstraint("CK_speech_segment_drafts_start_offset", "\"StartOffset\" >= 0");
                    table.ForeignKey(
                        name: "FK_speech_segment_drafts_chapter_speech_plan_drafts_OwnerId_Se~",
                        columns: x => new { x.OwnerId, x.SeriesId, x.PlanDraftId },
                        principalTable: "chapter_speech_plan_drafts",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_speech_segment_drafts_character_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.CharacterId },
                        principalTable: "series_characters",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "confirmed_speech_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    Length = table.Column<int>(type: "integer", nullable: false),
                    TextHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    DecisionSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_speech_segments", x => x.Id);
                    table.CheckConstraint("CK_confirmed_speech_segments_confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 100");
                    table.CheckConstraint("CK_confirmed_speech_segments_decision_source", "\"DecisionSource\" IN ('Rule', 'LocalModel', 'User')");
                    table.CheckConstraint("CK_confirmed_speech_segments_kind", "\"Kind\" IN ('Narrator', 'Dialogue')");
                    table.CheckConstraint("CK_confirmed_speech_segments_length", "\"Length\" > 0");
                    table.CheckConstraint("CK_confirmed_speech_segments_narrator_no_character", "\"Kind\" <> 'Narrator' OR \"CharacterId\" IS NULL");
                    table.CheckConstraint("CK_confirmed_speech_segments_sort_order", "\"SortOrder\" >= 0");
                    table.CheckConstraint("CK_confirmed_speech_segments_source_kind", "\"SourceKind\" IN ('ChapterTitle', 'Body')");
                    table.CheckConstraint("CK_confirmed_speech_segments_start_offset", "\"StartOffset\" >= 0");
                    table.ForeignKey(
                        name: "FK_confirmed_speech_segments_character_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.CharacterId },
                        principalTable: "series_characters",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_speech_segments_confirmed_speech_plan_revisions_O~",
                        columns: x => new { x.OwnerId, x.SeriesId, x.PlanRevisionId },
                        principalTable: "confirmed_speech_plan_revisions",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "narration_job_speech_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    NarrationJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChapterSortOrder = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedSpeechPlanRevisionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_narration_job_speech_plans", x => x.Id);
                    table.CheckConstraint("CK_njob_speech_plans_chapter_sort_order", "\"ChapterSortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_njob_speech_plans_job_scope",
                        column: x => x.NarrationJobId,
                        principalTable: "narration_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_njob_speech_plans_revision_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.ConfirmedSpeechPlanRevisionId },
                        principalTable: "confirmed_speech_plan_revisions",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_speech_plan_drafts_chapter",
                table: "chapter_speech_plan_drafts",
                columns: new[] { "OwnerId", "SeriesId", "BookId", "ChapterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_confirmed_speech_plans_revision",
                table: "confirmed_speech_plan_revisions",
                columns: new[] { "OwnerId", "SeriesId", "BookId", "ChapterId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_speech_segments_OwnerId_SeriesId_CharacterId",
                table: "confirmed_speech_segments",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "UX_confirmed_speech_segments_order",
                table: "confirmed_speech_segments",
                columns: new[] { "OwnerId", "SeriesId", "PlanRevisionId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_narration_job_speech_plans_NarrationJobId",
                table: "narration_job_speech_plans",
                column: "NarrationJobId");

            migrationBuilder.CreateIndex(
                name: "IX_narration_job_speech_plans_OwnerId_SeriesId_ConfirmedSpeech~",
                table: "narration_job_speech_plans",
                columns: new[] { "OwnerId", "SeriesId", "ConfirmedSpeechPlanRevisionId" });

            migrationBuilder.CreateIndex(
                name: "UX_njob_speech_plans_chapter",
                table: "narration_job_speech_plans",
                columns: new[] { "OwnerId", "SeriesId", "NarrationJobId", "ChapterSortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_njob_speech_plans_revision",
                table: "narration_job_speech_plans",
                columns: new[] { "OwnerId", "SeriesId", "NarrationJobId", "ConfirmedSpeechPlanRevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_speech_segment_drafts_OwnerId_SeriesId_CharacterId",
                table: "speech_segment_drafts",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "UX_speech_segment_drafts_order",
                table: "speech_segment_drafts",
                columns: new[] { "OwnerId", "SeriesId", "PlanDraftId", "SortOrder" },
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE "chapter_speech_plan_drafts"
                ADD CONSTRAINT "FK_speech_plan_drafts_books_OwnerId_BookId"
                FOREIGN KEY ("OwnerId", "BookId")
                REFERENCES "books" ("OwnerId", "Id")
                ON DELETE CASCADE;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "chapter_speech_plan_drafts"
                ADD CONSTRAINT "FK_speech_plan_drafts_chapters_BookId_ChapterId"
                FOREIGN KEY ("BookId", "ChapterId")
                REFERENCES "chapters" ("BookId", "Id")
                ON DELETE CASCADE;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "confirmed_speech_plan_revisions"
                ADD CONSTRAINT "FK_confirmed_speech_plans_books_OwnerId_BookId"
                FOREIGN KEY ("OwnerId", "BookId")
                REFERENCES "books" ("OwnerId", "Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "confirmed_speech_plan_revisions"
                ADD CONSTRAINT "FK_confirmed_speech_plans_chapters_BookId_ChapterId"
                FOREIGN KEY ("BookId", "ChapterId")
                REFERENCES "chapters" ("BookId", "Id")
                ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "confirmed_speech_plan_revisions"
                DROP CONSTRAINT IF EXISTS "FK_confirmed_speech_plans_chapters_BookId_ChapterId";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "confirmed_speech_plan_revisions"
                DROP CONSTRAINT IF EXISTS "FK_confirmed_speech_plans_books_OwnerId_BookId";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "chapter_speech_plan_drafts"
                DROP CONSTRAINT IF EXISTS "FK_speech_plan_drafts_chapters_BookId_ChapterId";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "chapter_speech_plan_drafts"
                DROP CONSTRAINT IF EXISTS "FK_speech_plan_drafts_books_OwnerId_BookId";
                """);

            migrationBuilder.DropTable(
                name: "confirmed_speech_segments");

            migrationBuilder.DropTable(
                name: "narration_job_speech_plans");

            migrationBuilder.DropTable(
                name: "speech_segment_drafts");

            migrationBuilder.DropTable(
                name: "confirmed_speech_plan_revisions");

            migrationBuilder.DropTable(
                name: "chapter_speech_plan_drafts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_chapters_BookId_Id",
                table: "chapters");
        }
    }
}
