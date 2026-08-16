using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Insights;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookLocalLlmCharacterAnalysisConfiguration
    : IEntityTypeConfiguration<BookLocalLlmCharacterAnalysis>
{
    public void Configure(EntityTypeBuilder<BookLocalLlmCharacterAnalysis> builder)
    {
        builder.ToTable("book_local_llm_character_analyses");
        builder.HasKey(analysis => analysis.BookId);
        builder.Property(analysis => analysis.Generator).HasMaxLength(30).IsRequired();
        builder.Property(analysis => analysis.Model).HasMaxLength(160).IsRequired();
        builder.Property(analysis => analysis.PromptVersion).HasMaxLength(80).IsRequired();
        builder.Property(analysis => analysis.SourceHash).HasMaxLength(128).IsRequired();
        builder.Property(analysis => analysis.CandidatesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(analysis => analysis.GeneratedAt).IsRequired();
        builder.HasIndex(analysis => new { analysis.OwnerId, analysis.ContentBookId });
        // books.OwnerId is intentionally nullable for retained legacy imports, so EF cannot model
        // it as an alternate key without silently making those rows required. The following simple
        // relationships remain for EF; migration 20260816154216 additionally installs database-level
        // composite owner FKs for this owner-scoped review-only data.
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithOne()
            .HasForeignKey<BookLocalLlmCharacterAnalysis>(analysis => analysis.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithMany()
            .HasForeignKey(analysis => analysis.ContentBookId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
