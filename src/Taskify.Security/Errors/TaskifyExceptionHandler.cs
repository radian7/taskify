using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Taskify.Security.Audit;

namespace Taskify.Security.Errors;

/// <summary>
/// The global exception handler. A malformed or oversized request becomes a generic 400 or 413; anything
/// else becomes a generic 500 (or 503 when a required service is down). The exception itself is logged on the server only and never sent to the
/// caller (constitution Principle I: no internal details in error responses).
/// </summary>
/// <param name="audit">Records the refusal.</param>
/// <param name="logger">Records unexpected failures.</param>
public sealed class TaskifyExceptionHandler(IAuditLogger audit, ILogger<TaskifyExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            // Covers unknown JSON fields, wrong types, bad enum names and bodies over the size limit.
            var status = badRequest.StatusCode is StatusCodes.Status413PayloadTooLarge
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;

            audit.Rejected(AuditOutcomes.FromStatusCode(status));
            await ProblemResponses.WriteAsync(
                httpContext,
                status,
                status == StatusCodes.Status413PayloadTooLarge ? "Request body too large." : "The request could not be read.",
                status == StatusCodes.Status413PayloadTooLarge ? null : "Check the request body against the API contract.");
            return true;
        }

        if (exception is ServiceUnavailableException)
        {
            // A dependency (for example the Projects API) is down: say so generically, keep details in the server log.
            logger.LogWarning(exception, "A required service is unavailable");
            audit.Rejected(AuditOutcome.Error);
            await ProblemResponses.WriteAsync(
                httpContext,
                StatusCodes.Status503ServiceUnavailable,
                "A required service is temporarily unavailable.",
                "Try again in a moment.");
            return true;
        }

        logger.LogError(exception, "Unhandled exception while processing a request");
        audit.Rejected(AuditOutcome.Error);
        await ProblemResponses.WriteAsync(httpContext, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        return true;
    }
}
