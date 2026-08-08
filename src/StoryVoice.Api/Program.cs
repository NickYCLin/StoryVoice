using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;
using StoryVoice.Api;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Books;
using StoryVoice.Infrastructure;
using StoryVoice.Infrastructure.Health;
using StoryVoice.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IBookImportService, BookImportService>();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024 + 64 * 1024);
builder.Services.AddStoryVoiceInfrastructure(builder.Configuration);

var healthChecks = builder.Services.AddHealthChecks()
    .AddDbContextCheck<StoryVoiceDbContext>("postgres", tags: ["ready"]);

if (!builder.Environment.IsEnvironment("Testing"))
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(redisConnection));
    healthChecks.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
}

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<StoryVoiceDbContext>();
    await database.Database.MigrateAsync();
}

app.MapGet("/", () => Results.Ok(new
{
    name = "StoryVoice",
    tagline = "Turn books into performances.",
    status = "foundation"
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static async (context, _) =>
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("healthy");
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapBookEndpoints();
app.Run();

public partial class Program;
