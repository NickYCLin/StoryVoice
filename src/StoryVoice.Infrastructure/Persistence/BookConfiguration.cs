using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Books;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("books");
        builder.HasKey(book => book.Id);
        builder.Property(book => book.OwnerId);
        builder.Property(book => book.Title).HasMaxLength(500).IsRequired();
        builder.Property(book => book.Author).HasMaxLength(300).IsRequired();
        builder.Property(book => book.Language).HasMaxLength(20).IsRequired();
        builder.Property(book => book.OriginalFileName).HasMaxLength(500).IsRequired();
        builder.Property(book => book.FileType).HasMaxLength(20).IsRequired();
        builder.Property(book => book.StoragePath).HasMaxLength(500);
        builder.Property(book => book.ContentBookId);
        builder.Property(book => book.SourceProvider).HasMaxLength(50);
        builder.Property(book => book.ExternalSourceId).HasMaxLength(128);
        builder.Property(book => book.SourceUrl).HasMaxLength(2_000);
        builder.Property(book => book.CoverImageUrl).HasMaxLength(2_000);
        builder.Property(book => book.TitleCorrection).HasMaxLength(500);
        builder.Property(book => book.AuthorCorrection).HasMaxLength(300);
        builder.Property(book => book.CoverImageUrlCorrection).HasMaxLength(2_000);
        builder.Property(book => book.NativeTtsAvailable);
        builder.Property(book => book.EbookLayout).HasConversion<string>().HasMaxLength(20);
        builder.Property(book => book.SourceSyncedAt);
        builder.Property(book => book.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(book => book.CreatedAt).IsRequired();
        builder.HasIndex(book => new { book.OwnerId, book.SourceProvider, book.ExternalSourceId })
            .IsUnique()
            .HasFilter("\"OwnerId\" IS NOT NULL AND \"SourceProvider\" IS NOT NULL AND \"ExternalSourceId\" IS NOT NULL");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(book => book.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(book => book.ContentBookId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(book => book.ContentBookId);
        builder.HasMany(book => book.Chapters)
            .WithOne()
            .HasForeignKey(chapter => chapter.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(book => book.Chapters)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
