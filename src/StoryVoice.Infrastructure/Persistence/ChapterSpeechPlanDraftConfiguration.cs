using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class ChapterSpeechPlanDraftConfiguration : IEntityTypeConfiguration<ChapterSpeechPlanDraft>
{
    public void Configure(EntityTypeBuilder<ChapterSpeechPlanDraft> builder)
    {
        builder.ToTable("chapter_speech_plan_drafts", table =>
        {
            table.HasCheckConstraint(
                "CK_speech_plan_drafts_status",
                "\"Status\" IN ('Draft', 'NeedsReview', 'ReadyToConfirm', 'Stale')");
            table.HasCheckConstraint("CK_speech_plan_drafts_version", "\"PlanVersion\" >= 1");
        });
        builder.HasKey(draft => draft.Id);
        builder.Property(draft => draft.Id).ValueGeneratedNever();
        builder.HasAlternateKey(draft => new { draft.OwnerId, draft.SeriesId, draft.Id });
        builder.Property(draft => draft.SourceHash).HasMaxLength(128).IsRequired();
        builder.Property(draft => draft.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(draft => new { draft.OwnerId, draft.SeriesId, draft.BookId, draft.ChapterId })
            .HasDatabaseName("UX_speech_plan_drafts_chapter")
            .IsUnique();
        builder.HasOne<StorySeries>()
            .WithMany()
            .HasForeignKey(draft => new { draft.OwnerId, draft.SeriesId })
            .HasPrincipalKey(series => new { series.OwnerId, series.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_speech_plan_drafts_series_scope");
        builder.HasMany(draft => draft.Segments)
            .WithOne()
            .HasForeignKey(segment => new { segment.OwnerId, segment.SeriesId, segment.PlanDraftId })
            .HasPrincipalKey(draft => new { draft.OwnerId, draft.SeriesId, draft.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(draft => draft.Segments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
