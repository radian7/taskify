using Microsoft.AspNetCore.Http;

namespace Taskify.Security.Audit;

/// <summary>
/// Audits the refusals that no other component audited (spec FR-022, research R13): a body over the size limit (413),
/// a wrong method (405) and an unsupported media type (415). The framework answers these itself (minimal-API body
/// binding sets a bare status, and <c>UseStatusCodePages</c> adds the body), so neither the exception handler, a
/// validation filter nor the rate limiter sees them. It never changes the status code or the body.
/// </summary>
/// <remarks>
/// A refusal that was already audited is skipped: <see cref="AuditLogger"/> marks the request, so nothing is audited twice.
/// The audit line carries only identifiers, never the body or any user text.
/// </remarks>
/// <param name="next">The next middleware.</param>
/// <param name="audit">The audit logger.</param>
public sealed class UnauditedRejectionMiddleware(RequestDelegate next, IAuditLogger audit)
{
    /// <summary>The <see cref="HttpContext.Items"/> key that <see cref="AuditLogger"/> sets once a rejection is audited.</summary>
    internal const string AuditedKey = "taskify.rejection-audited";

    /// <summary>Runs the rest of the pipeline, then audits an unaudited 405, 413 or 415.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the request is processed.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await next(context);

        var status = context.Response.StatusCode;
        if (status is StatusCodes.Status405MethodNotAllowed or StatusCodes.Status413PayloadTooLarge or StatusCodes.Status415UnsupportedMediaType
            && !context.Items.ContainsKey(AuditedKey))
        {
            // 405 and 415 are malformed requests (Validation); 413 maps to TooLarge.
            audit.Rejected(AuditOutcomes.FromStatusCode(status));
        }
    }
}
