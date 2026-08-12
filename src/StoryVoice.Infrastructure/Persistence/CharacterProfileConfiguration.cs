using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Characters;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class CharacterProfileConfiguration : IEntityTypeConfiguration<CharacterProfile>
{
    public void Configure(EntityTypeBuilder<CharacterProfile> builder)
    {
        builder.ToTable("character_profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.HasAlternateKey(profile => new { profile.OwnerId, profile.Id })
            .HasName("AK_character_profiles_scope");

        builder.Property(profile => profile.CanonicalName).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.AvatarRelativePath).HasMaxLength(500);
        builder.Property(profile => profile.Age).HasMaxLength(100);
        builder.Property(profile => profile.Gender).HasMaxLength(100);
        builder.Property(profile => profile.Birthday).HasMaxLength(100);
        builder.Property(profile => profile.Personality).HasMaxLength(2_000);
        builder.Property(profile => profile.Catchphrase).HasMaxLength(2_000);
        builder.Property(profile => profile.Background).HasMaxLength(4_000);
        builder.Property(profile => profile.SpeakingStyle).HasMaxLength(2_000);
        builder.Property(profile => profile.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasIndex(profile => new { profile.OwnerId, profile.CanonicalName })
            .HasDatabaseName("IX_character_profiles_owner_name");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(profile => profile.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
