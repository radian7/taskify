using System.Net;
using Microsoft.AspNetCore.Http;
using Taskify.Security.ApiKeys;

namespace Taskify.Security.Audit;

/// <summary>Gives the end user's source IP for auditing (spec FR-032, research R8).</summary>
public interface IClientIpAccessor
{
    /// <summary>Gets the IP address text, or <see langword="null"/> when unknown.</summary>
    string? ClientIp { get; }
}

/// <summary>Reads the source IP that <see cref="ClientIpMiddleware"/> recorded for the current request.</summary>
/// <param name="accessor">Gives access to the current request.</param>
public sealed class HttpClientIpAccessor(IHttpContextAccessor accessor) : IClientIpAccessor
{
    /// <inheritdoc />
    public string? ClientIp => accessor.HttpContext?.GetClientIp();
}

/// <summary>
/// Works out the source IP for audit logs. The Web app forwards the end user's address in
/// <c>X-Taskify-Client-Ip</c>; that header is trusted <b>only</b> from the Web caller and only when it parses
/// as an IP address. From any other caller it is ignored and the connection's own address is used.
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class ClientIpMiddleware(RequestDelegate next)
{
    // The longest textual IPv6 address with a scope id stays well below this.
    private const int MaxHeaderLength = 64;

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        string? ip = context.Connection.RemoteIpAddress?.ToString();

        if (context.GetCaller() == Callers.Web
            && context.Request.Headers.TryGetValue(TaskifyHeaders.ClientIp, out var header)
            && header.Count == 1
            && header[0] is { Length: > 0 and <= MaxHeaderLength } text
            && IPAddress.TryParse(text, out var parsed))
        {
            // Re-format the parsed address so nothing but a valid IP can reach the logs.
            ip = parsed.ToString();
        }

        context.SetClientIp(ip);
        return next(context);
    }
}
