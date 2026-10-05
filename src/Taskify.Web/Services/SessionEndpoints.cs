using Microsoft.AspNetCore.Mvc;
using Taskify.Security.Audit;
using Taskify.Security.Users;

namespace Taskify.Web.Services;

/// <summary>
/// The endpoint that records which predefined user a browser acts as (spec FR-002). It is a plain form post, not a
/// SignalR call, because only an HTTP response can set an <c>HttpOnly</c> cookie.
/// </summary>
public static class SessionEndpoints
{
    /// <summary>Maps <c>POST /session/select</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        // Antiforgery is validated automatically for form-bound endpoints (app.UseAntiforgery()).
        app.MapPost("/session/select", SelectAsync)
            .WithName("selectUser");

        return app;
    }

    /// <summary>
    /// Records the chosen user: validates it, sets the protected cookie, audits the choice (with the previous user and
    /// the source IP, spec FR-032) and redirects. An empty or unknown user is rejected, audited and sets no cookie.
    /// </summary>
    /// <param name="userId">The chosen user, from the form.</param>
    /// <param name="context">The request context.</param>
    /// <param name="users">The directory of predefined users.</param>
    /// <param name="cookie">Creates the protected cookie value.</param>
    /// <param name="audit">Records the choice or its rejection.</param>
    /// <returns>A redirect to the project list, or back to the selection screen when the choice was rejected.</returns>
    public static async Task<IResult> SelectAsync(
        [FromForm] Guid userId,
        HttpContext context,
        IUserDirectory users,
        SelectedUserCookie cookie,
        IAuditLogger audit)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();

        // The previous selection (if any) comes from the principal that the middleware validated.
        Guid? previousUserId = Guid.TryParse(context.User.FindFirst(TaskifyClaims.UserId)?.Value, out var previous) ? previous : null;

        // Server-side validation: only one of the five predefined users can be chosen (spec FR-021).
        if (userId == Guid.Empty || !await users.ExistsAsync(userId, context.RequestAborted))
        {
            audit.Log(new AuditEntry(AuditActions.RequestRejected, AuditOutcome.Validation)
            {
                EntityType = "SelectUser",
                PreviousUserId = previousUserId,
                SourceIp = ip,
            });
            return Results.Redirect("/", permanent: false);
        }

        context.Response.Cookies.Append(SelectedUserCookie.CookieName, cookie.Protect(userId), SelectedUserCookie.Options);

        audit.Log(new AuditEntry(AuditActions.UserSelected, AuditOutcome.Succeeded)
        {
            ActingUserId = userId,
            PreviousUserId = previousUserId,
            SourceIp = ip,
        });

        return Results.Redirect("/projects", permanent: false);
    }
}
