using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterVoiceCloneConfirmationIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_cvpo_active_slot",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_remote_state",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_state",
                table: "character_voice_profile_operations");

            // The preceding migration intentionally had no consent-evidence columns. If it was
            // ever deployed on its own and accepted operations, there is no safe value with which
            // to backfill legal evidence. Stop for operator reconciliation instead of manufacturing
            // empty/default attestations.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM character_voice_profile_operations) THEN
                        RAISE EXCEPTION 'cannot add clone consent evidence to existing operations without reconciliation';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmationRequestedAt",
                table: "character_voice_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationTranscriptIntent",
                table: "character_voice_profiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttestationVersion",
                table: "character_voice_profile_operations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false);

            migrationBuilder.AddColumn<bool>(
                name: "CommercialUseAllowed",
                table: "character_voice_profile_operations",
                type: "boolean",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ConsentReceiptSha256",
                table: "character_voice_profile_operations",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ConsentRecordSha256",
                table: "character_voice_profile_operations",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ConsentSignedDate",
                table: "character_voice_profile_operations",
                type: "date",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceVersion",
                table: "character_voice_profile_operations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedTranscriptSha256",
                table: "character_voice_profile_operations",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false);

            migrationBuilder.AddColumn<bool>(
                name: "FormalNarrationAllowed",
                table: "character_voice_profile_operations",
                type: "boolean",
                nullable: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrivateEvaluationAllowed",
                table: "character_voice_profile_operations",
                type: "boolean",
                nullable: false);

            migrationBuilder.AddColumn<bool>(
                name: "PublicDistributionAllowed",
                table: "character_voice_profile_operations",
                type: "boolean",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "RecorderName",
                table: "character_voice_profile_operations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecordingDate",
                table: "character_voice_profile_operations",
                type: "date",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "UX_cvpo_active_slot",
                table: "character_voice_profile_operations",
                columns: new[] { "OwnerId", "CharacterProfileId", "SlotKey" },
                unique: true,
                filter: "\"State\" IN ('Staged', 'RemotePrepared', 'NeedsAttention')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_evidence_contract",
                table: "character_voice_profile_operations",
                sql: "\"ConsentType\" IN ('self_recorded', 'explicit_permission', 'licensed_voice') AND \"EvidenceVersion\" = 'storyvoice-clone-consent-receipt/v2' AND \"AttestationVersion\" = 'storyvoice-clone-subject-consent/v1'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_evidence_dates",
                table: "character_voice_profile_operations",
                sql: "\"RecordingDate\" > DATE '-infinity' AND \"ConsentSignedDate\" > DATE '-infinity' AND \"ConsentSignedDate\" >= \"RecordingDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_evidence_hashes",
                table: "character_voice_profile_operations",
                sql: "\"ReferenceAudioSha256\" ~ '^[0-9a-f]{64}$' AND \"ConsentRecordSha256\" ~ '^[0-9a-f]{64}$' AND \"ConsentReceiptSha256\" ~ '^[0-9a-f]{64}$' AND \"ExpectedTranscriptSha256\" ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_evidence_identity",
                table: "character_voice_profile_operations",
                sql: "\"RightsConfirmedByUserId\" = \"OwnerId\" AND length(btrim(\"RecorderName\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_private_evaluation",
                table: "character_voice_profile_operations",
                sql: "\"PrivateEvaluationAllowed\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_remote_state",
                table: "character_voice_profile_operations",
                sql: "(\"State\" = 'Staged' AND \"RemoteTaskId\" IS NULL AND \"RemotePreparedAt\" IS NULL AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NULL) OR (\"State\" = 'RemotePrepared' AND \"RemoteTaskId\" IS NOT NULL AND \"RemotePreparedAt\" IS NOT NULL AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NULL) OR (\"State\" = 'Activated' AND \"RemoteTaskId\" IS NOT NULL AND \"RemotePreparedAt\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"SafeErrorCode\" IS NULL) OR (\"State\" = 'NeedsAttention' AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NOT NULL AND ((\"RemoteTaskId\" IS NULL AND \"RemotePreparedAt\" IS NULL) OR (\"RemoteTaskId\" IS NOT NULL AND \"RemotePreparedAt\" IS NOT NULL))) OR (\"State\" = 'Rejected' AND \"RemoteTaskId\" IS NULL AND \"RemotePreparedAt\" IS NULL AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_state",
                table: "character_voice_profile_operations",
                sql: "\"State\" IN ('Staged', 'RemotePrepared', 'Activated', 'NeedsAttention', 'Rejected')");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_cvpo_active_slot",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_remote_state",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_state",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_evidence_contract",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_evidence_dates",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_evidence_hashes",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_evidence_identity",
                table: "character_voice_profile_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_cvpo_private_evaluation",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "ConfirmationRequestedAt",
                table: "character_voice_profiles");

            migrationBuilder.DropColumn(
                name: "ConfirmationTranscriptIntent",
                table: "character_voice_profiles");

            migrationBuilder.DropColumn(
                name: "AttestationVersion",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "CommercialUseAllowed",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "ConsentReceiptSha256",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "ConsentRecordSha256",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "ConsentSignedDate",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "EvidenceVersion",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "ExpectedTranscriptSha256",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "FormalNarrationAllowed",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "PrivateEvaluationAllowed",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "PublicDistributionAllowed",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "RecorderName",
                table: "character_voice_profile_operations");

            migrationBuilder.DropColumn(
                name: "RecordingDate",
                table: "character_voice_profile_operations");

            migrationBuilder.CreateIndex(
                name: "UX_cvpo_active_slot",
                table: "character_voice_profile_operations",
                columns: new[] { "OwnerId", "CharacterProfileId", "SlotKey" },
                unique: true,
                filter: "\"State\" <> 'Activated'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_remote_state",
                table: "character_voice_profile_operations",
                sql: "(\"State\" = 'Staged' AND \"RemoteTaskId\" IS NULL) OR (\"State\" IN ('RemotePrepared', 'Activated') AND \"RemoteTaskId\" IS NOT NULL) OR \"State\" = 'NeedsAttention'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_cvpo_state",
                table: "character_voice_profile_operations",
                sql: "\"State\" IN ('Staged', 'RemotePrepared', 'Activated', 'NeedsAttention')");
        }
    }
}
