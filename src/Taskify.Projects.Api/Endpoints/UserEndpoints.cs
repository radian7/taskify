using Taskify.Contracts;
using Taskify.Security.ApiKeys;
using Taskify.Security.Errors;
using Taskify.Security.RateLimiting;
using Taskify.Security.Users;

namespace Taskify.Projects.Api.Endpoints;

/// <summary>A predefined user as returned by the API (contracts/projects-api.yaml, schema <c>User</c>).</summary>
/// <param name="Id">The fixed user ID.</param>
/// <param name="DisplayName">The user's name (1–100 characters). Display it as plain text.</param>
/// <param name="Role">The role label.</param>
public sealed record UserDto(Guid Id, string DisplayName, UserRole Role);

/// <summary>The read-only user directory routes (spec FR-001, FR-003).</summary>
public static class UserEndpoints
{
    /// <summary>
    /// Maps <c>GET /api/users</c> and <c>GET /api/users/{userId}</c>. They need no acting user, because they are used
    /// before anyone is selected and by services building their own directory (research R8). There are deliberately
    /// no routes to create, change or delete a user.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireCallers(Callers.Web, Callers.Tasks, Callers.Notifications)
            .AllowAnonymousActingUser()
            .RequireReads();

        group.MapGet("/", async (IUserDirectory users, CancellationToken cancellationToken) =>
            Results.Ok((await users.GetAllAsync(cancellationToken)).Select(ToDto)))
            .WithName("listUsers")
            .Produces<IEnumerable<UserDto>>();

        group.MapGet("/{userId:guid}", async (Guid userId, IUserDirectory users, CancellationToken cancellationToken) =>
        {
            var user = (await users.GetAllAsync(cancellationToken)).FirstOrDefault(u => u.Id == userId);
            return user is null ? Problems.NotFound("User") : Results.Ok(ToDto(user));
        })
            .WithName("getUser")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static UserDto ToDto(UserInfo user) => new(user.Id, user.DisplayName, user.Role);
}
