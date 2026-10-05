using Microsoft.AspNetCore.Http;

namespace Taskify.Security.Users;

/// <summary>The user on whose behalf the current request is made (phase 1: chosen from a list, no login).</summary>
public interface ICurrentActingUser
{
    /// <summary>Gets the validated acting user ID.</summary>
    /// <exception cref="InvalidOperationException">The route is exempt from, or ran before, acting-user validation.</exception>
    Guid UserId { get; }
}

/// <summary>Reads the acting user that <see cref="ActingUserMiddleware"/> validated for the current request.</summary>
/// <param name="accessor">Gives access to the current request.</param>
public sealed class HttpCurrentActingUser(IHttpContextAccessor accessor) : ICurrentActingUser
{
    /// <inheritdoc />
    public Guid UserId =>
        accessor.HttpContext?.GetActingUserId()
        ?? throw new InvalidOperationException("No validated acting user on this request.");
}
