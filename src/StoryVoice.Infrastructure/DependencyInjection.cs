using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Books;
using StoryVoice.Application.Characters;
using StoryVoice.Application.Collections;
using StoryVoice.Application.Insights;
using StoryVoice.Application.Library;
using StoryVoice.Application.Narrations;
using StoryVoice.Application.Narrations.SpeechPlanning;
using StoryVoice.Application.Series;
using StoryVoice.Infrastructure.BookImports;
using StoryVoice.Infrastructure.Characters;
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
        services.Configure<CharacterVoiceStorageOptions>(options =>
            options.RootPath = configuration[$"{CharacterVoiceStorageOptions.SectionName}:RootPath"]
                ?? options.RootPath);
        services.Configure<CharacterAvatarStorageOptions>(options =>
            options.RootPath = configuration[$"{CharacterAvatarStorageOptions.SectionName}:RootPath"]
                ?? options.RootPath);
        services.AddOptions<ThreeWaAiHubOptions>()
            .Bind(configuration.GetSection(ThreeWaAiHubOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "3wa Cluster API base URL is required.")
            .ValidateOnStart();
        services.AddOptions<NarrationOptions>()
            .Bind(configuration.GetSection(NarrationOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.AudioRootPath), "Narration audio root is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Voice), "Narration voice is required.")
            .Validate(options => options.MaxAttempts is >= 1 and <= 10, "Narration attempts must be between 1 and 10.")
            .Validate(options => options.ProviderTimeoutMinutes >= 1, "Narration provider timeout must be positive.")
            .Validate(options => options.LeaseMinutes > options.ProviderTimeoutMinutes, "Narration lease must exceed provider timeout.")
            .ValidateOnStart();
        services.AddOptions<NarrationAdmissionOptions>()
            .Bind(configuration.GetSection(NarrationAdmissionOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<MultiCharacterNarrationOptions>()
            .Bind(configuration.GetSection(MultiCharacterNarrationOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProviderVersion)
                && !string.IsNullOrWhiteSpace(options.CompositionVersion)
                && !string.IsNullOrWhiteSpace(options.FfmpegProfile)
                && options.ChapterPauseMs is >= 0 and <= 5_000,
                "Multi-character narration composition settings are invalid.")
            .ValidateOnStart();
        services.AddOptions<SeriesVoiceCatalogOptions>()
            .Bind(configuration.GetSection(SeriesVoiceCatalogOptions.SectionName))
            .PostConfigure(options =>
            {
                if (options.Voices.Count == 0)
                {
                    options.Voices = SeriesVoiceCatalogOptions.CreateDefaultVoices();
                }
            })
            .Validate(
                options => options.Voices.Count > 0
                    && options.Voices.All(voice =>
                        !string.IsNullOrWhiteSpace(voice.Provider)
                        && !string.IsNullOrWhiteSpace(voice.Voice)
                        && !string.IsNullOrWhiteSpace(voice.DisplayName)
                        && !string.IsNullOrWhiteSpace(voice.Locale))
                    && options.Voices
                        .Select(voice => $"{voice.Provider.Trim().ToUpperInvariant()}\n{voice.Voice.Trim()}")
                        .Distinct(StringComparer.Ordinal)
                        .Count() == options.Voices.Count,
                "Series voice catalog entries must be complete and unique.")
            .ValidateOnStart();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IBookMetadataCorrectionService, BookMetadataCorrectionService>();
        services.AddScoped<IBookInsightsService, BookInsightsService>();
        services.AddScoped<ILibraryStatusService, LibraryStatusService>();
        services.AddScoped<INarrationService, NarrationService>();
        services.AddScoped<ISeriesNarrationService, SeriesNarrationService>();
        services.AddScoped<IStagedNarrationBatchProgressService, StagedNarrationBatchProgressService>();
        services.AddScoped<IStorySeriesRepository, StorySeriesRepository>();
        services.AddScoped<ISeriesService, SeriesService>();
        services.AddScoped<IBookCollectionRepository, BookCollectionRepository>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<ISharedCollectionService, SharedCollectionService>();
        services.AddScoped<PostgreSqlCastEpochActivationPublisher>();
        services.AddScoped<CompanionTokenService>();
        services.AddSingleton<ChineseSpeechSegmenter>();
        services.AddSingleton<ISpeakerAttributionProvider>(
            _ => new LocalSpeakerAttributionProvider(new RuleBasedSpeakerAttributionProvider()));
        services.AddScoped<IChapterSpeechPlanRepository, ChapterSpeechPlanRepository>();
        services.AddScoped<ISpeechPlanService, SpeechPlanService>();
        services.AddSingleton<IBookImportParser, PlainTextBookParser>();
        services.AddSingleton<IBookImportParser, EpubBookParser>();
        services.AddSingleton<IBookFileStorage, LocalBookFileStorage>();
        services.AddSingleton<LocalCharacterVoiceAudioStorage>();
        services.AddSingleton<LocalCharacterAvatarStorage>();
        services.AddScoped<ICharacterProfileService, CharacterProfileService>();
        services.AddScoped<ICharacterVoiceProfileService, CharacterVoiceProfileService>();
        services.AddScoped<ICharacterVoicePreviewService, CharacterVoicePreviewService>();
        services.AddHttpClient<IThreeWaVoiceProfileClient, ThreeWaVoiceProfileClient>((provider, client) =>
        {
            var hubOptions = provider.GetRequiredService<IOptions<ThreeWaAiHubOptions>>().Value;
            client.BaseAddress = new Uri(hubOptions.BaseUrl);
        });
        services.AddHttpClient<IThreeWaSynthesisClient, ThreeWaSynthesisClient>((provider, client) =>
        {
            var hubOptions = provider.GetRequiredService<IOptions<ThreeWaAiHubOptions>>().Value;
            client.BaseAddress = new Uri(hubOptions.BaseUrl);
        });
        return services;
    }
}
