using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNarrationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "narration_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Voice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AudioRelativePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AudioBytes = table.Column<long>(type: "bigint", nullable: true),
                    RightsAttestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_narration_jobs", x => x.Id);
                    table.CheckConstraint("CK_narration_jobs_attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_narration_jobs_audio_bytes", "\"AudioBytes\" IS NULL OR \"AudioBytes\" > 0");
                    table.CheckConstraint("CK_narration_jobs_progress", "\"ProgressPercent\" >= 0 AND \"ProgressPercent\" <= 100");
                    table.ForeignKey(
                        name: "FK_narration_jobs_books_BookId",
                        column: x => x.BookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_narration_jobs_books_ContentBookId",
                        column: x => x.ContentBookId,
                        principalTable: "books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_BookId",
                table: "narration_jobs",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_ContentBookId",
                table: "narration_jobs",
                column: "ContentBookId");

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_OwnerId_BookId_ContentBookId_SourceHash_Voic~",
                table: "narration_jobs",
                columns: new[] { "OwnerId", "BookId", "ContentBookId", "SourceHash", "Voice", "Rate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_Status_LeaseExpiresAt",
                table: "narration_jobs",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_narration_jobs_Status_NextAttemptAt_CreatedAt",
                table: "narration_jobs",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "narration_jobs");
        }
    }
}
