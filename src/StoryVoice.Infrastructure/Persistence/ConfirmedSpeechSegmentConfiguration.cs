using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class ConfirmedSpeechSegmentConfiguration : IEntityTypeConfiguration<ConfirmedSpeechSegment>
{
    public void Configure(EntityTypeBuilder<ConfirmedSpeechSegment> builder)
    {
        builder.ToTable("confirmed_speech_segments", table =>
        {
            table.HasCheckConstraint("CK_confirmed_speech_segments_sort_order", "\"SortOrder\" >= 0");
            table.HasCheckConstraint("CK_confirmed_speech_segments_start_offset", "\"StartOffset\" >= 0");
            table.HasCheckConstraint("CK_confirmed_speech_segments_length", "\"Length\" > 0");
            table.HasCheckConstraint(
                "CK_confirmed_speech_segments_confidence",
                "\"Confidence\" >= 0 AND \"Confidence\" <= 100");
            table.HasCheckConstraint(
                "CK_confirmed_speech_segments_source_kind",
                "\"SourceKind\" IN ('ChapterTitle', 'Body')");
            table.HasCheckConstraint(
                "CK_confirmed_speech_segments_kind",
                "\"Kind\" IN ('Narrator', 'Dialogue', 'InnerMonologue')");
            table.HasCheckConstraint(
                "CK_confirmed_speech_segments_decision_source",
                "\"DecisionSource\" IN ('Rule', 'LocalModel', 'User')");
            table.HasCheckConstraint(
                "CK_confirmed_speech_segments_narrator_no_character",
                "\"Kind\" <> 'Narrator' OR \"CharacterId\" IS NULL");
            table.HasCheckConstraint(
                "CK_confirmed_speech_segments_inner_monologue_state",
                "\"Kind\" <> 'InnerMonologue' OR (\"CharacterId\" IS NOT NULL AND \"Confidence\" = 100 AND \"DecisionSource\" = 'Rule')");
        });
        builder.HasKey(segment => segment.Id);
        builder.Property(segment => segment.Id).ValueGeneratedNever();
        builder.Property(segment => segment.TextHash).HasMaxLength(128).IsRequired();
        builder.Property(segment => segment.SourceKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.DecisionSource).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(segment => segment.CharacterId).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(segment => new { segment.OwnerId, segment.SeriesId, segment.PlanRevisionId, segment.SortOrder })
            .HasDatabaseName("UX_confirmed_speech_segments_order")
            .IsUnique();
        builder.HasOne<SeriesCharacter>()
            .WithMany()
            .HasForeignKey(segment => new { segment.OwnerId, segment.SeriesId, segment.CharacterId })
            .HasPrincipalKey(character => new { character.OwnerId, character.SeriesId, character.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_confirmed_speech_segments_character_scope");
    }
}
