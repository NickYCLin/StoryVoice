using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Books;
using StoryVoice.Infrastructure.BookImports;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Persistence;

namespace StoryVoice.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddStoryVoiceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        services.AddDbContext<StoryVoiceDbContext>(options =>
            options.UseNpgsql(connectionString));
        services.Configure<BookStorageOptions>(options =>
            options.RootPath = configuration[$"{BookStorageOptions.SectionName}:RootPath"]
                ?? options.RootPath);
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<CompanionTokenService>();
        services.AddSingleton<IBookImportParser, PlainTextBookParser>();
        services.AddSingleton<IBookImportParser, EpubBookParser>();
        services.AddSingleton<IBookFileStorage, LocalBookFileStorage>();
        return services;
    }
}
