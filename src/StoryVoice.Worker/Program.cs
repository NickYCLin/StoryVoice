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
builder.Services.AddHostedService<StoryPipelineWorker>();

var host = builder.Build();
host.Run();
