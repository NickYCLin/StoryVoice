using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class NarrationJobConfiguration : IEntityTypeConfiguration<NarrationJob>
{
    public void Configure(EntityTypeBuilder<NarrationJob> builder)
    {
        builder.ToTable("narration_jobs", table =>
        {
            table.HasCheckConstraint("CK_narration_jobs_progress", "\"ProgressPercent\" >= 0 AND \"ProgressPercent\" <= 100");
            table.HasCheckConstraint("CK_narration_jobs_attempts", "\"Attempts\" >= 0");
            table.HasCheckConstraint("CK_narration_jobs_audio_bytes", "\"AudioBytes\" IS NULL OR \"AudioBytes\" > 0");
            table.HasCheckConstraint("CK_narration_jobs_mode", "\"Mode\" IN ('SingleVoice', 'MultiCharacter')");
            table.HasCheckConstraint(
                "CK_njobs_visibility",
                "\"Visibility\" IN ('Staged', 'Published', 'Historical')");
            table.HasCheckConstraint(
                "CK_njobs_correlations",
                "(\"Mode\" = 'SingleVoice' AND \"Visibility\" <> 'Staged' " +
                "AND \"SeriesId\" IS NULL AND \"CastRevisionId\" IS NULL " +
                "AND \"SpeechPlanRevisionId\" IS NULL AND \"RebuildBatchId\" IS NULL " +
                "AND \"RebuildMemberId\" IS NULL) OR " +
                "(\"Mode\" = 'MultiCharacter' AND \"SeriesId\" IS NOT NULL " +
                "AND \"CastRevisionId\" IS NOT NULL AND \"SpeechPlanRevisionId\" IS NOT NULL " +
                "AND \"RebuildBatchId\" IS NOT NULL AND \"RebuildMemberId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_njobs_published_audio",
                "\"Mode\" <> 'MultiCharacter' OR \"Visibility\" = 'Staged' OR " +
                "(\"Visibility\" IN ('Published', 'Historical') AND \"Status\" = 'Completed' " +
                "AND \"AudioRelativePath\" IS NOT NULL AND \"AudioBytes\" IS NOT NULL AND \"AudioBytes\" > 0)");
        });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.SourceHash).HasMaxLength(128).IsRequired();
        builder.Property(job => job.Voice).HasMaxLength(200).IsRequired();
        builder.Property(job => job.Rate).HasMaxLength(20).IsRequired();
        builder.Property(job => job.Mode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(NarrationMode.SingleVoice);
        builder.Property(job => job.Visibility)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(NarrationArtifactVisibility.Published)
            .HasSentinel((NarrationArtifactVisibility)(-1));
        builder.Property(job => job.SeriesId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(job => job.CastRevisionId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(job => job.SpeechPlanRevisionId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(job => job.RebuildBatchId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(job => job.RebuildMemberId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(job => job.LeaseOwner).HasMaxLength(200);
        builder.Property(job => job.ErrorCode).HasMaxLength(100);
        builder.Property(job => job.AudioRelativePath).HasMaxLength(1_000);
        builder.Property(job => job.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(job => new
        {
            job.OwnerId,
            job.BookId,
            job.ContentBookId,
            job.SourceHash,
            job.Voice,
            job.Rate
        })
            .IsUnique()
            .HasFilter("\"Mode\" = 'SingleVoice'");
        builder.HasIndex(job => new { job.OwnerId, job.SeriesId, job.CastRevisionId })
            .HasDatabaseName("IX_njobs_cast_scope");
        builder.HasIndex(job => new
        {
            job.OwnerId,
            job.SeriesId,
            job.RebuildBatchId,
            job.CastRevisionId
        })
            .HasDatabaseName("IX_njobs_batch_cast");
        builder.HasIndex(job => new { job.OwnerId, job.BookId, job.Id })
            .HasDatabaseName("UX_njobs_owner_book_id")
            .IsUnique();
        builder.HasIndex(job => new
        {
            job.OwnerId,
            job.SeriesId,
            job.RebuildBatchId,
            job.RebuildMemberId,
            job.BookId,
            job.Id
        })
            .HasDatabaseName("UX_njobs_member_artifact")
            .IsUnique();
        builder.HasIndex(job => new
        {
            job.OwnerId,
            job.SeriesId,
            job.RebuildBatchId,
            job.RebuildMemberId
        })
            .HasDatabaseName("UX_njobs_multi_member")
            .IsUnique()
            .HasFilter("\"Mode\" = 'MultiCharacter'");
        builder.HasIndex(job => new { job.Status, job.NextAttemptAt, job.CreatedAt });
        builder.HasIndex(job => new { job.Status, job.LeaseExpiresAt });
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithMany()
            .HasForeignKey(job => job.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithMany()
            .HasForeignKey(job => job.ContentBookId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorySeries>()
            .WithMany()
            .HasForeignKey(job => new { job.OwnerId, job.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_njobs_series_scope");
        builder.HasOne<NarrationCastRevision>()
            .WithMany()
            .HasForeignKey(job => new { job.OwnerId, job.SeriesId, job.CastRevisionId })
            .HasPrincipalKey(revision => new { revision.OwnerId, revision.SeriesId, revision.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_njobs_cast_scope");
        builder.HasOne<SeriesCastRebuildMember>()
            .WithMany()
            .HasForeignKey(job => new
            {
                job.OwnerId,
                job.SeriesId,
                job.RebuildBatchId,
                job.RebuildMemberId
            })
            .HasPrincipalKey(member => new
            {
                member.OwnerId,
                member.SeriesId,
                member.BatchId,
                member.Id
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_njobs_rebuild_member_scope");
        builder.HasOne<SeriesCastRebuildBatch>()
            .WithMany()
            .HasForeignKey(job => new
            {
                job.OwnerId,
                job.SeriesId,
                job.RebuildBatchId,
                job.CastRevisionId
            })
            .HasPrincipalKey(batch => new
            {
                batch.OwnerId,
                batch.SeriesId,
                batch.Id,
                batch.DraftCastRevisionId
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_njobs_batch_cast");
    }
}
