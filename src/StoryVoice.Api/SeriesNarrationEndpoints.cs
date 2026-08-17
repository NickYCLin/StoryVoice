using StoryVoice.Application.Narrations;

namespace StoryVoice.Api;

public static class SeriesNarrationEndpoints
{
    public static IEndpointRouteBuilder MapSeriesNarrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/series/{seriesId:guid}/narration-rebuilds")
            .WithTags("SeriesNarration")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapPost("/", async (
            Guid seriesId,
            CreateSeriesNarrationRebuildRequest request,
            HttpContext httpContext,
            ISeriesNarrationService service,
            CancellationToken cancellationToken) =>
        {
            var batch = await service.CreateRebuildAsync(seriesId, request, cancellationToken);
            return batch is null
                ? Results.NotFound()
                : Results.Created(
                    $"{httpContext.Request.PathBase}/api/series/{seriesId}/narration-rebuilds/{batch.Id}",
                    batch);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("CreateSeriesNarrationRebuild")
        .Produces<SeriesNarrationRebuildResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{batchId:guid}", async (
            Guid seriesId,
            Guid batchId,
            ISeriesNarrationService service,
            CancellationToken cancellationToken) =>
        {
            var batch = await service.GetRebuildAsync(seriesId, batchId, cancellationToken);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        })
        .WithName("GetSeriesNarrationRebuild")
        .Produces<SeriesNarrationRebuildResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{batchId:guid}/discard", async (
            Guid seriesId,
            Guid batchId,
            ISeriesNarrationService service,
            CancellationToken cancellationToken) =>
        {
            var batch = await service.DiscardRebuildAsync(seriesId, batchId, cancellationToken);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("DiscardSeriesNarrationRebuild")
        .Produces<SeriesNarrationRebuildResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{batchId:guid}/activate", async (
            Guid seriesId,
            Guid batchId,
            ISeriesNarrationService service,
            CancellationToken cancellationToken) =>
        {
            var batch = await service.ActivateRebuildAsync(seriesId, batchId, cancellationToken);
            return batch is null ? Results.NotFound() : Results.Ok(batch);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("ActivateSeriesNarrationRebuild")
        .Produces<SeriesNarrationRebuildResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
