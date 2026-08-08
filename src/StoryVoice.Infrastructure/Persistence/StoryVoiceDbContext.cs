using Microsoft.EntityFrameworkCore;
using StoryVoice.Domain.Books;

namespace StoryVoice.Infrastructure.Persistence;

public sealed class StoryVoiceDbContext(DbContextOptions<StoryVoiceDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();

    public DbSet<Chapter> Chapters => Set<Chapter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoryVoiceDbContext).Assembly);
    }
}
