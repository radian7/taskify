using Microsoft.EntityFrameworkCore;
using Taskify.Tasks.Api.Domain;

namespace Taskify.Tasks.Api.Data;

/// <summary>Queries shared by the endpoints that change a task.</summary>
public static class TaskQueries
{
    /// <summary>
    /// Loads a task and locks its row until the surrounding transaction ends. Two changes to the same task are then
    /// applied one after the other, each starting from the state the previous one left (spec FR-011: the last save wins,
    /// research R14), so the move history stays a chain and events describe real before-and-after states.
    /// Call it inside an explicit transaction.
    /// </summary>
    /// <param name="db">The tasks database.</param>
    /// <param name="taskId">The task to load.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tracked task, or <see langword="null"/> when it does not exist.</returns>
    public static async Task<TaskItem?> LoadForUpdateAsync(this TasksDbContext db, Guid taskId, CancellationToken cancellationToken)
    {
        // Parameterized (never string-built) SQL: the interpolated value becomes a command parameter.
        var rows = await db.Tasks
            .FromSqlInterpolated($"SELECT * FROM tasks WHERE \"Id\" = {taskId} FOR UPDATE")
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>
    /// Loads a comment and locks its row until the surrounding transaction ends, so an edit and a delete that arrive together
    /// are applied one after the other: a deleted comment can never be edited back to life (spec FR-024).
    /// Call it inside an explicit transaction.
    /// </summary>
    /// <param name="db">The tasks database.</param>
    /// <param name="commentId">The comment to load.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tracked comment, or <see langword="null"/> when it does not exist.</returns>
    public static async Task<Comment?> LoadCommentForUpdateAsync(this TasksDbContext db, Guid commentId, CancellationToken cancellationToken)
    {
        var rows = await db.Comments
            .FromSqlInterpolated($"SELECT * FROM comments WHERE \"Id\" = {commentId} FOR UPDATE")
            .AsTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>Counts a task's comments that are not deleted (the number shown on its card).</summary>
    /// <param name="db">The tasks database.</param>
    /// <param name="taskId">The task.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of comments.</returns>
    public static Task<int> CountCommentsAsync(this TasksDbContext db, Guid taskId, CancellationToken cancellationToken) =>
        db.Comments.AsNoTracking().CountAsync(c => c.TaskId == taskId && c.DeletedAt == null, cancellationToken);
}
