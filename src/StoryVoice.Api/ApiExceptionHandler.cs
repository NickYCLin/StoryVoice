using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StoryVoice.Application.BookImports;
using StoryVoice.Application.Insights;
using StoryVoice.Application.Narrations;

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
            BookTextUnavailableException => StatusCodes.Status409Conflict,
            NarrationTextUnavailableException => StatusCodes.Status409Conflict,
            SingleVoiceNarrationRetiredException => StatusCodes.Status409Conflict,
            NarrationAdmissionDisabledException => StatusCodes.Status409Conflict,
            NarrationRightsRequiredException => StatusCodes.Status400BadRequest,
            AntiforgeryValidationException => StatusCodes.Status400BadRequest,
            ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception");
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                StatusCodes.Status400BadRequest => "Request validation failed",
                StatusCodes.Status409Conflict => "Book text is unavailable",
                StatusCodes.Status415UnsupportedMediaType => "Book format is not supported",
                _ => "The service could not complete the request"
            },
            Detail = statusCode < StatusCodes.Status500InternalServerError
                ? exception.Message
                : "StoryVoice 暫時無法完成要求，請稍後再試。"
        };
        if (exception is BookTextUnavailableException)
        {
            problemDetails.Extensions["code"] = BookTextUnavailableException.StableCode;
        }
        else if (exception is NarrationTextUnavailableException)
        {
            problemDetails.Extensions["code"] = NarrationTextUnavailableException.StableCode;
        }
        else if (exception is SingleVoiceNarrationRetiredException)
        {
            problemDetails.Extensions["code"] = SingleVoiceNarrationRetiredException.StableCode;
        }
        else if (exception is NarrationAdmissionDisabledException)
        {
            problemDetails.Extensions["code"] = NarrationAdmissionDisabledException.StableCode;
        }
        else if (exception is NarrationRightsRequiredException)
        {
            problemDetails.Extensions["code"] = NarrationRightsRequiredException.StableCode;
        }

        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
