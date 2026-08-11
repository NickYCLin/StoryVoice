using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SpeechSegmentDraftConfiguration : IEntityTypeConfiguration<SpeechSegmentDraft>
{
    public void Configure(EntityTypeBuilder<SpeechSegmentDraft> builder)
    {
        builder.ToTable("speech_segment_drafts", table =>
        {
            table.HasCheckConstraint("CK_speech_segment_drafts_sort_order", "\"SortOrder\" >= 0");
            table.HasCheckConstraint("CK_speech_segment_drafts_start_offset", "\"StartOffset\" >= 0");
            table.HasCheckConstraint("CK_speech_segment_drafts_length", "\"Length\" > 0");
            table.HasCheckConstraint(
                "CK_speech_segment_drafts_confidence",
                "\"Confidence\" >= 0 AND \"Confidence\" <= 100");
            table.HasCheckConstraint(
                "CK_speech_segment_drafts_source_kind",
                "\"SourceKind\" IN ('ChapterTitle', 'Body')");
            table.HasCheckConstraint("CK_speech_segment_drafts_kind", "\"Kind\" IN ('Narrator', 'Dialogue')");
            table.HasCheckConstraint(
                "CK_speech_segment_drafts_decision_source",
                "\"DecisionSource\" IN ('Rule', 'LocalModel', 'User')");
            table.HasCheckConstraint(
                "CK_speech_segment_drafts_review_status",
                "\"ReviewStatus\" IN ('Suggested', 'Confirmed', 'Rejected')");
            table.HasCheckConstraint(
                "CK_speech_segment_drafts_narrator_no_character",
                "\"Kind\" <> 'Narrator' OR \"CharacterId\" IS NULL");
        });
        builder.HasKey(segment => segment.Id);
        builder.Property(segment => segment.Id).ValueGeneratedNever();
        builder.Property(segment => segment.TextHash).HasMaxLength(128).IsRequired();
        builder.Property(segment => segment.SourceKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.DecisionSource).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.ReviewStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.CharacterId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(segment => new { segment.OwnerId, segment.SeriesId, segment.PlanDraftId, segment.SortOrder })
            .HasDatabaseName("UX_speech_segment_drafts_order")
            .IsUnique();
        builder.HasOne<SeriesCharacter>()
            .WithMany()
            .HasForeignKey(segment => new { segment.OwnerId, segment.SeriesId, segment.CharacterId })
            .HasPrincipalKey(character => new { character.OwnerId, character.SeriesId, character.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_speech_segment_drafts_character_scope");
    }
}
