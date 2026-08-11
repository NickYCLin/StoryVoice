using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;

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
    }
}
