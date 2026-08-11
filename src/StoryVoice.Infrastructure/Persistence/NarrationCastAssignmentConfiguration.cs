using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class NarrationCastAssignmentConfiguration : IEntityTypeConfiguration<NarrationCastAssignment>
{
    public void Configure(EntityTypeBuilder<NarrationCastAssignment> builder)
    {
        builder.ToTable("narration_cast_assignments");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Id).ValueGeneratedNever();
        builder.Property(assignment => assignment.CanonicalNameSnapshot)
            .HasMaxLength(SeriesFieldLimits.CharacterName)
            .IsRequired();
        builder.Property(assignment => assignment.VoiceProvider)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(assignment => assignment.ProviderVersion)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(assignment => assignment.Voice)
            .HasMaxLength(SeriesFieldLimits.Voice)
            .IsRequired();
        builder.Property(assignment => assignment.Rate)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(assignment => assignment.Pitch)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(assignment => assignment.Volume)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();

        builder.HasIndex(assignment => new
        {
            assignment.OwnerId,
            assignment.SeriesId,
            assignment.CastRevisionId,
            assignment.CharacterId
        })
            .HasDatabaseName("UX_ncast_asgn_character")
            .IsUnique();

        builder.HasOne<SeriesCharacter>()
            .WithMany()
            .HasForeignKey(assignment => new
            {
                assignment.OwnerId,
                assignment.SeriesId,
                assignment.CharacterId
            })
            .HasPrincipalKey(character => new { character.OwnerId, character.SeriesId, character.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ncast_asgn_character_scope");
    }
}
