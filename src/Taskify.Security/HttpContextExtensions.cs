using Microsoft.AspNetCore.Http;

namespace Taskify.Security;

/// <summary>
/// Typed access to the per-request values set by the Taskify security middleware
/// (<see cref="ApiKeys.ApiKeyMiddleware"/>, <see cref="Audit.ClientIpMiddleware"/>, <see cref="Users.ActingUserMiddleware"/>).
/// </summary>
public static class HttpContextExtensions
{
    private const string CallerKey = "Taskify.Caller";
    private const string ActingUserKey = "Taskify.ActingUser";
    private const string ClientIpKey = "Taskify.ClientIp";

    /// <summary>Gets the name of the calling service identified by its API key, or <see langword="null"/>.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A name from <see cref="ApiKeys.Callers"/>, or <see langword="null"/> before authentication.</returns>
    public static string? GetCaller(this HttpContext context) =>
        context.Items.TryGetValue(CallerKey, out var value) ? value as string : null;

    /// <summary>Records the authenticated caller. Only the security middleware should call this.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="caller">The caller name.</param>
    internal static void SetCaller(this HttpContext context, string caller) => context.Items[CallerKey] = caller;

    /// <summary>Gets the validated acting user, or <see langword="null"/> when the route is exempt or not yet validated.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>The acting user ID, or <see langword="null"/>.</returns>
    public static Guid? GetActingUserId(this HttpContext context) =>
        context.Items.TryGetValue(ActingUserKey, out var value) && value is Guid id ? id : null;

    /// <summary>Records the validated acting user. Only the security middleware should call this.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="userId">The acting user ID.</param>
    internal static void SetActingUserId(this HttpContext context, Guid userId) => context.Items[ActingUserKey] = userId;

    /// <summary>Gets the end user's source IP for auditing (research R8), or <see langword="null"/>.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>The IP address text, or <see langword="null"/>.</returns>
    public static string? GetClientIp(this HttpContext context) =>
        context.Items.TryGetValue(ClientIpKey, out var value) ? value as string : null;

    /// <summary>Records the source IP. Only the security middleware should call this.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="ip">The IP address text.</param>
    internal static void SetClientIp(this HttpContext context, string? ip) => context.Items[ClientIpKey] = ip;
}
