using Microsoft.EntityFrameworkCore;
using Taskify.Contracts.Events;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.Outbox;
using Taskify.Security.RateLimiting;
using Taskify.Security.Users;
using Taskify.Security.Validation;
using Taskify.Tasks.Api.Data;

namespace Taskify.Tasks.Api.Endpoints;

/// <summary>Moving tasks between columns and reading their history (spec FR-012, FR-023; User Story 2).</summary>
public static class MoveEndpoints
{
    /// <summary>Maps <c>POST /api/tasks/{taskId}/moves</c> and <c>GET /api/tasks/{taskId}/history</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapMoveEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").RequireCallers(Callers.Web);

        group.MapPost("/{taskId:guid}/moves", MoveAsync)
            .RequireWrites()
            .RequireValidation<MoveTaskRequest>()
            .WithName("moveTask")
            .Produces<MoveTaskResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // There is deliberately no PUT, PATCH or DELETE: the history cannot be edited (spec FR-023).
        group.MapGet("/{taskId:guid}/history", HistoryAsync)
            .RequireReads()
            .WithName("getTaskHistory")
            .Produces<IEnumerable<StatusChangeDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Moves a task. Any user may move any task from any column to any other (FR-012). When two moves of the same
    /// task arrive together the last one to commit wins (FR-011); the row is locked for the duration so that the
    /// history stays a chain in which each entry starts where the previous one ended.
    /// </summary>
    /// <param name="taskId">The task to move.</param>
    /// <param name="request">The target column (already validated).</param>
    /// <param name="db">The tasks database.</param>
    /// <param name="user">The acting user.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change or the refusal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The task after the move, with the history entry, or 404.</returns>
    public static async Task<IResult> MoveAsync(
        Guid taskId,
        MoveTaskRequest request,
        TasksDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var task = await db.LoadForUpdateAsync(taskId, cancellationToken);

        if (task is null)
        {
            audit.Rejected(AuditOutcome.NotFound, "Task", taskId);
            return Problems.NotFound("Task");
        }

        var now = time.GetUtcNowMicroseconds();
        var change = task.MoveTo(request.ToStatus, user.UserId, now);

        if (change is not null)
        {
            // The data change, its history entry and its event are saved together or not at all (research R4).
            db.StatusChanges.Add(change);
            OutboxWriter.Add(
                db,
                EventTypes.TaskMoved,
                user.UserId,
                new TaskMovedPayload(task.Id, task.ProjectId, task.Title, change.FromStatus, change.ToStatus, task.AssigneeUserId),
                now);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (change is not null)
        {
            // Identifiers and statuses only; never titles or text (spec FR-022).
            audit.Changed(AuditActions.TaskMoved, "Task", task.Id);
        }

        return Results.Ok(new MoveTaskResult(
            Changed: change is not null,
            TaskDetailDto.From(task, await db.CountCommentsAsync(task.Id, cancellationToken)),
            change is null ? null : StatusChangeDto.From(change)));
    }

    private static async Task<IResult> HistoryAsync(Guid taskId, TasksDbContext db, IAuditLogger audit, CancellationToken cancellationToken)
    {
        if (!await db.Tasks.AsNoTracking().AnyAsync(t => t.Id == taskId, cancellationToken))
        {
            audit.Rejected(AuditOutcome.NotFound, "Task", taskId);
            return Problems.NotFound("Task");
        }

        // Newest first. IDs are time-ordered, so they break ties between moves in the same instant.
        var history = await db.StatusChanges.AsNoTracking()
            .Where(s => s.TaskId == taskId)
            .OrderByDescending(s => s.MovedAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync(cancellationToken);
        return Results.Ok(history.Select(StatusChangeDto.From));
    }
}
