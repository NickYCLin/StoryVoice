using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Books;
using StoryVoice.Application.Insights;
using StoryVoice.Application.Library;
using StoryVoice.Application.Narrations;
using StoryVoice.Infrastructure.BookImports;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Narrations;
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
        services.AddOptions<NarrationOptions>()
            .Bind(configuration.GetSection(NarrationOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.AudioRootPath), "Narration audio root is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Voice), "Narration voice is required.")
            .Validate(options => options.MaxAttempts is >= 1 and <= 10, "Narration attempts must be between 1 and 10.")
            .Validate(options => options.ProviderTimeoutMinutes >= 1, "Narration provider timeout must be positive.")
            .Validate(options => options.LeaseMinutes > options.ProviderTimeoutMinutes, "Narration lease must exceed provider timeout.")
            .ValidateOnStart();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IBookMetadataCorrectionService, BookMetadataCorrectionService>();
        services.AddScoped<IBookInsightsService, BookInsightsService>();
        services.AddScoped<ILibraryStatusService, LibraryStatusService>();
        services.AddScoped<INarrationService, NarrationService>();
        services.AddScoped<CompanionTokenService>();
        services.AddSingleton<IBookImportParser, PlainTextBookParser>();
        services.AddSingleton<IBookImportParser, EpubBookParser>();
        services.AddSingleton<IBookFileStorage, LocalBookFileStorage>();
        return services;
    }
}
