using StoryVoice.Application.Insights;

namespace StoryVoice.Api;

public static class BookInsightEndpoints
{
    public static IEndpointRouteBuilder MapBookInsightEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/books/{bookId:guid}")
            .WithTags("Book insights")
            .RequireAuthorization(StoryVoicePolicies.UserSession);

        group.MapGet("/content-link", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var link = await service.GetContentLinkAsync(bookId, cancellationToken);
            return link is null ? Results.NoContent() : Results.Ok(link);
        });

        group.MapPut("/content-link", async (
            Guid bookId,
            SetBookContentLinkRequest request,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var link = await service.SetContentLinkAsync(bookId, request.ContentBookId, cancellationToken);
            return link is null ? Results.NotFound() : Results.Ok(link);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapDelete("/content-link", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
            await service.RemoveContentLinkAsync(bookId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/summary", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var summary = await service.GetSummaryAsync(bookId, cancellationToken);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        });

        group.MapPut("/summary", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var summary = await service.GenerateSummaryAsync(bookId, cancellationToken);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/character-analysis", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var analysis = await service.GetCharacterAnalysisAsync(bookId, cancellationToken);
            return analysis is null ? Results.NotFound() : Results.Ok(analysis);
        });

        group.MapPut("/character-analysis", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var analysis = await service.GenerateCharacterAnalysisAsync(bookId, cancellationToken);
            return analysis is null ? Results.NotFound() : Results.Ok(analysis);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/notes", async (
            Guid bookId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var notes = await service.ListNotesAsync(bookId, cancellationToken);
            return notes is null ? Results.NotFound() : Results.Ok(notes);
        });

        group.MapPost("/notes", async (
            Guid bookId,
            CreateReadingNoteRequest request,
            HttpContext httpContext,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var note = await service.CreateNoteAsync(bookId, request, cancellationToken);
            return note is null
                ? Results.NotFound()
                : Results.Created($"{httpContext.Request.PathBase}/api/books/{bookId}/notes/{note.Id}", note);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapPut("/notes/{noteId:guid}", async (
            Guid bookId,
            Guid noteId,
            UpdateReadingNoteRequest request,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
        {
            var note = await service.UpdateNoteAsync(bookId, noteId, request, cancellationToken);
            return note is null ? Results.NotFound() : Results.Ok(note);
        })
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapDelete("/notes/{noteId:guid}", async (
            Guid bookId,
            Guid noteId,
            IBookInsightsService service,
            CancellationToken cancellationToken) =>
            await service.DeleteNoteAsync(bookId, noteId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
        .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }
}
