using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Books;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable("chapters");
        builder.HasKey(chapter => chapter.Id);
        builder.Property(chapter => chapter.Title).HasMaxLength(500).IsRequired();
        builder.Property(chapter => chapter.OriginalText).IsRequired();
        builder.HasIndex(chapter => new { chapter.BookId, chapter.ChapterNumber }).IsUnique();
        builder.HasIndex(chapter => new { chapter.BookId, chapter.SortOrder }).IsUnique();
    }
}
