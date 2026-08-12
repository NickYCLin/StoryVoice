using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterVoiceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_voice_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SceneCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ConsentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReferenceAudioRelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReferenceAudioSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    VoicePromptText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Transcript = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TranscriptConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoiceProfileTaskId = table.Column<string>(type: "character varying(191)", maxLength: 191, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RightsConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RightsConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_voice_profiles", x => x.Id);
                    table.UniqueConstraint("AK_cvp_scope", x => new { x.OwnerId, x.SeriesId, x.Id });
                    table.CheckConstraint("CK_cvp_consent_type", "\"ConsentType\" IS NULL OR \"ConsentType\" IN ('self_recorded', 'explicit_permission', 'licensed_voice')");
                    table.CheckConstraint("CK_cvp_kind_scene", "(\"Kind\" = 'Base' AND \"SceneCode\" IS NULL) OR (\"Kind\" = 'Scene' AND \"SceneCode\" IN ('neutral', 'nervous', 'happy', 'angry', 'sad'))");
                    table.CheckConstraint("CK_cvp_mode", "(\"Mode\" = 'Clone' AND \"ConsentType\" IS NOT NULL AND \"ReferenceAudioRelativePath\" IS NOT NULL AND \"ReferenceAudioSha256\" IS NOT NULL AND \"VoicePromptText\" IS NULL) OR (\"Mode\" = 'Design' AND \"ConsentType\" IS NULL AND \"ReferenceAudioRelativePath\" IS NULL AND \"ReferenceAudioSha256\" IS NULL AND \"VoicePromptText\" IS NOT NULL)");
                    table.CheckConstraint("CK_cvp_sha256", "\"ReferenceAudioSha256\" IS NULL OR \"ReferenceAudioSha256\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_cvp_status", "\"Status\" IN ('Pending', 'AwaitingTranscriptConfirmation', 'Ready', 'Failed')");
                    table.ForeignKey(
                        name: "FK_cvp_character_scope",
                        columns: x => new { x.OwnerId, x.SeriesId, x.CharacterId },
                        principalTable: "series_characters",
                        principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_character_voice_profiles_OwnerId_SeriesId_CharacterId",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_cvp_character_status",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_cvp_task",
                table: "character_voice_profiles",
                column: "VoiceProfileTaskId");

            migrationBuilder.CreateIndex(
                name: "UX_cvp_base_per_character",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Base'");

            migrationBuilder.CreateIndex(
                name: "UX_cvp_scene_per_character",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterId", "SceneCode" },
                unique: true,
                filter: "\"SceneCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_voice_profiles");
        }
    }
}
