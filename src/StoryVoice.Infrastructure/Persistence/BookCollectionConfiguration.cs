using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Collections;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class BookCollectionConfiguration : IEntityTypeConfiguration<BookCollection>
{
    public void Configure(EntityTypeBuilder<BookCollection> builder)
    {
        builder.ToTable("book_collections");
        builder.HasKey(collection => collection.Id);
        builder.Property(collection => collection.Id).ValueGeneratedNever();
        builder.HasAlternateKey(collection => new { collection.OwnerId, collection.Id });
        builder.Property(collection => collection.Name)
            .HasMaxLength(CollectionFieldLimits.CollectionName)
            .IsRequired();
        builder.Property(collection => collection.NormalizedName)
            .HasMaxLength(CollectionFieldLimits.CollectionName)
            .IsRequired();
        builder.Property(collection => collection.Description)
            .HasMaxLength(CollectionFieldLimits.Description);
        builder.Property(collection => collection.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(collection => new { collection.OwnerId, collection.NormalizedName }).IsUnique();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(collection => collection.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(collection => collection.Books)
            .WithOne()
            .HasForeignKey(book => new { book.OwnerId, book.CollectionId })
            .HasPrincipalKey(collection => new { collection.OwnerId, collection.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(collection => collection.Shares)
            .WithOne()
            .HasForeignKey(share => new { share.OwnerId, share.CollectionId })
            .HasPrincipalKey(collection => new { collection.OwnerId, collection.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(collection => collection.Books)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(collection => collection.Shares)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
