using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskify.Contracts;

/// <summary>
/// The strict JSON settings shared by every Taskify service for REST bodies and events
/// (research R7): camelCase names that must match exactly, unknown properties rejected, required
/// constructor parameters enforced, and enums limited to their exact names.
/// </summary>
public static class ContractJson
{
    /// <summary>A shared, read-only instance configured with <see cref="Configure"/>.</summary>
    public static JsonSerializerOptions Options { get; } = CreateReadOnly();

    /// <summary>Creates a new, mutable options instance with the strict settings applied.</summary>
    /// <returns>New options.</returns>
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }

    /// <summary>Applies the strict settings to existing options (also used for ASP.NET Core's JSON options).</summary>
    /// <param name="options">The options to configure.</param>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Security: exact property names only, so a request cannot smuggle in a differently cased duplicate.
        options.PropertyNameCaseInsensitive = false;
        // Unknown fields are rejected, not ignored (allow-list validation).
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.RespectRequiredConstructorParameters = true;
        options.RespectNullableAnnotations = true;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new StrictEnumConverterFactory());
    }

    private static JsonSerializerOptions CreateReadOnly()
    {
        var options = Create();
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
