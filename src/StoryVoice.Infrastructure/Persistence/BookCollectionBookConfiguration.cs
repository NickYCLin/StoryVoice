using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Collections;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookCollectionBookConfiguration : IEntityTypeConfiguration<BookCollectionBook>
{
    public void Configure(EntityTypeBuilder<BookCollectionBook> builder)
    {
        builder.ToTable("book_collection_books", table =>
        {
            table.HasCheckConstraint(
                "CK_book_collection_books_sort_order",
                $"\"SortOrder\" >= 0 AND \"SortOrder\" <= {CollectionFieldLimits.MaximumSortOrder}");
        });
        builder.HasKey(book => book.Id);
        builder.Property(book => book.Id).ValueGeneratedNever();
        builder.Property(book => book.VolumeLabel).HasMaxLength(CollectionFieldLimits.VolumeLabel);
        builder.HasIndex(book => new { book.OwnerId, book.CollectionId, book.BookId })
            .HasDatabaseName("UX_collection_books_owner_collection_book")
            .IsUnique();
        builder.HasIndex(book => new { book.OwnerId, book.CollectionId, book.SortOrder })
            .HasDatabaseName("UX_collection_books_owner_collection_sort")
            .IsUnique();
    }
}
