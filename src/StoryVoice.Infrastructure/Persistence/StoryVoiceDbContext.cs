using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StoryVoice.Domain.Books;
using StoryVoice.Infrastructure.Identity;

namespace StoryVoice.Infrastructure.Persistence;

public sealed class StoryVoiceDbContext(DbContextOptions<StoryVoiceDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Chapter> Chapters => Set<Chapter>();

    public DbSet<CompanionAccessToken> CompanionAccessTokens => Set<CompanionAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoryVoiceDbContext).Assembly);
    }
}
