using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskify.Contracts;

namespace Taskify.Security.Outbox;

/// <summary>Stages domain events in the outbox (research R4).</summary>
public static class OutboxWriter
{
    /// <summary>
    /// Adds an event to the current change tracker. It is saved by the same <c>SaveChanges</c> call, and so in the
    /// same transaction, as the data change it describes. Call it before saving, never after.
    /// </summary>
    /// <typeparam name="TPayload">One of the payload records in <c>Taskify.Contracts.Events</c>.</typeparam>
    /// <param name="db">The service's database context.</param>
    /// <param name="type">The event type, from <c>EventTypes</c>.</param>
    /// <param name="actorUserId">The user who made the change.</param>
    /// <param name="payload">The event payload. It must not contain descriptions or comment text.</param>
    /// <param name="now">The time of the change (UTC).</param>
    /// <returns>The staged message.</returns>
    public static OutboxMessage Add<TPayload>(DbContext db, string type, Guid actorUserId, TPayload payload, DateTimeOffset now)
        where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            ActorUserId = actorUserId,
            Payload = JsonSerializer.Serialize(payload, ContractJson.Options),
            OccurredAt = now,
        };
        db.Set<OutboxMessage>().Add(message);
        return message;
    }
}
