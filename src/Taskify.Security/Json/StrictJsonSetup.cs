using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;

namespace Taskify.Security.Json;

/// <summary>
/// Makes ASP.NET Core read request bodies with the strict <see cref="ContractJson"/> settings: unknown fields
/// are rejected (400), property names must match exactly, required fields must be present, and enums accept
/// only their exact names (research R7, constitution Principle II).
/// </summary>
public static class StrictJsonSetup
{
    /// <summary>Applies the strict JSON settings to minimal API binding and results.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddStrictJson(this IServiceCollection services) =>
        services.Configure<JsonOptions>(options => ContractJson.Configure(options.SerializerOptions));
}
