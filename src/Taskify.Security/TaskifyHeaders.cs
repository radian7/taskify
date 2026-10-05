namespace Taskify.Security;

/// <summary>The custom HTTP header names of the internal Taskify protocol (contracts/*-api.yaml).</summary>
public static class TaskifyHeaders
{
    /// <summary>The calling service's secret key (research R8, deviation D2).</summary>
    public const string ApiKey = "X-Api-Key";

    /// <summary>The ID of the acting user. Phase 1 has no login, so the Web app sends the user chosen in the UI.</summary>
    public const string ActingUser = "X-Taskify-User";

    /// <summary>The end user's source IP, set only by the Web app and used only for audit logs (spec FR-032).</summary>
    public const string ClientIp = "X-Taskify-Client-Ip";
}
