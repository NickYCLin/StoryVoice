using StoryVoice.Application.Library;

namespace StoryVoice.Api;

public static class LibraryStatusEndpoints
{
    public static IEndpointRouteBuilder MapLibraryStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/library/status-matrix/",
                async (ILibraryStatusService service, CancellationToken cancellationToken) =>
                    Results.Ok(await service.ListAsync(cancellationToken)))
            .RequireAuthorization();
        return endpoints;
    }
}
