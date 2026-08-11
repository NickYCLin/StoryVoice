using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SeriesCharacterIdentityKeyConfiguration
    : IEntityTypeConfiguration<SeriesCharacterIdentityKey>
{
    public void Configure(EntityTypeBuilder<SeriesCharacterIdentityKey> builder)
    {
        builder.ToTable("series_character_identity_keys", table =>
        {
            table.HasCheckConstraint(
                "CK_series_character_identity_keys_kind",
                "\"Kind\" IN ('Canonical', 'Alias')");
        });
        builder.HasKey(identityKey => identityKey.Id);
        builder.Property(identityKey => identityKey.Id).ValueGeneratedNever();
        builder.Property(identityKey => identityKey.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(identityKey => identityKey.Value)
            .HasMaxLength(SeriesFieldLimits.Alias)
            .IsRequired();
        builder.Property(identityKey => identityKey.NormalizedValue)
            .HasMaxLength(SeriesFieldLimits.Alias)
            .IsRequired();
        builder.HasIndex(identityKey => new
        {
            identityKey.OwnerId,
            identityKey.SeriesId,
            identityKey.NormalizedValue
        })
            .HasDatabaseName("UX_char_keys_series_value")
            .IsUnique();
        builder.HasIndex(identityKey => new
        {
            identityKey.OwnerId,
            identityKey.SeriesId,
            identityKey.CharacterId,
            identityKey.Id,
            identityKey.Kind
        })
            .HasDatabaseName("UX_char_keys_canonical_target")
            .IsUnique();
        builder.HasOne<SeriesCharacter>()
            .WithMany()
            .HasForeignKey(identityKey => new
            {
                identityKey.OwnerId,
                identityKey.SeriesId,
                identityKey.CharacterId
            })
            .HasPrincipalKey(character => new
            {
                character.OwnerId,
                character.SeriesId,
                character.Id
            })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_char_keys_character_scope")
            .IsRequired();
    }
}
