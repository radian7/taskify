namespace Taskify.Web.Services;

/// <summary>
/// Adds the browser security headers to every response (research R9; constitution Principle I). The policy allows
/// only same-origin scripts, styles and form posts, and forbids framing the app. It is a second line of defense
/// behind output encoding: even if user text were ever rendered as markup, an injected script would not run.
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The Content Security Policy. There are no inline scripts or styles anywhere in the app, so none are allowed.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        return next(context);
    }
}
