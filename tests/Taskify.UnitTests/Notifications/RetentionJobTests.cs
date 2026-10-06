using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Taskify.Contracts;
using Taskify.Notifications.Api.Data;
using Taskify.Notifications.Api.Domain;

namespace Taskify.UnitTests.Notifications;

/// <summary>The 30-day retention of notifications and processed events (spec FR-030; User Story 6).</summary>
public sealed class RetentionJobTests : IDisposable
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider provider;

    public RetentionJobTests()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<NotificationsDbContext>(o => o.UseInMemoryDatabase(databaseName));
        provider = services.BuildServiceProvider();
    }

    public void Dispose() => provider.Dispose();

    private NotificationRetentionJob Job() => new(
        provider.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<NotificationRetentionJob>.Instance);

    private async Task SeedAsync(TimeSpan age)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var at = time.GetUtcNow() - age;
        db.Notifications.Add(new Notification(
            Guid.NewGuid(), SeedIds.Tomasz, NotificationType.TaskMoved, Guid.NewGuid(), Guid.NewGuid(), SeedIds.Priya, "s", at, Guid.NewGuid()));
        db.ProcessedEvents.Add(new ProcessedEvent(Guid.NewGuid(), at));
        await db.SaveChangesAsync();
    }

    private async Task<(int Notifications, int Events)> CountsAsync()
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return (await db.Notifications.CountAsync(), await db.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Rows_older_than_30_days_are_deleted_and_newer_ones_stay()
    {
        await SeedAsync(TimeSpan.FromDays(31));
        await SeedAsync(TimeSpan.FromDays(45));
        await SeedAsync(TimeSpan.FromDays(29));
        await SeedAsync(TimeSpan.Zero);

        var deleted = await Job().RunOnceAsync(CancellationToken.None);

        Assert.Equal((2, 2), deleted);
        Assert.Equal((2, 2), await CountsAsync());
    }

    [Fact]
    public async Task Rows_become_old_enough_as_the_clock_moves_on()
    {
        await SeedAsync(TimeSpan.FromDays(10));
        Assert.Equal((0, 0), await Job().RunOnceAsync(CancellationToken.None));

        time.Advance(TimeSpan.FromDays(21));

        Assert.Equal((1, 1), await Job().RunOnceAsync(CancellationToken.None));
        Assert.Equal((0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_run_with_nothing_old_deletes_nothing()
    {
        await SeedAsync(TimeSpan.FromDays(1));

        Assert.Equal((0, 0), await Job().RunOnceAsync(CancellationToken.None));
        Assert.Equal((1, 1), await CountsAsync());
    }
}
