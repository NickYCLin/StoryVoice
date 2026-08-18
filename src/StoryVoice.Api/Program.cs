using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;
using StoryVoice.Api;
using StoryVoice.Application.Authentication;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Books;
using StoryVoice.Application.Bookshelves;
using StoryVoice.Application.Narrations;
using StoryVoice.Infrastructure;
using StoryVoice.Infrastructure.Health;
using StoryVoice.Infrastructure.Identity;
using StoryVoice.Infrastructure.Insights;
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
builder.Services.AddScoped<IBooksComTwBookshelfService, BooksComTwBookshelfService>();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = CharacterVoiceProfileLimits.MaximumReferenceAudioBytes + 64 * 1024);
builder.Services.AddStoryVoiceInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "StoryVoice.Antiforgery";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<StoryVoiceDbContext>()
    .AddSignInManager();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "StoryVoice.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, CompanionAuthenticationHandler>(
        CompanionAuthenticationDefaults.Scheme,
        _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(StoryVoicePolicies.UserSession, policy =>
    {
        policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme);
        policy.RequireAuthenticatedUser();
    })
    .AddPolicy(StoryVoicePolicies.BookshelfSync, policy =>
    {
        policy.AddAuthenticationSchemes(CompanionAuthenticationDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            CompanionAuthenticationDefaults.ScopeClaim,
            CompanionAuthenticationDefaults.BookshelfSyncScope);
    });
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
var dataProtectionKeysPath = builder.Configuration["Auth:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("StoryVoice")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

var healthChecks = builder.Services.AddHealthChecks()
    .AddDbContextCheck<StoryVoiceDbContext>("postgres", tags: ["ready"]);

if (!builder.Environment.IsEnvironment("Testing"))
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(redisConnection));
    builder.Services.RemoveAll<ILocalGpuExecutionGate>();
    builder.Services.AddSingleton<ILocalGpuExecutionGate, RedisLocalGpuExecutionGate>();
    healthChecks.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
}

var app = builder.Build();

app.UseForwardedHeaders();

var configuredPathBase = builder.Configuration["ReverseProxy:PathBase"]?.TrimEnd('/');
if (!string.IsNullOrWhiteSpace(configuredPathBase))
{
    app.Use((context, next) =>
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-Prefix", out var forwardedPrefix)
            && string.Equals(forwardedPrefix.ToString(), configuredPathBase, StringComparison.Ordinal))
        {
            context.Request.PathBase = new PathString(configuredPathBase);
        }

        return next(context);
    });
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

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

app.MapAccountEndpoints();
app.MapBookEndpoints();
app.MapBookInsightEndpoints();
app.MapNarrationEndpoints();
app.MapLibraryStatusEndpoints();
app.MapSeriesEndpoints();
app.MapSeriesNarrationEndpoints();
app.MapCollectionEndpoints();
app.MapSpeechPlanEndpoints();
app.MapCharacterProfileEndpoints();
app.MapCharacterVoiceProfileEndpoints();
app.Run();

public partial class Program;
