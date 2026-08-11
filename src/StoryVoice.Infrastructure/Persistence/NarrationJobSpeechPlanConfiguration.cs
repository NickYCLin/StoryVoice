using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class NarrationJobSpeechPlanConfiguration : IEntityTypeConfiguration<NarrationJobSpeechPlan>
{
    public void Configure(EntityTypeBuilder<NarrationJobSpeechPlan> builder)
    {
        builder.ToTable("narration_job_speech_plans", table =>
        {
            table.HasCheckConstraint("CK_njob_speech_plans_chapter_sort_order", "\"ChapterSortOrder\" >= 0");
        });
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.HasIndex(link => new { link.OwnerId, link.SeriesId, link.NarrationJobId, link.ChapterSortOrder })
            .HasDatabaseName("UX_njob_speech_plans_chapter")
            .IsUnique();
        builder.HasIndex(link => new
        {
            link.OwnerId,
            link.SeriesId,
            link.NarrationJobId,
            link.ConfirmedSpeechPlanRevisionId
        })
            .HasDatabaseName("UX_njob_speech_plans_revision")
            .IsUnique();
        // NarrationJob.SeriesId is nullable (null for SingleVoice jobs), so it cannot join an
        // EF alternate key without forcing that column non-null — that broke the existing
        // SingleVoice compatibility constraint when scaffolded. OwnerId/SeriesId stay as plain
        // denormalized columns (populated from the job in the same transaction, indexed above
        // for scoped queries) while the actual referential guarantee is this single-column FK to
        // the job's real primary key.
        builder.HasOne<NarrationJob>()
            .WithMany()
            .HasForeignKey(link => link.NarrationJobId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_njob_speech_plans_job_scope");
        builder.HasOne<ConfirmedSpeechPlanRevision>()
            .WithMany()
            .HasForeignKey(link => new { link.OwnerId, link.SeriesId, link.ConfirmedSpeechPlanRevisionId })
            .HasPrincipalKey(revision => new { revision.OwnerId, revision.SeriesId, revision.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_njob_speech_plans_revision_scope");
    }
}
