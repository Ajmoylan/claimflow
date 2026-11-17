using ClaimFlow.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 9457 ProblemDetails responses in one place.
/// Expected business failures get a specific status and a safe message.
/// Anything unexpected becomes a generic 500: no stack traces or SQL ever reach the client.
/// </summary>
public sealed partial class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem = exception switch
        {
            DomainValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = $"{notFound.Resource} not found",
                Detail = notFound.Message,
            },
            InvalidStatusTransitionException transition => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Status change not allowed",
                Detail = transition.Message,
                Extensions = { ["currentStatus"] = transition.From.ToString(), ["requestedStatus"] = transition.To.ToString() },
            },
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The claim was changed by someone else",
                Detail = "Reload the claim and try again.",
            },
            DemoLimitReachedException limit => new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Demo limit reached",
                Detail = limit.Message,
            },
            BadHttpRequestException badRequest => new ProblemDetails
            {
                // e.g. 413 when the body exceeds Kestrel's MaxRequestBodySize.
                Status = badRequest.StatusCode,
                Title = ReasonPhrases.GetReasonPhrase(badRequest.StatusCode),
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
            },
        };

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
