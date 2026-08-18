using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class CharacterVoiceProfileOperationConfiguration
    : IEntityTypeConfiguration<CharacterVoiceProfileOperation>
{
    public void Configure(EntityTypeBuilder<CharacterVoiceProfileOperation> builder)
    {
        builder.ToTable("character_voice_profile_operations", table =>
        {
            table.HasCheckConstraint(
                "CK_cvpo_type",
                "\"Type\" IN ('Create', 'Replace')");
            table.HasCheckConstraint(
                "CK_cvpo_state",
                "\"State\" IN ('Staged', 'RemotePrepared', 'Activated', 'NeedsAttention', 'Rejected')");
            table.HasCheckConstraint(
                "CK_cvpo_kind_scene",
                "(\"Kind\" = 'Base' AND \"SceneCode\" IS NULL AND \"SlotKey\" = 'base') " +
                "OR (\"Kind\" = 'Scene' AND \"SceneCode\" IS NOT NULL AND \"SlotKey\" = 'scene:' || \"SceneCode\")");
            table.HasCheckConstraint(
                "CK_cvpo_replace_target",
                "(\"Type\" = 'Create' AND \"OldProfileId\" IS NULL AND \"OldProfileConcurrencyStamp\" IS NULL) " +
                "OR (\"Type\" = 'Replace' AND \"OldProfileId\" IS NOT NULL AND \"OldProfileConcurrencyStamp\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_cvpo_duration",
                "\"ReferenceAudioDurationSeconds\" >= 10 AND \"ReferenceAudioDurationSeconds\" <= 45");
            table.HasCheckConstraint(
                "CK_cvpo_private_evaluation",
                "\"PrivateEvaluationAllowed\" = TRUE");
            table.HasCheckConstraint(
                "CK_cvpo_evidence_hashes",
                "\"ReferenceAudioSha256\" ~ '^[0-9a-f]{64}$' " +
                "AND \"ConsentRecordSha256\" ~ '^[0-9a-f]{64}$' " +
                "AND \"ConsentReceiptSha256\" ~ '^[0-9a-f]{64}$' " +
                "AND \"ExpectedTranscriptSha256\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "CK_cvpo_evidence_dates",
                "\"RecordingDate\" > DATE '-infinity' " +
                "AND \"ConsentSignedDate\" > DATE '-infinity' " +
                "AND \"ConsentSignedDate\" >= \"RecordingDate\"");
            table.HasCheckConstraint(
                "CK_cvpo_evidence_identity",
                "\"RightsConfirmedByUserId\" = \"OwnerId\" " +
                "AND length(btrim(\"RecorderName\")) > 0");
            table.HasCheckConstraint(
                "CK_cvpo_evidence_contract",
                "\"ConsentType\" IN ('self_recorded', 'explicit_permission', 'licensed_voice') " +
                $"AND \"EvidenceVersion\" = '{CharacterVoiceConsentEvidence.CurrentEvidenceVersion}' " +
                $"AND \"AttestationVersion\" = '{CharacterVoiceConsentEvidence.CurrentAttestationVersion}'");
            table.HasCheckConstraint(
                "CK_cvpo_remote_state",
                "(\"State\" = 'Staged' AND \"RemoteTaskId\" IS NULL AND \"RemotePreparedAt\" IS NULL " +
                "AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NULL) " +
                "OR (\"State\" = 'RemotePrepared' AND \"RemoteTaskId\" IS NOT NULL " +
                "AND \"RemotePreparedAt\" IS NOT NULL AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NULL) " +
                "OR (\"State\" = 'Activated' AND \"RemoteTaskId\" IS NOT NULL " +
                "AND \"RemotePreparedAt\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"SafeErrorCode\" IS NULL) " +
                "OR (\"State\" = 'NeedsAttention' AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NOT NULL " +
                "AND ((\"RemoteTaskId\" IS NULL AND \"RemotePreparedAt\" IS NULL) " +
                "OR (\"RemoteTaskId\" IS NOT NULL AND \"RemotePreparedAt\" IS NOT NULL))) " +
                "OR (\"State\" = 'Rejected' AND \"RemoteTaskId\" IS NULL AND \"RemotePreparedAt\" IS NULL " +
                "AND \"ActivatedAt\" IS NULL AND \"SafeErrorCode\" IS NOT NULL)");
        });

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.NewProfileId).ValueGeneratedNever();
        builder.Property(operation => operation.Type)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(operation => operation.State)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(operation => operation.Kind)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(operation => operation.SceneCode).HasMaxLength(20);
        builder.Property(operation => operation.SlotKey).HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.ConsentType).HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.RecorderName)
            .HasMaxLength(CharacterVoiceConsentEvidence.MaximumRecorderNameLength)
            .IsRequired();
        builder.Property(operation => operation.ConsentRecordSha256)
            .HasColumnType("character(64)")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.ConsentReceiptSha256)
            .HasColumnType("character(64)")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.ExpectedTranscriptSha256)
            .HasColumnType("character(64)")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.EvidenceVersion)
            .HasMaxLength(CharacterVoiceConsentEvidence.MaximumVersionLength)
            .IsRequired();
        builder.Property(operation => operation.AttestationVersion)
            .HasMaxLength(CharacterVoiceConsentEvidence.MaximumVersionLength)
            .IsRequired();
        builder.Property(operation => operation.ExpectedTranscript).HasMaxLength(2_000).IsRequired();
        builder.Property(operation => operation.AsrDraftTranscript).HasMaxLength(2_000);
        builder.Property(operation => operation.ReferenceAudioRelativePath).HasMaxLength(500).IsRequired();
        builder.Property(operation => operation.ReferenceAudioSha256)
            .HasColumnType("character(64)")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.CredentialKeyId)
            .HasMaxLength(CharacterVoiceProfileOperation.MaximumCredentialKeyIdLength)
            .IsRequired();
        builder.Property(operation => operation.RemoteTaskId)
            .HasMaxLength(CharacterVoiceProfileOperation.MaximumStoredRemoteTaskIdLength);
        builder.Property(operation => operation.SafeErrorCode)
            .HasMaxLength(CharacterVoiceProfileOperation.MaximumSafeErrorCodeLength);
        builder.Property(operation => operation.ConcurrencyStamp).IsConcurrencyToken();
        builder.Ignore(operation => operation.UsageScopes);

        builder.HasIndex(operation => operation.NewProfileId)
            .HasDatabaseName("UX_cvpo_new_profile")
            .IsUnique();
        builder.HasIndex(operation => new
        {
            operation.OwnerId,
            operation.CharacterProfileId,
            operation.SlotKey,
        })
            .HasDatabaseName("UX_cvpo_active_slot")
            .IsUnique()
            .HasFilter("\"State\" IN ('Staged', 'RemotePrepared', 'NeedsAttention')");
        builder.HasIndex(operation => new { operation.OwnerId, operation.CharacterProfileId, operation.State })
            .HasDatabaseName("IX_cvpo_owner_character_state");
        builder.HasIndex(operation => operation.RemoteTaskId)
            .HasDatabaseName("IX_cvpo_remote_task");

        // Deliberately no CharacterProfile/CharacterVoiceProfile relationship. Operation evidence
        // must survive accidental aggregate deletion and may refer to a replacement profile that
        // has not been inserted yet.
    }
}
