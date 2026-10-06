using Microsoft.EntityFrameworkCore;
using Taskify.Notifications.Api.Domain;

namespace Taskify.Notifications.Api.Data;

/// <summary>
/// Deletes notifications 30 days after they were created (spec FR-030) and processed-event records 30 days after they
/// were received (data-model.md), once a day. The outbox stops retrying an event long before 30 days, so a deleted
/// <see cref="ProcessedEvent"/> is never needed again.
/// </summary>
/// <param name="scopes">Creates a scope (and database context) for each run.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The log sink. It receives counts only, never notification text.</param>
public sealed class NotificationRetentionJob(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<NotificationRetentionJob> logger) : BackgroundService
{
    private const int BatchSize = 500;

    /// <summary>How long rows are kept (FR-030).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>How long to wait between runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Deletes everything older than the retention period.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The number of notifications and processed events deleted.</returns>
    public async Task<(int Notifications, int ProcessedEvents)> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - Retention;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var notifications = 0;
        while (true)
        {
            var old = await db.Notifications.Where(n => n.CreatedAt < cutoff).Take(BatchSize).ToListAsync(cancellationToken);
            if (old.Count == 0)
            {
                break;
            }

            db.Notifications.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
            notifications += old.Count;
        }

        var processed = 0;
        while (true)
        {
            var old = await db.ProcessedEvents.Where(e => e.ReceivedAt < cutoff).Take(BatchSize).ToListAsync(cancellationToken);
            if (old.Count == 0)
            {
                break;
            }

            db.ProcessedEvents.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
            processed += old.Count;
        }

        return (notifications, processed);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                var (notifications, processed) = await RunOnceAsync(stoppingToken);
                logger.LogInformation("Retention removed {Notifications} notifications and {ProcessedEvents} processed events.", notifications, processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed run is retried the next day; the service keeps running.
                logger.LogError(ex, "The notification retention run failed.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
