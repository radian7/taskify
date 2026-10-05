namespace Taskify.Security.ApiKeys;

/// <summary>The names of the services that call each other (research R8 key matrix).</summary>
public static class Callers
{
    /// <summary>The Blazor Server web app.</summary>
    public const string Web = "web";

    /// <summary>The Projects API.</summary>
    public const string Projects = "projects";

    /// <summary>The Tasks API.</summary>
    public const string Tasks = "tasks";

    /// <summary>The Notifications API.</summary>
    public const string Notifications = "notifications";
}

/// <summary>
/// API key configuration of one service (deviation D2: per-caller keys instead of OAuth client credentials).
/// Keys are secrets loaded from configuration (Aspire secret parameters) and are never logged.
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ApiKeys";

    /// <summary>The keys this service accepts, by caller name (see <see cref="Callers"/>).</summary>
    public Dictionary<string, string> Accepted { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The key this service presents when it calls other services.</summary>
    public string? OwnKey { get; set; }
}
