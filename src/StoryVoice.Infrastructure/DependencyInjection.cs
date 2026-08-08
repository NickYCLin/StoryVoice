using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Books;
using StoryVoice.Infrastructure.BookImports;
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
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddSingleton<IBookImportParser, PlainTextBookParser>();
        return services;
    }
}
