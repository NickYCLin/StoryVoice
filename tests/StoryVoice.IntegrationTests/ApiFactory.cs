using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StoryVoice.Domain.Series;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    static ApiFactory()
    {
        Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");
    }

    private readonly string _databaseName = $"storyvoice-tests-{Guid.NewGuid()}";
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        "storyvoice-integration-tests",
        Guid.NewGuid().ToString("N"));

    public string StorageRoot => _storageRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("BookStorage:RootPath", _storageRoot);
        builder.UseSetting("Narration:AudioRootPath", Path.Combine(_storageRoot, "audio"));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<StoryVoiceDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<StoryVoiceDbContext>>();
            services.AddDbContext<StoryVoiceDbContext>(options =>
                options
                    .UseInMemoryDatabase(_databaseName)
                    .AddInterceptors(new ComputedCanonicalKindInterceptor()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private sealed class ComputedCanonicalKindInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            SetComputedValues(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SetComputedValues(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void SetComputedValues(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            foreach (var entry in context.ChangeTracker.Entries<SeriesCharacter>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                entry.Property<string>("CanonicalIdentityKeyKind").CurrentValue = "Canonical";
            }
        }
    }
}
