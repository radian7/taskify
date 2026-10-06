using System.Text.Json;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.Security.Validation;

namespace Taskify.Notifications.Api.Domain;

/// <summary>What a trigger rule decided: who to notify, about what, with which text.</summary>
/// <param name="RecipientUserId">The user to notify; never the actor.</param>
/// <param name="Type">The kind of notification.</param>
/// <param name="TaskId">The task involved.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Summary">The text, at most 300 user-perceived characters.</param>
public sealed record NotificationDraft(Guid RecipientUserId, NotificationType Type, Guid TaskId, Guid ProjectId, string Summary);

/// <summary>The trigger table of data-model.md (spec FR-027): which events create a notification, and for whom.</summary>
public static class NotificationTriggerRules
{
    /// <summary>The longest summary, in user-perceived characters.</summary>
    public const int MaxSummaryCharacters = 300;

    /// <summary>Decides whether an event creates a notification.</summary>
    /// <param name="envelope">The validated event.</param>
    /// <param name="actorDisplayName">The display name of <see cref="EventEnvelope.ActorUserId"/>, from the user directory.</param>
    /// <returns>The notification to create, or <see langword="null"/> when the event creates none.</returns>
    public static NotificationDraft? Evaluate(EventEnvelope envelope, string actorDisplayName)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(actorDisplayName);

        var actor = envelope.ActorUserId;
        switch (envelope.Type)
        {
            case EventTypes.TaskCreated:
            {
                var p = envelope.Payload.Deserialize<TaskCreatedPayload>(ContractJson.Options)!;
                return Draft(p.AssigneeUserId, actor, NotificationType.TaskAssigned, p.TaskId, p.ProjectId, $"{actorDisplayName} assigned you '{p.Title}'");
            }

            case EventTypes.TaskAssigned:
            {
                var p = envelope.Payload.Deserialize<TaskAssignedPayload>(ContractJson.Options)!;
                return Draft(p.AssigneeUserId, actor, NotificationType.TaskAssigned, p.TaskId, p.ProjectId, $"{actorDisplayName} assigned you '{p.Title}'");
            }

            case EventTypes.TaskMoved:
            {
                var p = envelope.Payload.Deserialize<TaskMovedPayload>(ContractJson.Options)!;
                return Draft(p.AssigneeUserId, actor, NotificationType.TaskMoved, p.TaskId, p.ProjectId, $"{actorDisplayName} moved '{p.Title}' to {ColumnName(p.ToStatus)}");
            }

            case EventTypes.CommentAdded:
            {
                var p = envelope.Payload.Deserialize<CommentAddedPayload>(ContractJson.Options)!;
                return Draft(p.AssigneeUserId, actor, NotificationType.TaskCommented, p.TaskId, p.ProjectId, $"{actorDisplayName} commented on '{p.Title}'");
            }

            default:
                return null;
        }
    }

    private static NotificationDraft? Draft(Guid? recipient, Guid actor, NotificationType type, Guid taskId, Guid projectId, string summary) =>
        // Nobody to notify, or the actor would notify themselves (spec FR-027).
        recipient is not { } id || id == actor
            ? null
            : new NotificationDraft(id, type, taskId, projectId, TextLength.Truncate(summary, MaxSummaryCharacters));

    private static string ColumnName(TaskStatus status) => status switch
    {
        TaskStatus.ToDo => "To Do",
        TaskStatus.InProgress => "In Progress",
        TaskStatus.InReview => "In Review",
        TaskStatus.Done => "Done",
        _ => status.ToString(),
    };
}
