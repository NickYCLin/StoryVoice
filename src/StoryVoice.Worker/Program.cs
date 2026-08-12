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
builder.Services.AddSingleton<INarrationProvider, EdgeTtsNarrationProvider>();
builder.Services.AddSingleton<IMultiVoiceNarrationProvider, EdgeTtsMultiVoiceNarrationProvider>();
builder.Services.AddSingleton<IMultiVoiceNarrationProvider, ThreeWaVoxCpm2NarrationProvider>();
builder.Services.AddSingleton<INarrationProviderRegistry, NarrationProviderRegistry>();
builder.Services.AddSingleton<NarrationProviderDispatcher>();
builder.Services.AddHostedService<StoryPipelineWorker>();

var host = builder.Build();
host.Run();
