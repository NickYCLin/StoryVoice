using StoryVoice.Application.ExternalVoices;

namespace StoryVoice.Api;

public static class DeveloperConsoleEndpoints
{
    public static IEndpointRouteBuilder MapDeveloperConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/developer")
            .WithTags("Developer console")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/external-voice/overview", async (
            HttpContext httpContext,
            IDeveloperVoiceConsoleService service,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var overview = await service.GetOverviewAsync(cancellationToken);
            return Results.Ok(overview);
        });

        return endpoints;
    }
}
