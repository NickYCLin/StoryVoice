using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Books;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("books");
        builder.HasKey(book => book.Id);
        builder.Property(book => book.Title).HasMaxLength(500).IsRequired();
        builder.Property(book => book.Author).HasMaxLength(300).IsRequired();
        builder.Property(book => book.Language).HasMaxLength(20).IsRequired();
        builder.Property(book => book.OriginalFileName).HasMaxLength(500).IsRequired();
        builder.Property(book => book.FileType).HasMaxLength(20).IsRequired();
        builder.Property(book => book.StoragePath).HasMaxLength(500);
        builder.Property(book => book.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(book => book.CreatedAt).IsRequired();
        builder.HasMany(book => book.Chapters)
            .WithOne()
            .HasForeignKey(chapter => chapter.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(book => book.Chapters)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
