using Microsoft.AspNetCore.Builder;

namespace Taskify.Security.ApiKeys;

/// <summary>
/// Endpoint metadata listing which calling services may use a route (the research R8 key matrix).
/// Routes without this metadata are closed to every caller (fail closed).
/// </summary>
/// <param name="callers">The allowed caller names (see <see cref="Callers"/>).</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowCallersAttribute(params string[] callers) : Attribute
{
    /// <summary>Gets the allowed caller names.</summary>
    public IReadOnlyList<string> Callers { get; } = callers;
}

/// <summary>Endpoint builder helpers for <see cref="AllowCallersAttribute"/>.</summary>
public static class AllowCallersExtensions
{
    /// <summary>Restricts the endpoint to the named calling services.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <param name="callers">The caller names allowed on this route.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder RequireCallers<TBuilder>(this TBuilder builder, params string[] callers)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowCallersAttribute(callers));
}
