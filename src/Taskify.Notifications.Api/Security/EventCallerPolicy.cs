using Taskify.Contracts.Events;
using Taskify.Security.ApiKeys;

namespace Taskify.Notifications.Api.Security;

/// <summary>
/// Decides which calling service may send which event type to <c>/internal/events</c> (research R8 key matrix):
/// the Projects API only <c>ProjectCreated</c>, the Tasks API the other seven types, nobody else anything.
/// </summary>
public static class EventCallerPolicy
{
    /// <summary>Checks whether a caller may send an event type.</summary>
    /// <param name="caller">The caller name from <c>ApiKeyMiddleware</c> (see <c>Callers</c>), or <see langword="null"/>.</param>
    /// <param name="eventType">The event type name.</param>
    /// <returns><see langword="true"/> when allowed; otherwise the endpoint answers <c>403</c>.</returns>
    public static bool IsAllowed(string? caller, string eventType)
    {
        // Allow-list with exact (ordinal) matching: a valid key for the wrong service must not be able to forge
        // another service's events, even though both are internal (Principle II).
        return caller switch
        {
            Callers.Projects => EventTypes.PublishedByProjects.Contains(eventType),
            Callers.Tasks => EventTypes.PublishedByTasks.Contains(eventType),
            _ => false,
        };
    }
}
