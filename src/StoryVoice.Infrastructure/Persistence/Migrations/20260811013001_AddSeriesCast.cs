using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesCast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "story_series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NarratorProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorVoice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NarratorRate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorPitch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NarratorVolume = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DefaultSpeakerPauseMs = table.Column<int>(type: "integer", nullable: false),
                    ActiveCastRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_series", x => x.Id);
                    table.UniqueConstraint("AK_story_series_OwnerId_Id", x => new { x.OwnerId, x.Id });
                    table.CheckConstraint("CK_story_series_default_speaker_pause_ms", "\"DefaultSpeakerPauseMs\" >= 0 AND \"DefaultSpeakerPauseMs\" <= 60000");
                    table.ForeignKey(
                        name: "FK_story_series_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolumeLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    MembershipRevision = table.Column<int>(type: "integer", nullable: false),
                    ActiveNarrationJobId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_books", x => x.Id);
                    table.CheckConstraint("CK_series_books_membership_revision", "\"MembershipRevision\" >= 1");
                    table.CheckConstraint("CK_series_books_sort_order", "\"SortOrder\" >= 0 AND \"SortOrder\" <= 1000000");
                    table.ForeignKey(
                        name: "FK_series_books_story_series_OwnerId_SeriesId",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalIdentityKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VoiceProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Voice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Pitch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Volume = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalIdentityKeyKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, computedColumnSql: "'Canonical'", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_characters", x => x.Id);
                    table.UniqueConstraint("AK_series_characters_OwnerId_SeriesId_Id", x => new { x.OwnerId, x.SeriesId, x.Id });
                    table.CheckConstraint("CK_series_characters_role", "\"Role\" IN ('Main', 'Supporting', 'Minor')");
                    table.ForeignKey(
                        name: "FK_series_characters_story_series_OwnerId_SeriesId",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_character_identity_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_character_identity_keys", x => x.Id);
                    table.CheckConstraint("CK_series_character_identity_keys_kind", "\"Kind\" IN ('Canonical', 'Alias')");
                    table.ForeignKey(
                        name: "FK_char_keys_character_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.CharacterId },
                        principalTable: "series_characters",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_char_keys_series_scope",
                        columns: x => new { x.OwnerId, x.SeriesId },
                        principalTable: "story_series",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_books_owner_id",
                table: "books",
                columns: new[] { "OwnerId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_series_books_OwnerId_SeriesId_SortOrder",
                table: "series_books",
                columns: new[] { "OwnerId", "SeriesId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_series_books_owner_book",
                table: "series_books",
                columns: new[] { "OwnerId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_char_keys_canonical_target",
                table: "series_character_identity_keys",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId", "Id", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_char_keys_series_value",
                table: "series_character_identity_keys",
                columns: new[] { "OwnerId", "SeriesId", "NormalizedValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_series_OwnerId_NormalizedName",
                table: "story_series",
                columns: new[] { "OwnerId", "NormalizedName" },
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_books"
                ADD CONSTRAINT "FK_series_books_books_OwnerId_BookId"
                FOREIGN KEY ("OwnerId", "BookId")
                REFERENCES "books" ("OwnerId", "Id")
                ON DELETE CASCADE;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_characters"
                ADD CONSTRAINT "FK_series_characters_canonical_identity_key"
                FOREIGN KEY (
                    "OwnerId",
                    "SeriesId",
                    "Id",
                    "CanonicalIdentityKeyId",
                    "CanonicalIdentityKeyKind")
                REFERENCES "series_character_identity_keys" (
                    "OwnerId",
                    "SeriesId",
                    "CharacterId",
                    "Id",
                    "Kind")
                ON DELETE RESTRICT
                DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "story_series") THEN
                        RAISE EXCEPTION 'Cannot roll back AddSeriesCast while Phase-B series rows exist.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_characters"
                DROP CONSTRAINT IF EXISTS "FK_series_characters_canonical_identity_key";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "series_books"
                DROP CONSTRAINT IF EXISTS "FK_series_books_books_OwnerId_BookId";
                """);

            migrationBuilder.DropTable(
                name: "series_books");

            migrationBuilder.DropTable(
                name: "series_character_identity_keys");

            migrationBuilder.DropTable(
                name: "series_characters");

            migrationBuilder.DropTable(
                name: "story_series");

            migrationBuilder.DropIndex(
                name: "UX_books_owner_id",
                table: "books");
        }
    }
}
