namespace Taskify.Contracts.Events;

// One record per event type. Field names, nullability and ordering mirror the *Envelope.payload schemas in
// specs/001-taskify-kanban-board/contracts/events.asyncapi.yaml. Descriptions and comment text are NEVER
// part of a payload (spec FR-022, research R13).

/// <summary>Payload of <see cref="EventTypes.ProjectCreated"/> (AsyncAPI message <c>ProjectCreated</c>).</summary>
/// <param name="ProjectId">The new project.</param>
/// <param name="Name">The project name (1–100 user-perceived characters).</param>
public sealed record ProjectCreatedPayload(Guid ProjectId, string Name);

/// <summary>Payload of <see cref="EventTypes.TaskCreated"/> (AsyncAPI message <c>TaskCreated</c>).</summary>
/// <param name="TaskId">The new task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The task title (1–200 user-perceived characters).</param>
/// <param name="Status">The starting column; always <see cref="TaskStatus.ToDo"/>.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/> when unassigned.</param>
public sealed record TaskCreatedPayload(Guid TaskId, Guid ProjectId, string Title, TaskStatus Status, Guid? AssigneeUserId);

/// <summary>Payload of <see cref="EventTypes.TaskUpdated"/> (AsyncAPI message <c>TaskUpdated</c>).</summary>
/// <param name="TaskId">The task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The task title after the update.</param>
public sealed record TaskUpdatedPayload(Guid TaskId, Guid ProjectId, string Title);

/// <summary>Payload of <see cref="EventTypes.TaskAssigned"/> (AsyncAPI message <c>TaskAssigned</c>).</summary>
/// <param name="TaskId">The task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The task title.</param>
/// <param name="PreviousAssigneeUserId">The assignee before the change, or <see langword="null"/>.</param>
/// <param name="AssigneeUserId">The assignee after the change, or <see langword="null"/> when unassigned.</param>
public sealed record TaskAssignedPayload(
    Guid TaskId, Guid ProjectId, string Title, Guid? PreviousAssigneeUserId, Guid? AssigneeUserId);

/// <summary>Payload of <see cref="EventTypes.TaskMoved"/> (AsyncAPI message <c>TaskMoved</c>).</summary>
/// <param name="TaskId">The task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The task title.</param>
/// <param name="FromStatus">The column it left.</param>
/// <param name="ToStatus">The column it entered.</param>
/// <param name="AssigneeUserId">The current assignee, or <see langword="null"/>.</param>
public sealed record TaskMovedPayload(
    Guid TaskId, Guid ProjectId, string Title, TaskStatus FromStatus, TaskStatus ToStatus, Guid? AssigneeUserId);

/// <summary>Payload of <see cref="EventTypes.CommentAdded"/> (AsyncAPI message <c>CommentAdded</c>).</summary>
/// <param name="TaskId">The task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The task title.</param>
/// <param name="CommentId">The new comment (its text is not included).</param>
/// <param name="AssigneeUserId">The current assignee, or <see langword="null"/>.</param>
public sealed record CommentAddedPayload(Guid TaskId, Guid ProjectId, string Title, Guid CommentId, Guid? AssigneeUserId);

/// <summary>Payload of <see cref="EventTypes.CommentEdited"/> (AsyncAPI message <c>CommentEdited</c>).</summary>
/// <param name="TaskId">The task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="CommentId">The edited comment.</param>
public sealed record CommentEditedPayload(Guid TaskId, Guid ProjectId, Guid CommentId);

/// <summary>Payload of <see cref="EventTypes.CommentDeleted"/> (AsyncAPI message <c>CommentDeleted</c>).</summary>
/// <param name="TaskId">The task.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="CommentId">The deleted comment.</param>
public sealed record CommentDeletedPayload(Guid TaskId, Guid ProjectId, Guid CommentId);
