using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class NarrationCastRevisionConfiguration : IEntityTypeConfiguration<NarrationCastRevision>
{
    public void Configure(EntityTypeBuilder<NarrationCastRevision> builder)
    {
        builder.ToTable("narration_cast_revisions", table =>
        {
            table.HasCheckConstraint(
                "CK_ncast_revs_revision",
                "\"RevisionNumber\" >= 1");
            table.HasCheckConstraint(
                "CK_ncast_revs_status",
                "\"Status\" IN ('Draft', 'Active', 'Historical')");
            table.HasCheckConstraint(
                "CK_ncast_revs_epoch",
                "(\"Status\" = 'Draft' AND \"EpochNumber\" IS NULL AND \"ActivatedAt\" IS NULL) " +
                "OR (\"Status\" IN ('Active', 'Historical') AND \"EpochNumber\" IS NOT NULL " +
                "AND \"EpochNumber\" >= 1 AND \"ActivatedAt\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_ncast_revs_pauses",
                $"\"DefaultSpeakerPauseMs\" >= 0 AND \"DefaultSpeakerPauseMs\" <= {SeriesFieldLimits.MaximumSpeakerPauseMs} " +
                $"AND \"ChapterPauseMs\" >= 0 AND \"ChapterPauseMs\" <= {SeriesFieldLimits.MaximumSpeakerPauseMs}");
            table.HasCheckConstraint(
                "CK_ncast_revs_fingerprint",
                "\"Fingerprint\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "CK_ncast_revs_chronology",
                "\"ActivatedAt\" IS NULL OR \"ActivatedAt\" >= \"CreatedAt\"");
        });

        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.HasAlternateKey(revision => new { revision.OwnerId, revision.SeriesId, revision.Id })
            .HasName("AK_ncast_revs_scope");
        builder.Property(revision => revision.RevisionNumber);
        builder.Property(revision => revision.DefaultSpeakerPauseMs);
        builder.Property(revision => revision.ChapterPauseMs);
        builder.Property(revision => revision.CreatedAt);
        builder.Property(revision => revision.Fingerprint)
            .HasColumnType("character(64)")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(revision => revision.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(revision => revision.NarratorProvider)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(revision => revision.NarratorProviderVersion)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(revision => revision.NarratorVoice)
            .HasMaxLength(SeriesFieldLimits.Voice)
            .IsRequired();
        builder.Property(revision => revision.NarratorRate)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(revision => revision.NarratorPitch)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(revision => revision.NarratorVolume)
            .HasMaxLength(SeriesFieldLimits.SynthesisParameter)
            .IsRequired();
        builder.Property(revision => revision.CompositionVersion)
            .HasMaxLength(SeriesFieldLimits.Provider)
            .IsRequired();
        builder.Property(revision => revision.FfmpegProfile)
            .HasMaxLength(SeriesFieldLimits.Voice)
            .IsRequired();

        builder.HasIndex(revision => new { revision.OwnerId, revision.SeriesId, revision.RevisionNumber })
            .HasDatabaseName("UX_ncast_revs_number")
            .IsUnique();
        builder.HasIndex(revision => new { revision.OwnerId, revision.SeriesId, revision.Fingerprint })
            .HasDatabaseName("UX_ncast_revs_fingerprint")
            .IsUnique();
        builder.HasIndex(revision => new { revision.OwnerId, revision.SeriesId })
            .HasDatabaseName("UX_ncast_revs_active")
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
        builder.HasIndex(revision => new { revision.OwnerId, revision.SeriesId, revision.EpochNumber })
            .HasDatabaseName("UX_ncast_revs_epoch")
            .IsUnique()
            .HasFilter("\"EpochNumber\" IS NOT NULL");

        builder.HasOne<StorySeries>()
            .WithMany()
            .HasForeignKey(revision => new { revision.OwnerId, revision.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ncast_revs_series_scope");
        builder.HasMany(revision => revision.Assignments)
            .WithOne()
            .HasForeignKey(assignment => new
            {
                assignment.OwnerId,
                assignment.SeriesId,
                assignment.CastRevisionId
            })
            .HasPrincipalKey(revision => new { revision.OwnerId, revision.SeriesId, revision.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_ncast_asgn_revision_scope");
        builder.Navigation(revision => revision.Assignments)
            .HasField("_assignments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
