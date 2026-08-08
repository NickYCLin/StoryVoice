using StoryVoice.Application.Books;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Bookshelves;

namespace StoryVoice.Api;

public static class BookEndpoints
{
    private const long MaximumImportBytes = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/books").WithTags("Books");

        group.MapPost("/", async (
            CreateBookRequest request,
            HttpContext httpContext,
            IBookService service,
            CancellationToken cancellationToken) =>
        {
            var book = await service.CreateAsync(request, cancellationToken);
            return Results.Created(BookLocation(httpContext.Request.PathBase, book.Id), book);
        })
        .WithName("CreateBook");

        group.MapPost("/import", async (
            IFormFile file,
            string? title,
            string? author,
            string? language,
            HttpContext httpContext,
            IBookImportService importService,
            CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                throw new ArgumentException("上傳檔案不可為空白。", nameof(file));
            }

            if (file.Length > MaximumImportBytes)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Book file is too large",
                    detail: "單一書籍檔案不可超過 10 MiB。");
            }

            await using var content = file.OpenReadStream();
            var book = await importService.ImportAsync(
                content,
                Path.GetFileName(file.FileName),
                title,
                author,
                language,
                cancellationToken);
            return Results.Created(BookLocation(httpContext.Request.PathBase, book.Id), book);
        })
        .DisableAntiforgery()
        .WithName("ImportBook")
        .Produces<BookDetailsResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/sources/books-com-tw/import", async (
            BooksComTwBookshelfImportRequest request,
            IBooksComTwBookshelfService bookshelfService,
            CancellationToken cancellationToken) =>
            Results.Ok(await bookshelfService.ImportAsync(request, cancellationToken)))
            .WithName("ImportBooksComTwBookshelf")
            .Produces<BooksComTwBookshelfImportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

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

    private static string BookLocation(PathString pathBase, Guid bookId) =>
        $"{pathBase}/api/books/{bookId}";
}
