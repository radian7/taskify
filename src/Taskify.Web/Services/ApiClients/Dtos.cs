namespace Taskify.Web.Services.ApiClients;

// The shapes the Web app reads from the APIs (contracts/*-api.yaml). Every text field is displayed as plain text only.

/// <summary>A project (contracts/projects-api.yaml, schema <c>Project</c>).</summary>
/// <param name="Id">The project ID.</param>
/// <param name="Name">The name (1–100 characters).</param>
/// <param name="Description">The description (up to 1,000 characters), or <see langword="null"/>.</param>
/// <param name="CreatedByUserId">The creator.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record ProjectDto(Guid Id, string Name, string? Description, Guid CreatedByUserId, DateTimeOffset CreatedAt);

/// <summary>A task as listed on a board (contracts/tasks-api.yaml, schema <c>TaskSummary</c>).</summary>
/// <param name="Id">The task ID.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The title (1–200 characters).</param>
/// <param name="Status">The column.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/>.</param>
/// <param name="CommentCount">The number of comments that are not deleted.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record TaskSummaryDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    TaskStatus Status,
    Guid? AssigneeUserId,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A task with its description and creator (contracts/tasks-api.yaml, schema <c>TaskDetail</c>).</summary>
/// <param name="Id">The task ID.</param>
/// <param name="ProjectId">Its project.</param>
/// <param name="Title">The title (1–200 characters).</param>
/// <param name="Description">The description (up to 5,000 characters), or <see langword="null"/>.</param>
/// <param name="Status">The column.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/>.</param>
/// <param name="CreatedByUserId">The creator.</param>
/// <param name="CommentCount">The number of comments that are not deleted.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
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
    DateTimeOffset UpdatedAt);

/// <summary>One recorded move of a task (contracts/tasks-api.yaml, schema <c>StatusChange</c>).</summary>
/// <param name="Id">The status change ID.</param>
/// <param name="TaskId">The task that moved.</param>
/// <param name="FromStatus">The column it left.</param>
/// <param name="ToStatus">The column it entered.</param>
/// <param name="MovedByUserId">Who moved it.</param>
/// <param name="MovedAt">When it moved.</param>
public sealed record StatusChangeDto(Guid Id, Guid TaskId, TaskStatus FromStatus, TaskStatus ToStatus, Guid MovedByUserId, DateTimeOffset MovedAt);

/// <summary>The result of a move (contracts/tasks-api.yaml, schema <c>MoveTaskResult</c>).</summary>
/// <param name="Changed"><see langword="false"/> when the task was already in that column and nothing was saved.</param>
/// <param name="Task">The task after the move.</param>
/// <param name="StatusChange">The history entry that was written, or <see langword="null"/>.</param>
public sealed record MoveTaskResultDto(bool Changed, TaskDetailDto Task, StatusChangeDto? StatusChange);

/// <summary>The body of a move request (contracts/tasks-api.yaml, schema <c>MoveTaskRequest</c>).</summary>
/// <param name="ToStatus">The column to move to.</param>
public sealed record MoveTaskBody(TaskStatus ToStatus);

/// <summary>The body of a create-project request (contracts/projects-api.yaml, schema <c>CreateProjectRequest</c>).</summary>
/// <param name="Name">The name: 1–100 characters after trimming.</param>
/// <param name="Description">The description: up to 1,000 characters after trimming, or <see langword="null"/>.</param>
public sealed record CreateProjectBody(string Name, string? Description);

/// <summary>The body of a create-task request (contracts/tasks-api.yaml, schema <c>CreateTaskRequest</c>).</summary>
/// <param name="ProjectId">The project.</param>
/// <param name="Title">The title: 1–200 characters after trimming.</param>
/// <param name="Description">The description: up to 5,000 characters after trimming, or <see langword="null"/>.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/>.</param>
public sealed record CreateTaskBody(Guid ProjectId, string Title, string? Description, Guid? AssigneeUserId);

/// <summary>The body of an edit-task request (contracts/tasks-api.yaml, schema <c>UpdateTaskRequest</c>).</summary>
/// <param name="Title">The title: 1–200 characters after trimming.</param>
/// <param name="AssigneeUserId">The assignee, or <see langword="null"/> to unassign.</param>
/// <param name="Description">The description: up to 5,000 characters after trimming, or <see langword="null"/>.</param>
public sealed record UpdateTaskBody(string Title, Guid? AssigneeUserId, string? Description);
