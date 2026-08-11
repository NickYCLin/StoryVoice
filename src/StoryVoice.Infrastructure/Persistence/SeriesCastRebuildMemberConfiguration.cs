using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;

namespace StoryVoice.Infrastructure.Persistence;

internal sealed class SeriesCastRebuildMemberConfiguration : IEntityTypeConfiguration<SeriesCastRebuildMember>
{
    public void Configure(EntityTypeBuilder<SeriesCastRebuildMember> builder)
    {
        builder.ToTable("series_cast_rebuild_members", table =>
        {
            table.HasCheckConstraint(
                "CK_rebuild_members_revision",
                "\"MembershipRevision\" >= 1");
            table.HasCheckConstraint(
                "CK_rebuild_members_status",
                "\"Status\" IN ('Pending', 'Building', 'Ready', 'Failed')");
            table.HasCheckConstraint(
                "CK_rebuild_members_pointer",
                "(\"Status\" = 'Pending' AND \"StagedNarrationJobId\" IS NULL) " +
                "OR (\"Status\" IN ('Building', 'Ready') AND \"StagedNarrationJobId\" IS NOT NULL) " +
                "OR \"Status\" = 'Failed'");
            table.HasCheckConstraint(
                "CK_rebuild_members_distinct_jobs",
                "\"StagedNarrationJobId\" IS NULL OR \"PreviousActiveNarrationJobId\" IS NULL " +
                "OR \"StagedNarrationJobId\" <> \"PreviousActiveNarrationJobId\"");
        });

        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();
        builder.HasAlternateKey(member => new
        {
            member.OwnerId,
            member.SeriesId,
            member.BatchId,
            member.Id
        })
            .HasName("AK_rebuild_members_scope");
        builder.Property(member => member.MembershipRevision);
        builder.Property(member => member.PreviousActiveNarrationJobId);
        builder.Property(member => member.StagedNarrationJobId)
            .HasField("_stagedNarrationJobId")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(member => member.Status)
            .HasField("_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(member => new
        {
            member.OwnerId,
            member.SeriesId,
            member.SeriesBookId,
            member.BookId,
            member.MembershipRevision
        })
            .HasDatabaseName("IX_rebuild_members_series_book");
        builder.HasIndex(member => new
        {
            member.OwnerId,
            member.SeriesId,
            member.BatchId,
            member.SeriesBookId
        })
            .HasDatabaseName("UX_rebuild_members_series_book")
            .IsUnique();
        builder.HasIndex(member => new
        {
            member.OwnerId,
            member.SeriesId,
            member.BatchId,
            member.BookId
        })
            .HasDatabaseName("UX_rebuild_members_book")
            .IsUnique();
        builder.HasIndex(member => member.PreviousActiveNarrationJobId)
            .HasDatabaseName("IX_rebuild_members_previous_job")
            .HasFilter("\"PreviousActiveNarrationJobId\" IS NOT NULL");

        builder.HasOne<SeriesBook>()
            .WithMany()
            .HasForeignKey(member => new
            {
                member.OwnerId,
                member.SeriesId,
                member.SeriesBookId,
                member.BookId,
                member.MembershipRevision
            })
            .HasPrincipalKey(seriesBook => new
            {
                seriesBook.OwnerId,
                seriesBook.SeriesId,
                seriesBook.Id,
                seriesBook.BookId,
                seriesBook.MembershipRevision
            })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_rebuild_members_series_book");
    }
}
