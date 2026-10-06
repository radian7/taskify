using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskify.Contracts.Events;
using Taskify.Notifications.Api.Data;
using Taskify.Notifications.Api.Domain;
using Taskify.Notifications.Api.Hubs;
using Taskify.Notifications.Api.Security;
using Taskify.Notifications.Api.Validation;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.RateLimiting;
using Taskify.Security.Users;

namespace Taskify.Notifications.Api.Endpoints;

/// <summary>The event intake used by the Projects and Tasks outbox dispatchers (research R5; User Story 5).</summary>
public static class InternalEventEndpoints
{
    // An event is a few hundred bytes; anything near this size is not one of ours.
    private const int MaxBodyBytes = 64 * 1024;

    /// <summary>Maps <c>POST /internal/events</c>.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapInternalEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/events", ReceiveAsync)
            .RequireCallers(Callers.Projects, Callers.Tasks)
            .AllowAnonymousActingUser()
            .RequireInternalEventLimit()
            .WithName("receiveEvent")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Validates, de-duplicates and handles one event. Redelivery is normal (at-least-once, R5), so a known
    /// <c>eventId</c> answers <c>202</c> and changes nothing.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="validator">Validates the envelope and payload.</param>
    /// <param name="db">The notifications database.</param>
    /// <param name="users">The user directory, for the actor's display name in summaries.</param>
    /// <param name="broadcaster">Sends the hub signals.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the event or the refusal.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>202, or a Problem Details response.</returns>
    public static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        EventEnvelopeValidator validator,
        NotificationsDbContext db,
        IUserDirectory users,
        RealtimeBroadcaster broadcaster,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var json = await ReadBodyAsync(request.Body, cancellationToken);
        if (json is null)
        {
            audit.Rejected(AuditOutcome.TooLarge);
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Payload too large");
        }

        var result = await validator.ValidateAsync(json, cancellationToken);
        if (!result.IsValid)
        {
            audit.Rejected(AuditOutcome.Validation);
            var errors = result.Errors
                .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);
            return Problems.Validation(errors);
        }

        var envelope = Parse(json);

        // The key matrix (R8): a valid key for the wrong service must not forge another service's events.
        if (!EventCallerPolicy.IsAllowed(request.HttpContext.GetCaller(), envelope.Type))
        {
            audit.Rejected(AuditOutcome.Forbidden);
            return Problems.Forbidden("This caller may not send this event type.");
        }

        // The directory is cached and read outside the transaction; it only supplies the name used in the summary.
        var actorName = (await users.GetAllAsync(cancellationToken)).FirstOrDefault(u => u.Id == envelope.ActorUserId)?.DisplayName
            ?? "Someone";

        Notification? notification = null;
        var firstDelivery = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            notification = null;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            if (await db.ProcessedEvents.AnyAsync(e => e.EventId == envelope.EventId, cancellationToken))
            {
                return false;
            }

            db.ProcessedEvents.Add(new ProcessedEvent(envelope.EventId, time.GetUtcNow()));

            // Notification creation runs in the same transaction as the ProcessedEvent row (spec FR-027, SC-009).
            if (NotificationTriggerRules.Evaluate(envelope, actorName) is { } draft)
            {
                notification = new Notification(
                    Guid.CreateVersion7(), draft.RecipientUserId, draft.Type, draft.TaskId, draft.ProjectId,
                    envelope.ActorUserId, draft.Summary, time.GetUtcNowMicroseconds(), envelope.EventId);
                db.Notifications.Add(notification);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two deliveries of the same event may have raced; if the other one won, this is a duplicate.
                if (await IsDuplicateAsync(db, envelope.EventId, cancellationToken))
                {
                    return false;
                }

                throw;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        });

        if (!firstDelivery)
        {
            return Results.Accepted();
        }

        audit.Log(new AuditEntry(AuditActions.EventReceived, AuditOutcome.Succeeded) { ActingUserId = envelope.ActorUserId });

        // After the commit: signals are only hints (R5), the saved state is the truth.
        await broadcaster.BroadcastAsync(envelope, cancellationToken);
        if (notification is not null)
        {
            await broadcaster.NotifyAsync(notification, cancellationToken);
        }

        return Results.Accepted();
    }

    private static async Task<bool> IsDuplicateAsync(NotificationsDbContext db, Guid eventId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await db.ProcessedEvents.AsNoTracking().AnyAsync(e => e.EventId == eventId, cancellationToken);
    }

    private static async Task<string?> ReadBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > MaxBodyBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static EventEnvelope Parse(string json)
    {
        // Already validated: every field is present and well-typed.
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new EventEnvelope(
            root.GetProperty("eventId").GetGuid(),
            root.GetProperty("type").GetString()!,
            root.GetProperty("version").GetInt32(),
            root.GetProperty("occurredAt").GetDateTimeOffset(),
            root.GetProperty("actorUserId").GetGuid(),
            root.GetProperty("payload").Clone());
    }
}
