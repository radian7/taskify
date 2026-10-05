using Microsoft.EntityFrameworkCore;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.RateLimiting;
using Taskify.Tasks.Api.Data;

namespace Taskify.Tasks.Api.Endpoints;

/// <summary>Routes that read tasks (spec FR-005, FR-008, FR-010; contracts/tasks-api.yaml).</summary>
public static class TaskReadEndpoints
{
    /// <summary>Maps <c>GET /api/tasks</c> and <c>GET /api/tasks/{taskId}</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTaskReadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks")
            .RequireCallers(Callers.Web)
            .RequireReads();

        // All tasks of one project, for its board. A project with no tasks (or an unknown project) has an empty list.
        // A missing or malformed projectId never reaches the handler: model binding rejects it with 400.
        group.MapGet("/", async (Guid projectId, TasksDbContext db, CancellationToken cancellationToken) =>
        {
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            // The number of comments on each card, not counting deleted ones.
            var counts = await db.Comments.AsNoTracking()
                .Where(c => c.DeletedAt == null && db.Tasks.Any(t => t.Id == c.TaskId && t.ProjectId == projectId))
                .GroupBy(c => c.TaskId)
                .Select(g => new { TaskId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TaskId, x => x.Count, cancellationToken);

            // Columns in board order, newest first inside each column. Sorted in memory: the status is stored as text.
            var ordered = tasks
                .OrderBy(t => t.Status)
                .ThenByDescending(t => t.CreatedAt)
                .Select(t => TaskSummaryDto.From(t, counts.GetValueOrDefault(t.Id)));
            return Results.Ok(ordered);
        })
            .WithName("listTasks")
            .Produces<IEnumerable<TaskSummaryDto>>();

        group.MapGet("/{taskId:guid}", async (Guid taskId, TasksDbContext db, IAuditLogger audit, CancellationToken cancellationToken) =>
        {
            var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            if (task is null)
            {
                audit.Rejected(AuditOutcome.NotFound, "Task", taskId);
                return Problems.NotFound("Task");
            }

            return Results.Ok(TaskDetailDto.From(task, await db.CountCommentsAsync(task.Id, cancellationToken)));
        })
            .WithName("getTask")
            .Produces<TaskDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
