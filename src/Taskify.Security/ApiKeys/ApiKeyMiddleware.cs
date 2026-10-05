using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Taskify.Security.Audit;
using Taskify.Security.Errors;

namespace Taskify.Security.ApiKeys;

/// <summary>
/// Authenticates the calling service from <c>X-Api-Key</c> and enforces the research R8 key matrix
/// (deviation D2: per-caller API keys instead of OAuth 2.0 client credentials).
/// <list type="bullet">
/// <item>A missing, repeated or unknown key gets <c>401</c>.</item>
/// <item>A known caller on a route that does not list it in <see cref="AllowCallersAttribute"/> gets <c>403</c>.</item>
/// </list>
/// </summary>
/// <param name="next">The next middleware.</param>
/// <param name="options">The keys this service accepts.</param>
/// <param name="audit">Records refusals.</param>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options, IAuditLogger audit)
{
    /// <summary>Checks whether a path is a health endpoint, which needs no key (the only exemption).</summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true"/> for <c>/health</c> and <c>/alive</c>.</returns>
    public static bool IsHealthPath(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase) || path.Equals("/alive", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the response or the rest of the pipeline has finished.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsHealthPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var caller = Identify(context.Request.Headers[TaskifyHeaders.ApiKey]);
        if (caller is null)
        {
            audit.Rejected(AuditOutcome.Unauthorized);
            await ProblemResponses.WriteAsync(context, StatusCodes.Status401Unauthorized, "Missing or invalid API key.");
            return;
        }

        // The caller is known; record it before any further refusal so the audit entry names it.
        context.SetCaller(caller);

        var endpoint = context.GetEndpoint();
        if (endpoint is not null)
        {
            // Fail closed: a route that lists no callers is closed to everyone.
            var allowed = endpoint.Metadata.GetMetadata<AllowCallersAttribute>();
            if (allowed is null || !allowed.Callers.Contains(caller, StringComparer.Ordinal))
            {
                audit.Rejected(AuditOutcome.Forbidden);
                await ProblemResponses.WriteAsync(context, StatusCodes.Status403Forbidden, "This caller is not allowed on this route.");
                return;
            }
        }

        await next(context);
    }

    /// <summary>Finds which configured caller owns the presented key, comparing in constant time.</summary>
    private string? Identify(Microsoft.Extensions.Primitives.StringValues header)
    {
        // Exactly one header value; a repeated header is ambiguous and refused.
        if (header.Count != 1 || string.IsNullOrEmpty(header[0]))
        {
            return null;
        }

        var presented = Encoding.UTF8.GetBytes(header[0]!);
        string? match = null;

        // Compare against every configured key without stopping early, so timing does not reveal
        // which keys exist (security: constant-time comparison per key).
        foreach (var (name, key) in options.Value.Accepted)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(presented, Encoding.UTF8.GetBytes(key)))
            {
                match = name;
            }
        }

        return match;
    }
}
