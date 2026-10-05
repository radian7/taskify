using System.Text.Json;

namespace Taskify.Contracts.Events;

/// <summary>
/// The envelope of a domain event as delivered to the Notifications API (contracts/events.asyncapi.yaml,
/// schema <c>EnvelopeBase</c>). A breaking change creates a new <see cref="Version"/> (Principle III).
/// </summary>
/// <param name="EventId">The outbox message ID. The receiver de-duplicates on it.</param>
/// <param name="Type">One of <see cref="EventTypes"/>.</param>
/// <param name="Version">The contract version; always <see cref="CurrentVersion"/> for this contract.</param>
/// <param name="OccurredAt">When the change happened (UTC).</param>
/// <param name="ActorUserId">The user who made the change.</param>
/// <param name="Payload">The type-specific body; see the <c>*Payload</c> records.</param>
public sealed record EventEnvelope(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid ActorUserId,
    JsonElement Payload)
{
    /// <summary>The only supported contract version.</summary>
    public const int CurrentVersion = 1;
}
