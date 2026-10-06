namespace Taskify.Notifications.Api.Domain;

/// <summary>
/// Records that an event was already handled, so a redelivery from the outbox (at-least-once delivery, research R5)
/// changes nothing a second time. The row is inserted in the same transaction as the handlers' effects.
/// </summary>
public sealed class ProcessedEvent
{
    // For EF Core only.
    private ProcessedEvent()
    {
    }

    /// <summary>Creates a record for an event that is being handled now.</summary>
    /// <param name="eventId">The <c>eventId</c> from the envelope.</param>
    /// <param name="receivedAt">When this service received it (set by the server, never taken from the request).</param>
    public ProcessedEvent(Guid eventId, DateTimeOffset receivedAt)
    {
        EventId = eventId;
        ReceivedAt = receivedAt;
    }

    /// <summary>Gets the <c>eventId</c> from the envelope; the primary key.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Gets when the event was received (UTC, set by the server).</summary>
    public DateTimeOffset ReceivedAt { get; private set; }
}
