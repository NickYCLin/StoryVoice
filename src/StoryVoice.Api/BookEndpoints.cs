using StoryVoice.Application.Books;

namespace StoryVoice.Api;

public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/books").WithTags("Books");

        group.MapPost("/", async (
            CreateBookRequest request,
            IBookService service,
            CancellationToken cancellationToken) =>
        {
            var book = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/books/{book.Id}", book);
        })
        .WithName("CreateBook");

        group.MapGet("/", async (IBookService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListBooks");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IBookService service,
            CancellationToken cancellationToken) =>
        {
            var book = await service.GetAsync(id, cancellationToken);
            return book is null ? Results.NotFound() : Results.Ok(book);
        })
        .WithName("GetBook");

        return endpoints;
    }
}
