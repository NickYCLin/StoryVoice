using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Characters;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class CharacterVoiceProfileConfiguration : IEntityTypeConfiguration<CharacterVoiceProfile>
{
    public void Configure(EntityTypeBuilder<CharacterVoiceProfile> builder)
    {
        builder.ToTable("character_voice_profiles", table =>
        {
            table.HasCheckConstraint(
                "CK_cvp_kind_scene",
                "(\"Kind\" = 'Base' AND \"SceneCode\" IS NULL) " +
                "OR (\"Kind\" = 'Scene' AND \"SceneCode\" IN ('neutral', 'nervous', 'happy', 'angry', 'sad'))");
            table.HasCheckConstraint(
                "CK_cvp_mode",
                "(\"Mode\" = 'Clone' AND \"ConsentType\" IS NOT NULL AND \"ReferenceAudioRelativePath\" IS NOT NULL " +
                "AND \"ReferenceAudioSha256\" IS NOT NULL AND \"VoicePromptText\" IS NULL) " +
                "OR (\"Mode\" = 'Design' AND \"ConsentType\" IS NULL AND \"ReferenceAudioRelativePath\" IS NULL " +
                "AND \"ReferenceAudioSha256\" IS NULL AND \"VoicePromptText\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_cvp_consent_type",
                "\"ConsentType\" IS NULL OR \"ConsentType\" IN ('self_recorded', 'explicit_permission', 'licensed_voice')");
            table.HasCheckConstraint(
                "CK_cvp_sha256",
                "\"ReferenceAudioSha256\" IS NULL OR \"ReferenceAudioSha256\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "CK_cvp_status",
                "\"Status\" IN ('Pending', 'AwaitingTranscriptConfirmation', 'Ready', 'Failed')");
        });

        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.HasAlternateKey(profile => new { profile.OwnerId, profile.CharacterProfileId, profile.Id })
            .HasName("AK_cvp_scope");

        builder.Property(profile => profile.Kind)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(profile => profile.SceneCode).HasMaxLength(20);
        builder.Property(profile => profile.Mode)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();
        builder.Property(profile => profile.ConsentType).HasMaxLength(32);
        builder.Property(profile => profile.ReferenceAudioRelativePath).HasMaxLength(500);
        builder.Property(profile => profile.ReferenceAudioSha256)
            .HasColumnType("character(64)")
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(profile => profile.VoicePromptText).HasMaxLength(2_000);
        builder.Property(profile => profile.ExpectedTranscript).HasMaxLength(2_000);
        builder.Property(profile => profile.AsrDraftTranscript).HasMaxLength(2_000);
        builder.Property(profile => profile.ConfirmationTranscriptIntent).HasMaxLength(2_000);
        builder.Property(profile => profile.Transcript).HasMaxLength(2_000);
        builder.Property(profile => profile.VoiceProfileTaskId)
            .HasMaxLength(CharacterVoiceProfileOperation.MaximumStoredRemoteTaskIdLength);
        builder.Property(profile => profile.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(profile => profile.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasIndex(profile => new { profile.OwnerId, profile.CharacterProfileId, profile.Kind })
            .HasDatabaseName("UX_cvp_base_per_character")
            .IsUnique()
            .HasFilter("\"Kind\" = 'Base'");
        builder.HasIndex(profile => new { profile.OwnerId, profile.CharacterProfileId, profile.SceneCode })
            .HasDatabaseName("UX_cvp_scene_per_character")
            .IsUnique()
            .HasFilter("\"SceneCode\" IS NOT NULL");
        builder.HasIndex(profile => new { profile.OwnerId, profile.CharacterProfileId, profile.Status })
            .HasDatabaseName("IX_cvp_character_status");
        builder.HasIndex(profile => profile.VoiceProfileTaskId)
            .HasDatabaseName("IX_cvp_task");

        builder.HasOne<CharacterProfile>()
            .WithMany()
            .HasForeignKey(profile => new { profile.OwnerId, profile.CharacterProfileId })
            .HasPrincipalKey(character => new { character.OwnerId, character.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_cvp_character_profile");
    }
}
