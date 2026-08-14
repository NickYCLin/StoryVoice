using StoryVoice.Application.Series;

namespace StoryVoice.Api;

public static class SeriesEndpoints
{
    public static IEndpointRouteBuilder MapSeriesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/series")
            .WithTags("Series")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/", async (
            ISeriesService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListStorySeries");

        group.MapGet("/voice-options", (ISeriesService service) =>
            Results.Ok(service.ListVoiceOptions()))
            .WithName("ListSeriesVoiceOptions");

        group.MapGet("/{seriesId:guid}", async (
            Guid seriesId,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.GetAsync(seriesId, cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .WithName("GetStorySeries");

        group.MapPost("/", async (
            CreateStorySeriesRequest request,
            HttpContext httpContext,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.CreateAsync(request, cancellationToken);
            return Results.Created(
                $"{httpContext.Request.PathBase}/api/series/{series.Id}",
                series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("CreateStorySeries");

        group.MapPost("/{seriesId:guid}/books", async (
            Guid seriesId,
            AddSeriesBookRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.AddBookAsync(seriesId, request, cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("AddStorySeriesBook");

        group.MapPost("/{seriesId:guid}/characters", async (
            Guid seriesId,
            AddSeriesCharacterRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.AddCharacterAsync(seriesId, request, cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("AddStorySeriesCharacter");

        group.MapPut("/{seriesId:guid}/characters/{characterId:guid}", async (
            Guid seriesId,
            Guid characterId,
            UpdateSeriesCharacterRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.UpdateCharacterAsync(
                seriesId,
                characterId,
                request,
                cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("UpdateStorySeriesCharacter");

        group.MapPut("/{seriesId:guid}/point-of-view-character", async (
            Guid seriesId,
            SetSeriesPointOfViewCharacterRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.SetPointOfViewCharacterAsync(seriesId, request, cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("SetStorySeriesPointOfViewCharacter");

        group.MapPut("/{seriesId:guid}/narrative-voice", async (
            Guid seriesId,
            ConfigureSeriesNarrativeVoiceRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.ConfigureNarrativeVoiceAsync(
                seriesId,
                request,
                cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("ConfigureStorySeriesNarrativeVoice");

        group.MapPost("/{seriesId:guid}/characters/{characterId:guid}/aliases", async (
            Guid seriesId,
            Guid characterId,
            AddSeriesCharacterAliasRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.AddAliasAsync(
                seriesId,
                characterId,
                request,
                cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("AddStorySeriesCharacterAlias");

        group.MapPost("/{seriesId:guid}/analyzed-characters", async (
            Guid seriesId,
            ApplyAnalyzedSeriesCharactersRequest request,
            ISeriesService service,
            CancellationToken cancellationToken) =>
        {
            var series = await service.ApplyAnalyzedCharactersAsync(
                seriesId,
                request,
                cancellationToken);
            return series is null ? Results.NotFound() : Results.Ok(series);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("ApplyAnalyzedStorySeriesCharacters");

        return endpoints;
    }
}
