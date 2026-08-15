using Serilog;
using StoryVoice.Infrastructure;
using StoryVoice.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());
builder.Services.AddStoryVoiceInfrastructure(builder.Configuration);
builder.Services.AddOptions<VoAiOptions>()
    .Bind(builder.Configuration.GetSection(VoAiOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
        && baseUri.Scheme == Uri.UriSchemeHttps
        && string.Equals(baseUri.Host, "connect.voai.ai", StringComparison.OrdinalIgnoreCase)
        && baseUri.IsDefaultPort,
        "VoAI base URL must use the official HTTPS origin https://connect.voai.ai.")
    .Validate(options => options.TimeoutSeconds is >= 1 and <= 600,
        "VoAI timeout must be between 1 and 600 seconds.")
    .Validate(options => options.MaximumResponseBytes is >= 1_048_576 and <= 134_217_728,
        "VoAI response limit must be between 1 MiB and 128 MiB.")
    .Validate(options => options.MaximumJobResponseBytes is >= 67_108_864 and <= 8_589_934_592,
        "VoAI aggregate job response limit must be between 64 MiB and 8 GiB.")
    .Validate(options => options.MaximumChunksPerJob is >= 1 and <= 10_000,
        "VoAI chunks per job must be between 1 and 10000.")
    .Validate(options => options.SampleRate == 32_000,
        "VoAI synthesis currently requires a 32000 Hz WAV response.")
    .ValidateOnStart();
builder.Services.AddHttpClient(VoAiTtsClient.HttpClientName, client =>
        client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
    })
    .RedactLoggedHeaders(headerName =>
        string.Equals(headerName, "x-api-key", StringComparison.OrdinalIgnoreCase));
builder.Services.AddSingleton<IVoAiTtsClient, VoAiTtsClient>();
builder.Services.AddSingleton<INarrationProvider, EdgeTtsNarrationProvider>();
builder.Services.AddSingleton<IMultiVoiceNarrationProvider, EdgeTtsMultiVoiceNarrationProvider>();
builder.Services.AddSingleton<IMultiVoiceNarrationProvider, ThreeWaVoxCpm2NarrationProvider>();
builder.Services.AddSingleton<FfmpegVoAiAudioComposer>();
builder.Services.AddSingleton<IVoAiAudioComposer>(services =>
    services.GetRequiredService<FfmpegVoAiAudioComposer>());
builder.Services.AddSingleton<IFfmpegAudioComposer>(services =>
    services.GetRequiredService<FfmpegVoAiAudioComposer>());
builder.Services.AddSingleton<IMultiVoiceNarrationProvider, VoAiMultiVoiceNarrationProvider>();
builder.Services.AddSingleton<IMultiVoiceNarrationProvider, BlueMagpieMultiVoiceNarrationProvider>();
builder.Services.AddSingleton<INarrationProviderRegistry, NarrationProviderRegistry>();
builder.Services.AddSingleton<NarrationProviderDispatcher>();
builder.Services.AddHostedService<StoryPipelineWorker>();

var host = builder.Build();
host.Run();
