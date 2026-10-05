using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Taskify.Notifications.Api.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the context without starting the app or Aspire. It never connects
/// to a database, and the connection string below contains no real credentials.
/// </summary>
public sealed class NotificationsDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    /// <inheritdoc />
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only")
            .Options);
}
