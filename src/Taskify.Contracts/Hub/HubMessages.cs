namespace Taskify.Contracts.Hub;

// Messages sent by the Notifications API SignalR hub (/hubs/board) to the Web server. They are change
// signals: the Web server re-fetches the data from the REST APIs and never renders message content
// (contracts/realtime-hub.md). Shapes mirror the *Payload schemas in events.asyncapi.yaml.

/// <summary>Sent to group <c>project:{projectId}</c> when a board changes (AsyncAPI <c>BoardChanged</c>).</summary>
/// <param name="EventId">The source event ID; receivers ignore IDs they already handled.</param>
/// <param name="Type">The source event type.</param>
/// <param name="ProjectId">The affected project.</param>
/// <param name="TaskId">The affected task, when there is one.</param>
/// <param name="ActorUserId">Who made the change.</param>
/// <param name="OccurredAt">When the change happened.</param>
public sealed record BoardChanged(Guid EventId, string Type, Guid ProjectId, Guid? TaskId, Guid ActorUserId, DateTimeOffset OccurredAt);

/// <summary>Sent to group <c>task:{taskId}</c> when a task or its comments change (AsyncAPI <c>TaskChanged</c>).</summary>
/// <param name="EventId">The source event ID.</param>
/// <param name="Type">The source event type.</param>
/// <param name="TaskId">The affected task.</param>
/// <param name="ActorUserId">Who made the change.</param>
/// <param name="OccurredAt">When the change happened.</param>
public sealed record TaskChanged(Guid EventId, string Type, Guid TaskId, Guid ActorUserId, DateTimeOffset OccurredAt);

/// <summary>Sent to group <c>projects</c> when a project is created (AsyncAPI <c>ProjectListChanged</c>).</summary>
/// <param name="EventId">The source event ID.</param>
/// <param name="ProjectId">The new project.</param>
public sealed record ProjectListChanged(Guid EventId, Guid ProjectId);

/// <summary>
/// A notification as returned by the API and pushed to group <c>user:{recipientUserId}</c>
/// (AsyncAPI <c>NotificationCreated</c>).
/// </summary>
/// <param name="Id">The notification ID.</param>
/// <param name="Type">What happened.</param>
/// <param name="TaskId">The task involved.</param>
/// <param name="ProjectId">Its project, for linking to the board.</param>
/// <param name="ActorUserId">Who acted.</param>
/// <param name="Summary">Server-generated text, at most 300 user-perceived characters. Display it as plain text.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="IsRead">Whether the recipient has read it.</param>
public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    Guid TaskId,
    Guid ProjectId,
    Guid ActorUserId,
    string Summary,
    DateTimeOffset CreatedAt,
    bool IsRead);
