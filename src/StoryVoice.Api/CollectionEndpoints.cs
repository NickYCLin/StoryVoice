using StoryVoice.Application.Collections;

namespace StoryVoice.Api;

public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/collections")
            .WithTags("Collections")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/", async (
            ICollectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListBookCollections");

        group.MapGet("/shared-with-me", async (
            ISharedCollectionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListSharedWithMeAsync(cancellationToken)))
            .WithName("ListSharedWithMeCollections");

        group.MapGet("/shared-with-me/{collectionId:guid}", async (
            Guid collectionId,
            ISharedCollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.GetSharedAsync(collectionId, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .WithName("GetSharedCollection");

        group.MapGet("/shared-with-me/{collectionId:guid}/books/{bookId:guid}", async (
            Guid collectionId,
            Guid bookId,
            ISharedCollectionService service,
            CancellationToken cancellationToken) =>
        {
            var content = await service.GetSharedBookContentAsync(collectionId, bookId, cancellationToken);
            return content is null ? Results.NotFound() : Results.Ok(content);
        })
        .WithName("GetSharedCollectionBookContent");

        group.MapGet("/{collectionId:guid}", async (
            Guid collectionId,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.GetAsync(collectionId, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .WithName("GetBookCollection");

        group.MapPost("/", async (
            CreateBookCollectionRequest request,
            HttpContext httpContext,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.CreateAsync(request, cancellationToken);
            return Results.Created(
                $"{httpContext.Request.PathBase}/api/collections/{collection.Id}",
                collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("CreateBookCollection");

        group.MapPut("/{collectionId:guid}", async (
            Guid collectionId,
            UpdateBookCollectionRequest request,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.UpdateAsync(collectionId, request, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("UpdateBookCollection");

        group.MapDelete("/{collectionId:guid}", async (
            Guid collectionId,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var deleted = await service.DeleteAsync(collectionId, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("DeleteBookCollection");

        group.MapPost("/{collectionId:guid}/books", async (
            Guid collectionId,
            AddCollectionBookRequest request,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.AddBookAsync(collectionId, request, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("AddBookCollectionBook");

        group.MapPut("/{collectionId:guid}/books/{bookId:guid}", async (
            Guid collectionId,
            Guid bookId,
            UpdateCollectionBookRequest request,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.UpdateBookAsync(collectionId, bookId, request, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("UpdateBookCollectionBook");

        group.MapDelete("/{collectionId:guid}/books/{bookId:guid}", async (
            Guid collectionId,
            Guid bookId,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.RemoveBookAsync(collectionId, bookId, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("RemoveBookCollectionBook");

        group.MapPost("/{collectionId:guid}/shares", async (
            Guid collectionId,
            AddCollectionShareRequest request,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.AddShareAsync(collectionId, request, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("AddBookCollectionShare");

        group.MapDelete("/{collectionId:guid}/shares/{shareId:guid}", async (
            Guid collectionId,
            Guid shareId,
            ICollectionService service,
            CancellationToken cancellationToken) =>
        {
            var collection = await service.RevokeShareAsync(collectionId, shareId, cancellationToken);
            return collection is null ? Results.NotFound() : Results.Ok(collection);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>()
        .WithName("RevokeBookCollectionShare");

        return endpoints;
    }
}
