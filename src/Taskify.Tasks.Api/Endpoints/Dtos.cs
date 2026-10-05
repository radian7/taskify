using Taskify.Tasks.Api.Domain;

namespace Taskify.Tasks.Api.Endpoints;

/// <summary>A task as listed on a board (contracts/tasks-api.yaml, schema <c>TaskSummary</c>).</summary>
/// <param name="Id">The task ID.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The title (1–200 characters). Display it as plain text.</param>
/// <param name="Status">The column.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/>.</param>
/// <param name="CommentCount">The number of comments that are not deleted.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
/// <param name="UpdatedAt">When it last changed (UTC).</param>
public sealed record TaskSummaryDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    TaskStatus Status,
    Guid? AssigneeUserId,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Maps a task to its summary shape.</summary>
    /// <param name="task">The task.</param>
    /// <param name="commentCount">The number of comments that are not deleted.</param>
    /// <returns>The DTO.</returns>
    public static TaskSummaryDto From(TaskItem task, int commentCount) =>
        new(task.Id, task.ProjectId, task.Title, task.Status, task.AssigneeUserId, commentCount, task.CreatedAt, task.UpdatedAt);
}

/// <summary>A task with its description and creator (contracts/tasks-api.yaml, schema <c>TaskDetail</c>).</summary>
/// <param name="Id">The task ID.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The title (1–200 characters). Display it as plain text.</param>
/// <param name="Description">The description (up to 5,000 characters), or <see langword="null"/>. Display it as plain text.</param>
/// <param name="Status">The column.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/>.</param>
/// <param name="CreatedByUserId">The creator.</param>
/// <param name="CommentCount">The number of comments that are not deleted.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
/// <param name="UpdatedAt">When it last changed (UTC).</param>
public sealed record TaskDetailDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    TaskStatus Status,
    Guid? AssigneeUserId,
    Guid CreatedByUserId,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Maps a task to its detail shape.</summary>
    /// <param name="task">The task.</param>
    /// <param name="commentCount">The number of comments that are not deleted.</param>
    /// <returns>The DTO.</returns>
    public static TaskDetailDto From(TaskItem task, int commentCount) =>
        new(task.Id, task.ProjectId, task.Title, task.Description, task.Status, task.AssigneeUserId, task.CreatedByUserId, commentCount, task.CreatedAt, task.UpdatedAt);
}

/// <summary>The body of <c>POST /api/tasks/{taskId}/moves</c> (contracts/tasks-api.yaml, schema <c>MoveTaskRequest</c>).</summary>
/// <param name="ToStatus">The column to move the task to: exactly <c>ToDo</c>, <c>InProgress</c>, <c>InReview</c> or <c>Done</c>.</param>
public sealed record MoveTaskRequest(TaskStatus ToStatus);

/// <summary>One recorded move (contracts/tasks-api.yaml, schema <c>StatusChange</c>).</summary>
/// <param name="Id">The status change ID.</param>
/// <param name="TaskId">The task that moved.</param>
/// <param name="FromStatus">The column it left.</param>
/// <param name="ToStatus">The column it entered.</param>
/// <param name="MovedByUserId">Who moved it.</param>
/// <param name="MovedAt">When it moved (UTC).</param>
public sealed record StatusChangeDto(Guid Id, Guid TaskId, TaskStatus FromStatus, TaskStatus ToStatus, Guid MovedByUserId, DateTimeOffset MovedAt)
{
    /// <summary>Maps a status change to its API shape.</summary>
    /// <param name="change">The status change.</param>
    /// <returns>The DTO.</returns>
    public static StatusChangeDto From(StatusChange change) =>
        new(change.Id, change.TaskId, change.FromStatus, change.ToStatus, change.MovedByUserId, change.MovedAt);
}

/// <summary>The result of a move (contracts/tasks-api.yaml, schema <c>MoveTaskResult</c>).</summary>
/// <param name="Changed"><see langword="false"/> when the task was already in that column and nothing was saved.</param>
/// <param name="Task">The task after the move.</param>
/// <param name="StatusChange">The history entry that was written, or <see langword="null"/> when nothing changed.</param>
public sealed record MoveTaskResult(bool Changed, TaskDetailDto Task, StatusChangeDto? StatusChange);

/// <summary>The body of <c>POST /api/tasks</c> (contracts/tasks-api.yaml, schema <c>CreateTaskRequest</c>).</summary>
/// <param name="ProjectId">The project the task belongs to; it must exist.</param>
/// <param name="Title">The title: 1–200 user-perceived characters after trimming.</param>
/// <param name="Description">The description: up to 5,000 user-perceived characters after trimming, or <see langword="null"/>.</param>
/// <param name="AssigneeUserId">The assignee (one of the five users), or <see langword="null"/> for none.</param>
public sealed record CreateTaskRequest(Guid ProjectId, string Title, string? Description = null, Guid? AssigneeUserId = null);

/// <summary>The body of <c>PUT /api/tasks/{taskId}</c> (contracts/tasks-api.yaml, schema <c>UpdateTaskRequest</c>). It replaces all three fields.</summary>
/// <param name="Title">The title: 1–200 user-perceived characters after trimming.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/> to unassign the task. The field must be present.</param>
/// <param name="Description">The description: up to 5,000 user-perceived characters after trimming, or <see langword="null"/>.</param>
public sealed record UpdateTaskRequest(string Title, Guid? AssigneeUserId, string? Description = null);

/// <summary>The body of a comment request (contracts/tasks-api.yaml, schema <c>CommentTextRequest</c>).</summary>
/// <param name="Text">The comment: 1–2,000 user-perceived characters after trimming.</param>
public sealed record CommentTextRequest(string Text);

/// <summary>A comment as returned by the API (contracts/tasks-api.yaml, schema <c>Comment</c>).</summary>
/// <param name="Id">The comment ID.</param>
/// <param name="TaskId">The task it is on.</param>
/// <param name="AuthorUserId">The author.</param>
/// <param name="Text">The text, or <see langword="null"/> when the comment is deleted. Display it as plain text.</param>
/// <param name="CreatedAt">When it was posted (UTC).</param>
/// <param name="EditedAt">When it was last edited, or <see langword="null"/>.</param>
/// <param name="DeletedAt">When it was deleted, or <see langword="null"/>.</param>
/// <param name="IsDeleted">Whether it was deleted; a deleted comment shows as a placeholder.</param>
public sealed record CommentDto(
    Guid Id,
    Guid TaskId,
    Guid AuthorUserId,
    string? Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    bool IsDeleted)
{
    /// <summary>Maps a comment to its API shape.</summary>
    /// <param name="comment">The comment.</param>
    /// <returns>The DTO.</returns>
    public static CommentDto From(Comment comment) =>
        new(comment.Id, comment.TaskId, comment.AuthorUserId, comment.Text, comment.CreatedAt, comment.EditedAt, comment.DeletedAt, comment.IsDeleted);
}
