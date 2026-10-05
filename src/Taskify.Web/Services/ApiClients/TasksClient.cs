namespace Taskify.Web.Services.ApiClients;

/// <summary>Calls the Tasks API (tasks, moves, history, comments). Methods are added by the user stories that need them.</summary>
/// <param name="http">The HTTP client for the Tasks API.</param>
/// <param name="identity">The identity of the circuit making the call.</param>
public sealed class TasksClient(HttpClient http, CircuitIdentity identity) : ApiClientBase(http, identity)
{
    /// <summary>Gets all tasks of a project for its board.</summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tasks (possibly none), or the error.</returns>
    public Task<ApiResult<List<TaskSummaryDto>>> ListTasksAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        GetAsync<List<TaskSummaryDto>>($"/api/tasks?projectId={projectId:D}", cancellationToken: cancellationToken);

    /// <summary>Gets one task with its description.</summary>
    /// <param name="taskId">The task ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The task, or the error (404 when it does not exist).</returns>
    public Task<ApiResult<TaskDetailDto>> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        GetAsync<TaskDetailDto>($"/api/tasks/{taskId:D}", cancellationToken: cancellationToken);

    /// <summary>Moves a task to another column (spec FR-012). Any user may move any task to any column.</summary>
    /// <param name="taskId">The task ID.</param>
    /// <param name="toStatus">The column to move to.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The task after the move, or the error.</returns>
    public Task<ApiResult<MoveTaskResultDto>> MoveAsync(Guid taskId, TaskStatus toStatus, CancellationToken cancellationToken = default) =>
        SendAsync<MoveTaskResultDto>(HttpMethod.Post, $"/api/tasks/{taskId:D}/moves", new MoveTaskBody(toStatus), cancellationToken: cancellationToken);

    /// <summary>Gets a task's status history, newest first (spec FR-023).</summary>
    /// <param name="taskId">The task ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The history, or the error (404 when the task does not exist).</returns>
    public Task<ApiResult<List<StatusChangeDto>>> GetHistoryAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        GetAsync<List<StatusChangeDto>>($"/api/tasks/{taskId:D}/history", cancellationToken: cancellationToken);

    /// <summary>Creates a task in the To Do column (spec FR-009).</summary>
    /// <param name="projectId">The project.</param>
    /// <param name="title">The title (1–200 characters).</param>
    /// <param name="description">The description (up to 5,000 characters), or <see langword="null"/>.</param>
    /// <param name="assigneeUserId">The assignee, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new task, or the error.</returns>
    public Task<ApiResult<TaskDetailDto>> CreateTaskAsync(
        Guid projectId, string title, string? description, Guid? assigneeUserId, CancellationToken cancellationToken = default) =>
        SendAsync<TaskDetailDto>(HttpMethod.Post, "/api/tasks", new CreateTaskBody(projectId, title, description, assigneeUserId), cancellationToken: cancellationToken);

    /// <summary>Replaces a task's title, description and assignee (spec FR-011). Any user may edit any task.</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="title">The title (1–200 characters).</param>
    /// <param name="description">The description (up to 5,000 characters), or <see langword="null"/>.</param>
    /// <param name="assigneeUserId">The assignee, or <see langword="null"/> to unassign.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The task after the edit, or the error.</returns>
    public Task<ApiResult<TaskDetailDto>> UpdateTaskAsync(
        Guid taskId, string title, string? description, Guid? assigneeUserId, CancellationToken cancellationToken = default) =>
        SendAsync<TaskDetailDto>(HttpMethod.Put, $"/api/tasks/{taskId:D}", new UpdateTaskBody(title, assigneeUserId, description), cancellationToken: cancellationToken);

    /// <summary>Gets a task's comments, oldest first (spec FR-015).</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The comments, or the error.</returns>
    public Task<ApiResult<List<CommentDto>>> GetCommentsAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        GetAsync<List<CommentDto>>($"/api/tasks/{taskId:D}/comments", cancellationToken: cancellationToken);

    /// <summary>Adds a comment as the acting user.</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="text">The text (1–2,000 characters).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new comment, or the error.</returns>
    public Task<ApiResult<CommentDto>> AddCommentAsync(Guid taskId, string text, CancellationToken cancellationToken = default) =>
        SendAsync<CommentDto>(HttpMethod.Post, $"/api/tasks/{taskId:D}/comments", new CommentTextBody(text), cancellationToken: cancellationToken);

    /// <summary>Edits the acting user's own comment (spec FR-016).</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="commentId">The comment.</param>
    /// <param name="text">The new text (1–2,000 characters).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The comment after the edit, or the error.</returns>
    public Task<ApiResult<CommentDto>> EditCommentAsync(Guid taskId, Guid commentId, string text, CancellationToken cancellationToken = default) =>
        SendAsync<CommentDto>(HttpMethod.Put, $"/api/tasks/{taskId:D}/comments/{commentId:D}", new CommentTextBody(text), cancellationToken: cancellationToken);

    /// <summary>Deletes the acting user's own comment (spec FR-024).</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="commentId">The comment.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The deleted comment (as a placeholder), or the error.</returns>
    public Task<ApiResult<CommentDto>> DeleteCommentAsync(Guid taskId, Guid commentId, CancellationToken cancellationToken = default) =>
        SendAsync<CommentDto>(HttpMethod.Delete, $"/api/tasks/{taskId:D}/comments/{commentId:D}", body: null, cancellationToken: cancellationToken);
}
