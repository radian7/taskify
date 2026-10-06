using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taskify.Contracts.Hub;
using Taskify.Notifications.Api.Data;
using Taskify.Notifications.Api.Validation;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.RateLimiting;
using Taskify.Security.Users;
using Taskify.Security.Validation;

namespace Taskify.Notifications.Api.Endpoints;

/// <summary>The query string of <c>GET /api/notifications</c>, kept as text so the validator reports bad values.</summary>
/// <param name="Limit">The maximum number of notifications (1 to 100, default 50).</param>
/// <param name="UnreadOnly">Whether to list unread notifications only (default false).</param>
public sealed record ListNotificationsQuery(
    [FromQuery(Name = "limit")] string? Limit,
    [FromQuery(Name = "unreadOnly")] string? UnreadOnly);

/// <summary>The body of the unread-count answer.</summary>
/// <param name="Count">How many notifications are unread.</param>
public sealed record UnreadCountDto(int Count);

/// <summary>The acting user's notifications: list, unread count, mark read (spec FR-027 to FR-030; User Story 6).</summary>
public static class NotificationEndpoints
{
    /// <summary>Maps the routes under <c>/api/notifications</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireCallers(Callers.Web);

        group.MapGet("/", ListAsync)
            .RequireReads()
            .RequireValidation<ListNotificationsQuery>()
            .WithName("listNotifications")
            .Produces<IEnumerable<NotificationDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/unread-count", UnreadCountAsync)
            .RequireReads()
            .WithName("getUnreadCount")
            .Produces<UnreadCountDto>();

        group.MapPost("/{notificationId:guid}/read", MarkReadAsync)
            .RequireWrites()
            .WithName("markRead")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read-all", MarkAllReadAsync)
            .RequireWrites()
            .WithName("markAllRead")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] ListNotificationsQuery query,
        NotificationsDbContext db,
        ICurrentActingUser user,
        CancellationToken cancellationToken)
    {
        // Already validated: the values are absent or well-formed.
        var limit = query.Limit is null
            ? ListNotificationsQueryValidator.DefaultLimit
            : int.Parse(query.Limit, NumberStyles.None, CultureInfo.InvariantCulture);
        var unreadOnly = query.UnreadOnly is not null && bool.Parse(query.UnreadOnly);

        // Only the acting user's own notifications are ever selected (FR-029).
        var notifications = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == user.UserId);
        if (unreadOnly)
        {
            notifications = notifications.Where(n => n.ReadAt == null);
        }

        var items = await notifications
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return Results.Ok(items.Select(n => new NotificationDto(
            n.Id, n.Type, n.TaskId, n.ProjectId, n.ActorUserId, n.Summary, n.CreatedAt, n.ReadAt is not null)));
    }

    private static async Task<IResult> UnreadCountAsync(NotificationsDbContext db, ICurrentActingUser user, CancellationToken cancellationToken) =>
        Results.Ok(new UnreadCountDto(
            await db.Notifications.AsNoTracking().CountAsync(n => n.RecipientUserId == user.UserId && n.ReadAt == null, cancellationToken)));

    private static async Task<IResult> MarkReadAsync(
        Guid notificationId,
        NotificationsDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        // Missing and "belongs to another user" give the same answer, so the existence of a notification is not revealed (FR-029).
        if (notification is null || notification.RecipientUserId != user.UserId)
        {
            audit.Rejected(AuditOutcome.NotFound, "Notification", notificationId);
            return Problems.NotFound("Notification");
        }

        if (notification.ReadAt is null)
        {
            notification.MarkRead(time.GetUtcNowMicroseconds());
            await db.SaveChangesAsync(cancellationToken);
            audit.Changed(AuditActions.NotificationRead, "Notification", notification.Id);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllReadAsync(
        NotificationsDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNowMicroseconds();
        await db.Notifications
            .Where(n => n.RecipientUserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);

        audit.Changed(AuditActions.NotificationsReadAll, "User", user.UserId);
        return Results.NoContent();
    }
}
