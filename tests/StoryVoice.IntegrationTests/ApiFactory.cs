using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                options.UseInMemoryDatabase(_databaseName));
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
}
