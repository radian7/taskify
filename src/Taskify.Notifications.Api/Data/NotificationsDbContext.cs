using Microsoft.EntityFrameworkCore;

namespace Taskify.Notifications.Api.Data;

/// <summary>
/// The Notifications service database (<c>notificationsdb</c>): notifications and processed-event records.
/// Only this service reads or writes it (constitution Principle III). The Notifications API publishes no events,
/// so it has no outbox.
/// </summary>
/// <param name="options">The context options.</param>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // ProcessedEvent (US5) and Notification (US6) are added by the stories that need them.
    }
}
