using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Taskify.Tasks.Api.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the context without starting the app or Aspire. It never connects
/// to a database, and the connection string below contains no real credentials.
/// </summary>
public sealed class TasksDesignTimeFactory : IDesignTimeDbContextFactory<TasksDbContext>
{
    /// <inheritdoc />
    public TasksDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only")
            .Options);
}
