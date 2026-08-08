using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StoryVoice.Infrastructure.Identity;

internal sealed class CompanionAccessTokenConfiguration : IEntityTypeConfiguration<CompanionAccessToken>
{
    public void Configure(EntityTypeBuilder<CompanionAccessToken> builder)
    {
        builder.ToTable("companion_access_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.TokenHint).HasMaxLength(12).IsRequired();
        builder.Property(token => token.CreatedAt).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.RevokedAt });
        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
