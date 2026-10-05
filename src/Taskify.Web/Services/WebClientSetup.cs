using Taskify.Security.ApiKeys;
using Taskify.Security.Users;
using Taskify.Web.Services.ApiClients;

namespace Taskify.Web.Services;

/// <summary>Registers the Web app's services: API clients (HTTPS only), the user directory and per-circuit state.</summary>
public static class WebClientSetup
{
    /// <summary>Registers the typed API clients, the user directory and the per-circuit services.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTaskifyWebServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Service-to-service traffic is HTTPS only; "https+http://" would silently fall back to plain HTTP (research R16).
        services.AddTaskifyHttpClient<ProjectsClient>("https://projects-api");
        services.AddTaskifyHttpClient<TasksClient>("https://tasks-api");
        services.AddTaskifyHttpClient<NotificationsClient>("https://notifications-api");

        // The Web app validates the selected-user cookie against the same directory the APIs use (research R4).
        services.AddRemoteUserDirectory();

        services.AddSingleton<SelectedUserCookie>();
        services.AddScoped<CircuitIdentity>();
        services.AddScoped<CurrentUserService>();
        services.AddScoped<ClientIpCapture>();
        return services;
    }
}
