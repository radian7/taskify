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
    /// <summary>Gets the notifications.</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

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

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Id).ValueGeneratedNever();
            entity.Property(n => n.Type).HasConversion<string>().HasMaxLength(32);
            // The summary is at most 300 user-perceived characters; the column leaves room for the 16-units-per-character guard.
            entity.Property(n => n.Summary).HasMaxLength(4800);
            // "sourceEventId: UUID, unique": one notification per event, so redelivery creates nothing new.
            entity.HasIndex(n => n.SourceEventId).IsUnique();
            // The list and the unread count read one recipient's newest notifications.
            entity.HasIndex(n => new { n.RecipientUserId, n.ReadAt, n.CreatedAt })
                .IsDescending(false, false, true);
        });
    }
}
