using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Taskify.Contracts.Events;

namespace Taskify.Notifications.Api.Hubs;

/// <summary>
/// Turns an accepted domain event into change signals on the board hub (contracts/realtime-hub.md, research R5).
/// Signals carry IDs, the type, the actor and the time only: never titles or text, because the Web server re-fetches
/// the data from the owning API.
/// </summary>
/// <param name="hub">The hub context.</param>
public sealed class RealtimeBroadcaster(IHubContext<BoardHub> hub)
{
    private static readonly HashSet<string> BoardTypes = new(StringComparer.Ordinal)
    {
        EventTypes.TaskCreated, EventTypes.TaskUpdated, EventTypes.TaskAssigned, EventTypes.TaskMoved,
        EventTypes.CommentAdded, EventTypes.CommentDeleted,
    };

    private static readonly HashSet<string> TaskTypes = new(StringComparer.Ordinal)
    {
        EventTypes.TaskUpdated, EventTypes.TaskAssigned, EventTypes.TaskMoved,
        EventTypes.CommentAdded, EventTypes.CommentEdited, EventTypes.CommentDeleted,
    };

    /// <summary>Sends the signals for one validated event.</summary>
    /// <param name="envelope">The event.</param>
    /// <param name="cancellationToken">Cancels the sending.</param>
    /// <returns>A task that completes when the signals were handed to SignalR.</returns>
    public async Task BroadcastAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var projectId = ReadGuid(envelope.Payload, "projectId");
        var taskId = ReadGuid(envelope.Payload, "taskId");

        if (envelope.Type == EventTypes.ProjectCreated && projectId is { } created)
        {
            await hub.Clients.Group(BoardHub.ProjectsGroup).SendAsync(
                "ProjectListChanged", new { eventId = envelope.EventId, projectId = created }, cancellationToken);
        }

        if (BoardTypes.Contains(envelope.Type) && projectId is { } project)
        {
            await hub.Clients.Group(BoardHub.ProjectGroup(project)).SendAsync(
                "BoardChanged",
                new
                {
                    eventId = envelope.EventId,
                    type = envelope.Type,
                    projectId = project,
                    taskId,
                    actorUserId = envelope.ActorUserId,
                    occurredAt = envelope.OccurredAt,
                },
                cancellationToken);
        }

        if (TaskTypes.Contains(envelope.Type) && taskId is { } task)
        {
            await hub.Clients.Group(BoardHub.TaskGroup(task)).SendAsync(
                "TaskChanged",
                new
                {
                    eventId = envelope.EventId,
                    type = envelope.Type,
                    taskId = task,
                    actorUserId = envelope.ActorUserId,
                    occurredAt = envelope.OccurredAt,
                },
                cancellationToken);
        }
    }

    private static Guid? ReadGuid(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetGuid(out var id)
            ? id
            : null;
}
