using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StoryVoice.Application.BookImports;

namespace StoryVoice.Api;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            UnsupportedBookFormatException => StatusCodes.Status415UnsupportedMediaType,
            AntiforgeryValidationException => StatusCodes.Status400BadRequest,
            ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception");
        }

        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = statusCode switch
                {
                    StatusCodes.Status400BadRequest => "Request validation failed",
                    StatusCodes.Status415UnsupportedMediaType => "Book format is not supported",
                    _ => "The service could not complete the request"
                },
                Detail = statusCode < StatusCodes.Status500InternalServerError
                    ? exception.Message
                    : "StoryVoice 暫時無法完成要求，請稍後再試。"
            },
            Exception = exception
        });
    }
}
