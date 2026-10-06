using Microsoft.EntityFrameworkCore;
using Taskify.Notifications.Api.Domain;

namespace Taskify.Notifications.Api.Data;

/// <summary>
/// The Notifications service database (<c>notificationsdb</c>): notifications and processed-event records.
/// Only this service reads or writes it (constitution Principle III). The Notifications API publishes no events,
/// so it has no outbox.
/// </summary>
/// <param name="options">The context options.</param>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    /// <summary>Gets the events already handled, for de-duplication.</summary>
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");
            // "eventId: UUID, primary key, from the envelope": a duplicate insert is how a redelivery is detected.
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).ValueGeneratedNever();
        });

        // Notification (US6) is added by the story that needs it.
    }
}
