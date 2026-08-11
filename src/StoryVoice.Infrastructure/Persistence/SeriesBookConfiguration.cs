using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SeriesBookConfiguration : IEntityTypeConfiguration<SeriesBook>
{
    public void Configure(EntityTypeBuilder<SeriesBook> builder)
    {
        builder.ToTable("series_books", table =>
        {
            table.HasCheckConstraint(
                "CK_series_books_sort_order",
                $"\"SortOrder\" >= 0 AND \"SortOrder\" <= {SeriesFieldLimits.MaximumSortOrder}");
            table.HasCheckConstraint(
                "CK_series_books_membership_revision",
                "\"MembershipRevision\" >= 1");
        });
        builder.HasKey(book => book.Id);
        builder.Property(book => book.Id).ValueGeneratedNever();
        builder.Property(book => book.VolumeLabel)
            .HasMaxLength(SeriesFieldLimits.VolumeLabel)
            .IsRequired();
        builder.HasIndex(book => new { book.OwnerId, book.BookId })
            .HasDatabaseName("UX_series_books_owner_book")
            .IsUnique();
        builder.HasIndex(book => new { book.OwnerId, book.SeriesId, book.SortOrder }).IsUnique();
    }
}
