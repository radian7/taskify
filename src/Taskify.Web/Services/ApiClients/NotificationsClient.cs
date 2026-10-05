namespace Taskify.Web.Services.ApiClients;

/// <summary>Calls the Notifications API. Methods are added by the user story that needs them (US6).</summary>
/// <param name="http">The HTTP client for the Notifications API.</param>
/// <param name="identity">The identity of the circuit making the call.</param>
public sealed class NotificationsClient(HttpClient http, CircuitIdentity identity) : ApiClientBase(http, identity);
