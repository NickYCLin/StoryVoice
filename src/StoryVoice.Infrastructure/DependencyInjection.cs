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
using StoryVoice.Infrastructure.Insights;
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
            .Validate(options => options.LeaseMinutes is >= 2 and <= 60,
                "Narration lease must be between 2 and 60 minutes; the Worker renews it while synthesis is active.")
            .ValidateOnStart();
        services.AddOptions<NarrationAdmissionOptions>()
            .Bind(configuration.GetSection(NarrationAdmissionOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<LocalLlmCharacterAnalysisOptions>()
            .Bind(configuration.GetSection(LocalLlmCharacterAnalysisOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "本機 LLM base URL 無效。")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "本機 LLM model 不可空白。")
            .Validate(options => string.Equals(options.Model, "gpt-oss:20b", StringComparison.Ordinal),
                "本機 LLM 角色分析只允許核准的 gpt-oss:20b 模型。")
            .Validate(options => string.Equals(options.ReasoningEffort, "low", StringComparison.OrdinalIgnoreCase),
                "本機 LLM 角色分析只允許 low reasoning，避免完整章節推論超時。")
            .Validate(options => options.NumContext == 16_384,
                "本機 LLM 角色分析只允許已驗證的 16K context。")
            .Validate(options => options.TimeoutSeconds is >= 30 and <= 1_800, "本機 LLM timeout 必須介於 30 至 1800 秒。")
            .Validate(options => options.UnloadTimeoutSeconds is >= 3 and <= 60, "本機 LLM unload timeout 必須介於 3 至 60 秒。")
            .Validate(options => options.MaximumResponseBytes is >= 1_024 and <= 65_536, "本機 LLM response 上限必須介於 1 KiB 至 64 KiB。")
            .ValidateOnStart();
        services.AddOptions<LocalGpuExecutionGateOptions>()
            .Bind(configuration.GetSection(LocalGpuExecutionGateOptions.SectionName))
            .Validate(options => options.LeaseSeconds is >= LocalGpuExecutionGateOptions.MinimumLeaseSeconds and <= 7_200,
                "本機 GPU Redis lease 必須介於 1900 至 7200 秒，以安全涵蓋最長 Ollama 執行與卸載。")
            .Validate(options => options.PollIntervalMilliseconds is >= 25 and <= 2_000,
                "本機 GPU Redis lock poll interval 必須介於 25 至 2000 毫秒。")
            .Validate(options => options.RenewIntervalSeconds is >= 5 and <= 60,
                "本機 GPU Redis lease 必須每 5 至 60 秒續租一次。")
            .ValidateOnStart();
        services.AddOptions<BlueMagpieOptions>()
            .Bind(configuration.GetSection(BlueMagpieOptions.SectionName))
            .Validate(options => !options.FormalNarrationEnabled || options.Enabled,
                "BlueMagpie 正式配音只能在固定句試音 gateway 已啟用時開放。")
            .Validate(options => !options.Enabled || IsValidBlueMagpieBaseUrl(options.BaseUrl),
                "BlueMagpie base URL 無效。")
            .Validate(options => !options.Enabled
                || (!string.IsNullOrWhiteSpace(options.InternalToken)
                    && options.InternalToken.Trim().Length >= 32),
                "啟用 BlueMagpie 時必須設定至少 32 字元的 internal token。")
            .Validate(options => string.Equals(
                    options.ModelRevision,
                    BlueMagpieOptions.PinnedModelRevision,
                    StringComparison.Ordinal),
                "BlueMagpie 只允許已驗證的固定模型版本。")
            .Validate(options => options.ConnectTimeoutSeconds is >= 1 and <= 30,
                "BlueMagpie connect timeout 必須介於 1 至 30 秒。")
            .Validate(options => options.QueueTimeoutSeconds is >= 1 and <= 60,
                "BlueMagpie queue timeout 必須介於 1 至 60 秒。")
            .Validate(options => options.SynthesisWatchdogSeconds is >= 30 and <= 3_600,
                "BlueMagpie synthesis watchdog 必須介於 30 至 3600 秒。")
            .Validate(options => options.ModelLifecycleWatchdogSeconds is >= 30 and <= 3_600,
                "BlueMagpie model lifecycle watchdog 必須介於 30 至 3600 秒。")
            .Validate(options => options.RequestTimeoutSeconds
                    >= options.ConnectTimeoutSeconds
                        + options.QueueTimeoutSeconds
                        + options.SynthesisWatchdogSeconds
                        + 15
                    && options.RequestTimeoutSeconds <= 3_900,
                "BlueMagpie API request timeout 必須涵蓋 connect、queue、synthesis watchdog 與安全餘裕。")
            .Validate(options => options.MaximumResponseBytes is >= 64 * 1024 and <= 16 * 1024 * 1024,
                "BlueMagpie response 上限必須介於 64 KiB 至 16 MiB。")
            .Validate(options => options.MaximumChunksPerJob is >= 1 and <= 10_000,
                "BlueMagpie 單一工作分段上限必須介於 1 至 10000。")
            .Validate(options => options.MaximumJobAudioBytes is >= 64L * 1024 * 1024
                    and <= 8L * 1024 * 1024 * 1024,
                "BlueMagpie 單一工作 PCM 音訊上限必須介於 64 MiB 至 8 GiB。")
            .Validate(options => options.MaximumJobAudioBytes >= options.MaximumResponseBytes,
                "BlueMagpie 單一工作 PCM 音訊上限不得小於單一 response 上限。")
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
        // API production replaces this test-safe default with RedisLocalGpuExecutionGate after
        // registering its shared connection multiplexer.
        services.AddSingleton<ILocalGpuExecutionGate, InProcessLocalGpuExecutionGate>();
        services.AddHttpClient<ILocalLlmCharacterAnalysisProvider, OllamaCharacterAnalysisProvider>((provider, client) =>
        {
            var localLlmOptions = provider.GetRequiredService<IOptions<LocalLlmCharacterAnalysisOptions>>().Value;
            client.BaseAddress = new Uri(localLlmOptions.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(localLlmOptions.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { UseProxy = false });
        services.AddHttpClient<OllamaSpeakerAttributionProvider>((provider, client) =>
        {
            var localLlmOptions = provider.GetRequiredService<IOptions<LocalLlmCharacterAnalysisOptions>>().Value;
            client.BaseAddress = new Uri(localLlmOptions.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(localLlmOptions.TimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { UseProxy = false });
        services.AddHttpClient<IBlueMagpieTtsClient, BlueMagpieTtsClient>((provider, client) =>
        {
            var blueMagpieOptions = provider.GetRequiredService<IOptions<BlueMagpieOptions>>().Value;
            client.BaseAddress = new Uri(blueMagpieOptions.BaseUrl, UriKind.Absolute);
            // The gateway owns the Redis GPU lease independently, so an API timeout cannot
            // release CUDA early. A finite bound keeps a half-open internal connection from
            // poisoning the singleton preview cache until the API process restarts.
            client.Timeout = TimeSpan.FromSeconds(blueMagpieOptions.RequestTimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(
                provider.GetRequiredService<IOptions<BlueMagpieOptions>>().Value.ConnectTimeoutSeconds),
        });
        services.AddScoped<IBookInsightsService, BookInsightsService>();
        services.AddScoped<ILibraryStatusService, LibraryStatusService>();
        services.AddScoped<INarrationService, NarrationService>();
        services.AddScoped<ISeriesNarrationService, SeriesNarrationService>();
        services.AddScoped<IStagedNarrationBatchProgressService, StagedNarrationBatchProgressService>();
        services.AddScoped<IStorySeriesRepository, StorySeriesRepository>();
        services.AddScoped<ISeriesService, SeriesService>();
        // A singleton owns the successful two-voice preview cache for the lifetime of the API process.
        services.AddSingleton<ISeriesVoicePreviewService, SeriesVoicePreviewService>();
        services.AddScoped<IBookCollectionRepository, BookCollectionRepository>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<ISharedCollectionService, SharedCollectionService>();
        services.AddScoped<PostgreSqlCastEpochActivationPublisher>();
        services.AddScoped<CompanionTokenService>();
        services.AddSingleton<ChineseSpeechSegmenter>();
        services.AddSingleton<RuleBasedSpeakerAttributionProvider>();
        services.AddSingleton<ISpeakerAttributionProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<LocalLlmCharacterAnalysisOptions>>().Value;
            var hybrid = new HybridSpeakerAttributionProvider(
                provider.GetRequiredService<RuleBasedSpeakerAttributionProvider>(),
                provider.GetRequiredService<OllamaSpeakerAttributionProvider>());
            return new LocalSpeakerAttributionProvider(
                hybrid,
                TimeSpan.FromSeconds(options.TimeoutSeconds));
        });
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

    private static bool IsValidBlueMagpieBaseUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && string.Equals(uri.Host, "bluemagpie-gateway", StringComparison.OrdinalIgnoreCase)
            && uri.Port == 8081
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
