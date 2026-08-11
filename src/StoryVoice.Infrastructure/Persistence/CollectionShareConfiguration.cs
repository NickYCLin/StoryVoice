using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Collections;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class CollectionShareConfiguration : IEntityTypeConfiguration<CollectionShare>
{
    public void Configure(EntityTypeBuilder<CollectionShare> builder)
    {
        builder.ToTable("collection_shares");
        builder.HasKey(share => share.Id);
        builder.Property(share => share.Id).ValueGeneratedNever();
        builder.Property(share => share.GranteeEmail)
            .HasMaxLength(CollectionFieldLimits.GranteeEmail)
            .IsRequired();
        builder.HasIndex(share => new { share.CollectionId, share.GranteeUserId })
            .HasDatabaseName("UX_collection_shares_collection_grantee")
            .IsUnique();
        builder.HasIndex(share => share.GranteeUserId)
            .HasDatabaseName("IX_collection_shares_grantee");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(share => share.GranteeUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
