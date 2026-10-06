using Taskify.Contracts;

namespace Taskify.Notifications.Api.Domain;

/// <summary>
/// An in-app notification for one recipient (data-model.md, spec FR-027 to FR-030). The recipient is never the acting
/// user. Only the recipient may read or change it; anyone else gets <c>404</c> (FR-029).
/// </summary>
public sealed class Notification
{
    // For EF Core only.
    private Notification()
    {
        Summary = string.Empty;
    }

    /// <summary>Creates an unread notification.</summary>
    /// <param name="id">The generated ID.</param>
    /// <param name="recipientUserId">The user to notify.</param>
    /// <param name="type">What happened.</param>
    /// <param name="taskId">The task that triggered it.</param>
    /// <param name="projectId">Its project, for linking to the board.</param>
    /// <param name="actorUserId">Who made the change.</param>
    /// <param name="summary">The server-generated text (at most 300 user-perceived characters).</param>
    /// <param name="createdAt">When it was created (set by the server).</param>
    /// <param name="sourceEventId">The event that caused it; unique, so redelivery creates nothing new.</param>
    public Notification(
        Guid id, Guid recipientUserId, NotificationType type, Guid taskId, Guid projectId, Guid actorUserId,
        string summary, DateTimeOffset createdAt, Guid sourceEventId)
    {
        Id = id;
        RecipientUserId = recipientUserId;
        Type = type;
        TaskId = taskId;
        ProjectId = projectId;
        ActorUserId = actorUserId;
        Summary = summary;
        CreatedAt = createdAt;
        SourceEventId = sourceEventId;
    }

    /// <summary>Gets the notification ID.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the recipient, a predefined user who is never the actor.</summary>
    public Guid RecipientUserId { get; private set; }

    /// <summary>Gets what happened.</summary>
    public NotificationType Type { get; private set; }

    /// <summary>Gets the task that triggered the notification.</summary>
    public Guid TaskId { get; private set; }

    /// <summary>Gets the project of the task.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Gets who made the change.</summary>
    public Guid ActorUserId { get; private set; }

    /// <summary>Gets the server-generated summary text.</summary>
    public string Summary { get; private set; }

    /// <summary>Gets when the notification was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the recipient read it, or <see langword="null"/> while it is unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>Gets the ID of the event that caused this notification (unique).</summary>
    public Guid SourceEventId { get; private set; }

    /// <summary>Marks the notification read. Calling it again changes nothing (idempotent).</summary>
    /// <param name="now">The current time.</param>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
