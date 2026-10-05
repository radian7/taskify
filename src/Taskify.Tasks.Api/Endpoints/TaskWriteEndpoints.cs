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
using Taskify.Tasks.Api.Clients;
using Taskify.Tasks.Api.Data;
using Taskify.Tasks.Api.Domain;

namespace Taskify.Tasks.Api.Endpoints;

/// <summary>Creating and editing tasks (spec FR-009 to FR-011; User Story 3).</summary>
public static class TaskWriteEndpoints
{
    /// <summary>Maps <c>POST /api/tasks</c> and <c>PUT /api/tasks/{taskId}</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapTaskWriteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").RequireCallers(Callers.Web).RequireWrites();

        group.MapPost("/", CreateAsync)
            .RequireValidation<CreateTaskRequest>()
            .WithName("createTask")
            .Produces<TaskDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{taskId:guid}", UpdateAsync)
            .RequireValidation<UpdateTaskRequest>()
            .WithName("updateTask")
            .Produces<TaskDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>
    /// Creates a task in the To Do column (spec FR-009). The project must exist and the assignee, if any, must be one of
    /// the five users; otherwise the request is rejected with 422 and nothing is saved (spec FR-021).
    /// </summary>
    /// <param name="request">The new task (already validated).</param>
    /// <param name="db">The tasks database.</param>
    /// <param name="projects">Asks the Projects API whether the project exists.</param>
    /// <param name="users">The directory of predefined users.</param>
    /// <param name="user">The acting user, recorded as the creator.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change or the refusal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>201 with the new task, or 422.</returns>
    public static async Task<IResult> CreateAsync(
        CreateTaskRequest request,
        TasksDbContext db,
        ProjectsApiClient projects,
        IUserDirectory users,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        if (!await projects.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            audit.Rejected(AuditOutcome.UnknownReference, "Project", request.ProjectId);
            return Problems.UnknownReference("The project does not exist.");
        }

        if (request.AssigneeUserId is { } assignee && !await users.ExistsAsync(assignee, cancellationToken))
        {
            audit.Rejected(AuditOutcome.UnknownReference, "User", assignee);
            return Problems.UnknownReference("The assignee is not one of the predefined users.");
        }

        var now = time.GetUtcNowMicroseconds();
        var task = new TaskItem(
            Guid.CreateVersion7(),
            request.ProjectId,
            InputNormalizer.Trim(request.Title),
            InputNormalizer.TrimToNull(request.Description),
            TaskStatus.ToDo,
            request.AssigneeUserId,
            user.UserId,
            now);

        // The task and its event are saved together or not at all (research R4).
        db.Tasks.Add(task);
        OutboxWriter.Add(
            db,
            EventTypes.TaskCreated,
            user.UserId,
            new TaskCreatedPayload(task.Id, task.ProjectId, task.Title, task.Status, task.AssigneeUserId),
            now);
        await db.SaveChangesAsync(cancellationToken);

        audit.Changed(AuditActions.TaskCreated, "Task", task.Id);
        return Results.Created($"/api/tasks/{task.Id}", TaskDetailDto.From(task, commentCount: 0));
    }

    /// <summary>
    /// Replaces a task's title, description and assignee together (spec FR-011). When two edits arrive at nearly the same
    /// time the last one to commit wins as a whole (research R14); the row is locked while one is applied. It works for
    /// a task in any column, including Done.
    /// </summary>
    /// <param name="taskId">The task to edit.</param>
    /// <param name="request">The new values (already validated).</param>
    /// <param name="db">The tasks database.</param>
    /// <param name="users">The directory of predefined users.</param>
    /// <param name="user">The acting user.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change or the refusal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The task after the edit, 404, or 422.</returns>
    public static async Task<IResult> UpdateAsync(
        Guid taskId,
        UpdateTaskRequest request,
        TasksDbContext db,
        IUserDirectory users,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        // Checked before the row is locked, so a slow call to the Projects API never holds a lock.
        if (request.AssigneeUserId is { } assignee && !await users.ExistsAsync(assignee, cancellationToken))
        {
            audit.Rejected(AuditOutcome.UnknownReference, "User", assignee);
            return Problems.UnknownReference("The assignee is not one of the predefined users.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var task = await db.LoadForUpdateAsync(taskId, cancellationToken);
        if (task is null)
        {
            audit.Rejected(AuditOutcome.NotFound, "Task", taskId);
            return Problems.NotFound("Task");
        }

        var now = time.GetUtcNowMicroseconds();
        var change = task.Update(
            InputNormalizer.Trim(request.Title),
            InputNormalizer.TrimToNull(request.Description),
            request.AssigneeUserId,
            now);

        if (change.Any)
        {
            // Write all three fields every time, so the last save replaces the task as a unit instead of merging with
            // another edit that touched different fields (spec FR-011).
            var entry = db.Entry(task);
            entry.Property(t => t.Title).IsModified = true;
            entry.Property(t => t.Description).IsModified = true;
            entry.Property(t => t.AssigneeUserId).IsModified = true;

            if (change.TextChanged)
            {
                OutboxWriter.Add(db, EventTypes.TaskUpdated, user.UserId, new TaskUpdatedPayload(task.Id, task.ProjectId, task.Title), now);
            }

            if (change.AssigneeChanged)
            {
                OutboxWriter.Add(
                    db,
                    EventTypes.TaskAssigned,
                    user.UserId,
                    new TaskAssignedPayload(task.Id, task.ProjectId, task.Title, change.PreviousAssigneeUserId, task.AssigneeUserId),
                    now);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (change.TextChanged)
        {
            audit.Changed(AuditActions.TaskUpdated, "Task", task.Id);
        }

        if (change.AssigneeChanged)
        {
            audit.Changed(AuditActions.TaskAssigned, "Task", task.Id);
        }

        return Results.Ok(TaskDetailDto.From(task, await db.CountCommentsAsync(task.Id, cancellationToken)));
    }
}
