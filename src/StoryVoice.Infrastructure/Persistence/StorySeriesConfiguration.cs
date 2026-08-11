using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class StorySeriesConfiguration : IEntityTypeConfiguration<StorySeries>
{
    public void Configure(EntityTypeBuilder<StorySeries> builder)
    {
        builder.ToTable("story_series", table =>
        {
            table.HasCheckConstraint(
                "CK_story_series_default_speaker_pause_ms",
                $"\"DefaultSpeakerPauseMs\" >= 0 AND \"DefaultSpeakerPauseMs\" <= {SeriesFieldLimits.MaximumSpeakerPauseMs}");
        });
        builder.HasKey(series => series.Id);
        builder.Property(series => series.Id).ValueGeneratedNever();
        builder.HasAlternateKey(series => new { series.OwnerId, series.Id });
        builder.Property(series => series.Name)
            .HasMaxLength(SeriesFieldLimits.SeriesName)
            .IsRequired();
        builder.Property(series => series.NormalizedName)
            .HasMaxLength(SeriesFieldLimits.SeriesName)
            .IsRequired();
        builder.Property(series => series.NarratorProvider)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(series => series.NarratorVoice)
            .HasMaxLength(SeriesFieldLimits.Voice)
            .IsRequired();
        builder.Property(series => series.NarratorRate)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(series => series.NarratorPitch)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(series => series.NarratorVolume)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(series => series.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(series => new { series.OwnerId, series.NormalizedName }).IsUnique();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(series => series.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(series => series.Books)
            .WithOne()
            .HasForeignKey(book => new { book.OwnerId, book.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(series => series.Characters)
            .WithOne()
            .HasForeignKey(character => new { character.OwnerId, character.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(series => series.IdentityKeys)
            .WithOne()
            .HasForeignKey(identityKey => new { identityKey.OwnerId, identityKey.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_char_keys_series_scope");
        builder.Navigation(series => series.Books)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(series => series.Characters)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(series => series.IdentityKeys)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
