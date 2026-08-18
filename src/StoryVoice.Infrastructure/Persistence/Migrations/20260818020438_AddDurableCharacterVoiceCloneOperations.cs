using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableCharacterVoiceCloneOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AsrDraftTranscript",
                table: "character_voice_profiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedTranscript",
                table: "character_voice_profiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Existing awaiting-confirmation rows stored the ASR draft in Transcript. Preserve it
            // in the new dedicated audit column without changing legacy task handles (including
            // route_* values longer than the current canonical provider contract).
            migrationBuilder.Sql(
                """
                UPDATE character_voice_profiles
                SET "AsrDraftTranscript" = "Transcript"
                WHERE "Status" = 'AwaitingTranscriptConfirmation'
                  AND "TranscriptConfirmedAt" IS NULL
                  AND "Transcript" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "character_voice_profile_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SceneCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SlotKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpectedTranscript = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AsrDraftTranscript = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReferenceAudioRelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReferenceAudioSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ReferenceAudioDurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    RightsConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RightsConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OldProfileConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: true),
                    CredentialKeyId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RemoteTaskId = table.Column<string>(type: "character varying(191)", maxLength: 191, nullable: true),
                    SafeErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RemotePreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_voice_profile_operations", x => x.Id);
                    table.CheckConstraint("CK_cvpo_duration", "\"ReferenceAudioDurationSeconds\" >= 10 AND \"ReferenceAudioDurationSeconds\" <= 45");
                    table.CheckConstraint("CK_cvpo_kind_scene", "(\"Kind\" = 'Base' AND \"SceneCode\" IS NULL AND \"SlotKey\" = 'base') OR (\"Kind\" = 'Scene' AND \"SceneCode\" IS NOT NULL AND \"SlotKey\" = 'scene:' || \"SceneCode\")");
                    table.CheckConstraint("CK_cvpo_remote_state", "(\"State\" = 'Staged' AND \"RemoteTaskId\" IS NULL) OR (\"State\" IN ('RemotePrepared', 'Activated') AND \"RemoteTaskId\" IS NOT NULL) OR \"State\" = 'NeedsAttention'");
                    table.CheckConstraint("CK_cvpo_replace_target", "(\"Type\" = 'Create' AND \"OldProfileId\" IS NULL AND \"OldProfileConcurrencyStamp\" IS NULL) OR (\"Type\" = 'Replace' AND \"OldProfileId\" IS NOT NULL AND \"OldProfileConcurrencyStamp\" IS NOT NULL)");
                    table.CheckConstraint("CK_cvpo_state", "\"State\" IN ('Staged', 'RemotePrepared', 'Activated', 'NeedsAttention')");
                    table.CheckConstraint("CK_cvpo_type", "\"Type\" IN ('Create', 'Replace')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_cvpo_owner_character_state",
                table: "character_voice_profile_operations",
                columns: new[] { "OwnerId", "CharacterProfileId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_cvpo_remote_task",
                table: "character_voice_profile_operations",
                column: "RemoteTaskId");

            migrationBuilder.CreateIndex(
                name: "UX_cvpo_active_slot",
                table: "character_voice_profile_operations",
                columns: new[] { "OwnerId", "CharacterProfileId", "SlotKey" },
                unique: true,
                filter: "\"State\" <> 'Activated'");

            migrationBuilder.CreateIndex(
                name: "UX_cvpo_new_profile",
                table: "character_voice_profile_operations",
                column: "NewProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "AsrDraftTranscript",
                table: "character_voice_profiles");

            migrationBuilder.DropColumn(
                name: "ExpectedTranscript",
                table: "character_voice_profiles");
        }
    }
}
