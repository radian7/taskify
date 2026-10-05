using Microsoft.EntityFrameworkCore;

namespace Taskify.Security.Outbox;

/// <summary>
/// A domain event waiting to be delivered to the Notifications API (research R4: transactional outbox).
/// It is written in the same transaction as the change it describes, so every saved change produces an
/// event even if the Notifications API is down.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Gets or sets the message ID. It is also the event ID the receiver de-duplicates on.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the event type name (see <c>Taskify.Contracts.Events.EventTypes</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the user who made the change. It goes into the event envelope.</summary>
    public Guid ActorUserId { get; set; }

    /// <summary>Gets or sets the event payload as JSON. It never contains descriptions or comment text.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Gets or sets when the change happened, in the same transaction as the change.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets when delivery succeeded. <see langword="null"/> while the event is pending.</summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    /// <summary>Gets or sets the number of failed delivery attempts. Backoff is capped at one minute.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets when the receiver rejected the event permanently (400 or 403). A dead-lettered event is never
    /// retried; it is audited and counted because it means a live update or notification was missed.
    /// </summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }
}

/// <summary>EF Core mapping for <see cref="OutboxMessage"/>.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>Maps <see cref="OutboxMessage"/> to the <c>outbox_messages</c> table. Call from <c>OnModelCreating</c>.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ModelBuilder ConfigureOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Type).HasMaxLength(64).IsRequired();
            entity.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();

            // The dispatcher only ever reads pending rows, oldest first.
            entity.HasIndex(m => m.OccurredAt)
                .HasFilter("\"DispatchedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
        });

        return modelBuilder;
    }
}
