using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StoryVoice.Domain.Books;
using StoryVoice.Domain.Insights;
using StoryVoice.Domain.Narrations;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

public sealed class StoryVoiceDbContext(DbContextOptions<StoryVoiceDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Chapter> Chapters => Set<Chapter>();

    public DbSet<ReadingNote> ReadingNotes => Set<ReadingNote>();

    public DbSet<BookExtractiveSummary> BookExtractiveSummaries => Set<BookExtractiveSummary>();

    public DbSet<NarrationJob> NarrationJobs => Set<NarrationJob>();

    public DbSet<StorySeries> StorySeries => Set<StorySeries>();

    public DbSet<SeriesBook> SeriesBooks => Set<SeriesBook>();

    public DbSet<SeriesCharacter> SeriesCharacters => Set<SeriesCharacter>();

    public DbSet<SeriesCharacterIdentityKey> SeriesCharacterIdentityKeys =>
        Set<SeriesCharacterIdentityKey>();

    public DbSet<CompanionAccessToken> CompanionAccessTokens => Set<CompanionAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoryVoiceDbContext).Assembly);
    }
}
