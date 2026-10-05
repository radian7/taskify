using Microsoft.AspNetCore.Builder;

namespace Taskify.Security.Users;

/// <summary>
/// Endpoint metadata marking a route that does not need the <c>X-Taskify-User</c> header: the user
/// routes (needed before anyone is selected), <c>/internal/events</c> (the actor is in the envelope) and the hub.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipActingUserAttribute : Attribute;

/// <summary>Endpoint builder helpers for <see cref="SkipActingUserAttribute"/>.</summary>
public static class SkipActingUserExtensions
{
    /// <summary>Exempts the endpoint from the acting-user requirement.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AllowAnonymousActingUser<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SkipActingUserAttribute());
}
