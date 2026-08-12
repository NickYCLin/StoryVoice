using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Characters;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SeriesCharacterConfiguration : IEntityTypeConfiguration<SeriesCharacter>
{
    public void Configure(EntityTypeBuilder<SeriesCharacter> builder)
    {
        builder.ToTable("series_characters", table =>
        {
            table.HasCheckConstraint(
                "CK_series_characters_role",
                "\"Role\" IN ('Main', 'Supporting', 'Minor')");
        });
        builder.HasKey(character => character.Id);
        builder.Property(character => character.Id).ValueGeneratedNever();
        builder.HasAlternateKey(character => new { character.OwnerId, character.SeriesId, character.Id });
        builder.Property(character => character.CanonicalName)
            .HasMaxLength(SeriesFieldLimits.CharacterName)
            .IsRequired();
        builder.Property(character => character.NormalizedName)
            .HasMaxLength(SeriesFieldLimits.CharacterName)
            .IsRequired();
        builder.Property(character => character.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(character => character.VoiceProvider)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(character => character.Voice)
            .HasMaxLength(SeriesFieldLimits.Voice)
            .IsRequired();
        builder.Property(character => character.Rate)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(character => character.Pitch)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(character => character.Volume)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(character => character.Notes)
            .HasMaxLength(SeriesFieldLimits.Notes);
        builder.Property<string>("CanonicalIdentityKeyKind")
            .HasMaxLength(20)
            .HasComputedColumnSql("'Canonical'", stored: true)
            .IsRequired();
        builder.Property(character => character.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasIndex(character => new { character.OwnerId, character.CharacterProfileId })
            .HasDatabaseName("IX_series_characters_character_profile");
        builder.HasOne<CharacterProfile>()
            .WithMany()
            .HasForeignKey(character => new { character.OwnerId, character.CharacterProfileId })
            .HasPrincipalKey(profile => new { profile.OwnerId, profile.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_series_characters_character_profile");
    }
}
