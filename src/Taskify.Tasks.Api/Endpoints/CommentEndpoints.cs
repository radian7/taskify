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
using Taskify.Tasks.Api.Domain;

namespace Taskify.Tasks.Api.Endpoints;

/// <summary>Reading, posting, editing and deleting comments (spec FR-015 to FR-017, FR-024; User Story 4).</summary>
public static class CommentEndpoints
{
    /// <summary>Maps the comment routes under <c>/api/tasks/{taskId}/comments</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapCommentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks/{taskId:guid}/comments").RequireCallers(Callers.Web);

        group.MapGet("/", ListAsync)
            .RequireReads()
            .WithName("listComments")
            .Produces<IEnumerable<CommentDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", AddAsync)
            .RequireWrites()
            .RequireValidation<CommentTextRequest>()
            .WithName("addComment")
            .Produces<CommentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{commentId:guid}", EditAsync)
            .RequireWrites()
            .RequireValidation<CommentTextRequest>()
            .WithName("editComment")
            .Produces<CommentDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{commentId:guid}", DeleteAsync)
            .RequireWrites()
            .WithName("deleteComment")
            .Produces<CommentDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid taskId, TasksDbContext db, IAuditLogger audit, CancellationToken cancellationToken)
    {
        if (!await db.Tasks.AsNoTracking().AnyAsync(t => t.Id == taskId, cancellationToken))
        {
            audit.Rejected(AuditOutcome.NotFound, "Task", taskId);
            return Problems.NotFound("Task");
        }

        // Oldest first, deleted comments included as placeholders (spec FR-016, FR-024). IDs are time-ordered and break ties.
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
        return Results.Ok(comments.Select(CommentDto.From));
    }

    /// <summary>
    /// Adds a comment as the acting user (spec FR-015). Comments are allowed on a task in any column, including Done
    /// (clarification Q5). The comment and its event are saved together or not at all (research R4).
    /// </summary>
    /// <param name="taskId">The task.</param>
    /// <param name="request">The comment text (already validated).</param>
    /// <param name="db">The tasks database.</param>
    /// <param name="user">The acting user, recorded as the author.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>201 with the comment, or 404.</returns>
    public static async Task<IResult> AddAsync(
        Guid taskId,
        CommentTextRequest request,
        TasksDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            audit.Rejected(AuditOutcome.NotFound, "Task", taskId);
            return Problems.NotFound("Task");
        }

        var now = time.GetUtcNowMicroseconds();
        var comment = new Comment(Guid.CreateVersion7(), task.Id, user.UserId, InputNormalizer.Trim(request.Text), now);

        db.Comments.Add(comment);
        // The event carries IDs and the task title, never the comment text (spec FR-022).
        OutboxWriter.Add(
            db,
            EventTypes.CommentAdded,
            user.UserId,
            new CommentAddedPayload(task.Id, task.ProjectId, task.Title, comment.Id, task.AssigneeUserId),
            now);
        await db.SaveChangesAsync(cancellationToken);

        audit.Changed(AuditActions.CommentAdded, "Comment", comment.Id);
        return Results.Created($"/api/tasks/{taskId}/comments/{comment.Id}", CommentDto.From(comment));
    }

    /// <summary>Edits the acting user's own comment (spec FR-017): 403 for anyone else, 409 once it is deleted.</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="commentId">The comment.</param>
    /// <param name="request">The new text (already validated).</param>
    /// <param name="db">The tasks database.</param>
    /// <param name="user">The acting user.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change or the refusal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The comment after the edit, 403, 404 or 409.</returns>
    public static Task<IResult> EditAsync(
        Guid taskId,
        Guid commentId,
        CommentTextRequest request,
        TasksDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            taskId,
            commentId,
            db,
            user,
            time,
            audit,
            AuditActions.CommentEdited,
            EventTypes.CommentEdited,
            (comment, now) => comment.Edit(InputNormalizer.Trim(request.Text), user.UserId, now),
            cancellationToken);

    /// <summary>
    /// Deletes the acting user's own comment (spec FR-017, FR-024): the text is erased for good and a placeholder remains.
    /// 403 for anyone else, 409 if it is already deleted.
    /// </summary>
    /// <param name="taskId">The task.</param>
    /// <param name="commentId">The comment.</param>
    /// <param name="db">The tasks database.</param>
    /// <param name="user">The acting user.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change or the refusal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The placeholder comment, 403, 404 or 409.</returns>
    public static Task<IResult> DeleteAsync(
        Guid taskId,
        Guid commentId,
        TasksDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            taskId,
            commentId,
            db,
            user,
            time,
            audit,
            AuditActions.CommentDeleted,
            EventTypes.CommentDeleted,
            (comment, now) => comment.Delete(user.UserId, now),
            cancellationToken);

    private static async Task<IResult> ChangeAsync(
        Guid taskId,
        Guid commentId,
        TasksDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        string auditAction,
        string eventType,
        Func<Comment, DateTimeOffset, CommentChange> apply,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Locked, so an edit and a delete that arrive together cannot bring a deleted comment back (spec FR-024).
        var comment = await db.LoadCommentForUpdateAsync(commentId, cancellationToken);
        if (comment is null || comment.TaskId != taskId)
        {
            audit.Rejected(AuditOutcome.NotFound, "Comment", commentId);
            return Problems.NotFound("Comment");
        }

        var now = time.GetUtcNowMicroseconds();
        var change = apply(comment, now);

        switch (change.Outcome)
        {
            case CommentOutcome.Forbidden:
                // Server-side ownership check (spec FR-017): the UI hides the buttons, but this is the real control.
                audit.Rejected(AuditOutcome.Forbidden, "Comment", commentId);
                return Problems.Forbidden("Only the author can change this comment.");

            case CommentOutcome.AlreadyDeleted:
                audit.Rejected(AuditOutcome.Conflict, "Comment", commentId);
                return Problems.Conflict("This comment was deleted and can no longer be changed.");
        }

        if (change.Changed)
        {
            var task = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId, cancellationToken);
            object payload = eventType == EventTypes.CommentEdited
                ? new CommentEditedPayload(task.Id, task.ProjectId, comment.Id)
                : new CommentDeletedPayload(task.Id, task.ProjectId, comment.Id);
            OutboxWriter.Add(db, eventType, user.UserId, payload, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (change.Changed)
        {
            audit.Changed(auditAction, "Comment", comment.Id);
        }

        return Results.Ok(CommentDto.From(comment));
    }
}
