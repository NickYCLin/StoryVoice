using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class ConfirmedSpeechPlanRevisionConfiguration : IEntityTypeConfiguration<ConfirmedSpeechPlanRevision>
{
    public void Configure(EntityTypeBuilder<ConfirmedSpeechPlanRevision> builder)
    {
        builder.ToTable("confirmed_speech_plan_revisions", table =>
        {
            table.HasCheckConstraint("CK_confirmed_speech_plans_revision", "\"RevisionNumber\" >= 1");
            table.HasCheckConstraint("CK_confirmed_speech_plans_fingerprint", "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.HasAlternateKey(revision => new { revision.OwnerId, revision.SeriesId, revision.Id });
        builder.Property(revision => revision.SourceHash).HasMaxLength(128).IsRequired();
        builder.Property(revision => revision.Fingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(revision => new
        {
            revision.OwnerId,
            revision.SeriesId,
            revision.BookId,
            revision.ChapterId,
            revision.RevisionNumber
        })
            .HasDatabaseName("UX_confirmed_speech_plans_revision")
            .IsUnique();
        builder.HasOne<StorySeries>()
            .WithMany()
            .HasForeignKey(revision => new { revision.OwnerId, revision.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_confirmed_speech_plans_series_scope");
        builder.HasMany(revision => revision.Segments)
            .WithOne()
            .HasForeignKey(segment => new { segment.OwnerId, segment.SeriesId, segment.PlanRevisionId })
            .HasPrincipalKey(revision => new { revision.OwnerId, revision.SeriesId, revision.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(revision => revision.Segments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
