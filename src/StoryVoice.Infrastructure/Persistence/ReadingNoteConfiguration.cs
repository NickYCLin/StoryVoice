using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Insights;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class ReadingNoteConfiguration : IEntityTypeConfiguration<ReadingNote>
{
    public void Configure(EntityTypeBuilder<ReadingNote> builder)
    {
        builder.ToTable("reading_notes");
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Body).HasMaxLength(4_000).IsRequired();
        builder.Property(note => note.CreatedAt).IsRequired();
        builder.Property(note => note.UpdatedAt).IsRequired();
        builder.HasIndex(note => new { note.OwnerId, note.BookId, note.UpdatedAt });
        builder.HasOne<StoryVoice.Domain.Books.Book>()
            .WithMany()
            .HasForeignKey(note => note.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoryVoice.Domain.Books.Chapter>()
            .WithMany()
            .HasForeignKey(note => note.ChapterId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
