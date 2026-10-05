using System.Security.Claims;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.Users;

namespace Taskify.Web.Services;

/// <summary>The claim types the Web app puts on the request principal.</summary>
public static class TaskifyClaims
{
    /// <summary>The validated selected user's ID. Absent when nobody is selected (or the cookie was rejected).</summary>
    public const string UserId = ClaimTypes.NameIdentifier;

    /// <summary>
    /// The browser's source IP, for audit logs (spec FR-032). It travels on the principal because a Blazor circuit
    /// has no HTTP request of its own; the principal is captured when the circuit connects.
    /// </summary>
    public const string ClientIp = "taskify:client_ip";

    /// <summary>The authentication type of the principal. It does not mean a login happened (deviation D1).</summary>
    public const string AuthenticationType = "TaskifySelectedUser";
}

/// <summary>
/// Reads the selected-user cookie on every page request and puts the result, together with the source IP, on the
/// request principal, where Blazor Server picks it up when a circuit starts (research R8).
/// <list type="bullet">
/// <item>No cookie: nobody is selected.</item>
/// <item>A cookie that fails Data Protection, has expired, or names a GUID that is not a predefined user is
/// <b>rejected</b>: no selection, the cookie is deleted, and the rejection is audited (constitution Principle II).</item>
/// </list>
/// </summary>
/// <param name="next">The next middleware.</param>
/// <param name="cookie">Reads and validates the cookie.</param>
/// <param name="audit">Records rejections.</param>
public sealed class SelectedUserMiddleware(RequestDelegate next, SelectedUserCookie cookie, IAuditLogger audit)
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="users">The directory of predefined users.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context, IUserDirectory users)
    {
        if (ApiKeyMiddleware.IsHealthPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();
        var claims = new List<Claim>();
        if (ip is not null)
        {
            claims.Add(new Claim(TaskifyClaims.ClientIp, ip));
        }

        if (context.Request.Cookies.TryGetValue(SelectedUserCookie.CookieName, out var raw))
        {
            var userId = cookie.TryUnprotect(raw);

            bool known;
            try
            {
                known = userId is { } id && await users.ExistsAsync(id, context.RequestAborted);
            }
            catch (ServiceUnavailableException)
            {
                // The directory cannot be reached, so the cookie cannot be checked. Treat this request as "nobody
                // selected" but keep the cookie: it may be perfectly valid once the Projects API is back.
                known = false;
                userId = null;
                raw = null;
            }

            if (known)
            {
                claims.Add(new Claim(TaskifyClaims.UserId, userId!.Value.ToString("D")));
            }
            else if (raw is not null)
            {
                // Tampered, expired or unknown: refuse it, say so in the audit log, and remove it.
                audit.Log(new AuditEntry(AuditActions.RequestRejected, AuditOutcome.Validation)
                {
                    EntityType = "SelectedUserCookie",
                    SourceIp = ip,
                });
                context.Response.Cookies.Delete(SelectedUserCookie.CookieName, SelectedUserCookie.Options);
            }
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, TaskifyClaims.AuthenticationType));
        await next(context);
    }
}
