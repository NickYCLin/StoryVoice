using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Insights;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookExtractiveSummaryConfiguration : IEntityTypeConfiguration<BookExtractiveSummary>
{
    public void Configure(EntityTypeBuilder<BookExtractiveSummary> builder)
    {
        builder.ToTable("book_extractive_summaries");
        builder.HasKey(summary => summary.BookId);
        builder.Property(summary => summary.Kind).HasMaxLength(30).IsRequired();
        builder.Property(summary => summary.Generator).HasMaxLength(100).IsRequired();
        builder.Property(summary => summary.Version).HasMaxLength(30).IsRequired();
        builder.Property(summary => summary.SourceHash).HasMaxLength(128).IsRequired();
        builder.Property(summary => summary.ExcerptsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(summary => summary.GeneratedAt).IsRequired();
        builder.HasIndex(summary => new { summary.OwnerId, summary.ContentBookId });
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithOne()
            .HasForeignKey<BookExtractiveSummary>(summary => summary.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithMany()
            .HasForeignKey(summary => summary.ContentBookId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
