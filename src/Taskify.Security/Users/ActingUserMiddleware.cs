using Microsoft.AspNetCore.Http;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;

namespace Taskify.Security.Users;

/// <summary>
/// Validates the <c>X-Taskify-User</c> header: exactly one value, a non-empty GUID, and one of the five
/// predefined users. Anything else gets <c>400</c> and changes nothing (spec FR-021; contracts/*-api.yaml).
/// Routes marked <see cref="SkipActingUserAttribute"/> are exempt.
/// </summary>
/// <remarks>
/// Phase 1 has no login, so this header is a client-supplied claim, not proof of identity. That is the
/// accepted risk D1 in the plan; every request is audited with the acting user and source IP.
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="audit">Records refusals.</param>
public sealed class ActingUserMiddleware(RequestDelegate next, IAuditLogger audit)
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="users">The directory of predefined users.</param>
    /// <returns>A task that completes when the response or the rest of the pipeline has finished.</returns>
    public async Task InvokeAsync(HttpContext context, IUserDirectory users)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null
            || ApiKeyMiddleware.IsHealthPath(context.Request.Path)
            || endpoint.Metadata.GetMetadata<SkipActingUserAttribute>() is not null)
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers[TaskifyHeaders.ActingUser];
        if (header.Count == 1
            && Guid.TryParse(header[0], out var userId)
            && userId != Guid.Empty
            && await users.ExistsAsync(userId, context.RequestAborted))
        {
            context.SetActingUserId(userId);
            await next(context);
            return;
        }

        // Do not say whether the header was missing, malformed or unknown: one generic message.
        audit.Rejected(AuditOutcome.Validation);
        await ProblemResponses.WriteAsync(
            context,
            StatusCodes.Status400BadRequest,
            "Missing or invalid acting user.",
            $"Send the ID of one of the predefined users in the {TaskifyHeaders.ActingUser} header.");
    }
}
