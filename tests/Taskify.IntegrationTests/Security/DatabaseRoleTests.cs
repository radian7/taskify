using Npgsql;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Security;

/// <summary>
/// Each service runs as its own least-privilege database role (constitution Principle I; plan: Constitution Check
/// "Least privilege"): it reaches only its own database and can change only what it is meant to change.
/// </summary>
/// <param name="app">The running application.</param>
public class DatabaseRoleTests(TaskifyAppFixture app)
{
    private async Task<NpgsqlConnection> OpenAsync(string database, string? role = null, string? password = null)
    {
        var connection = new NpgsqlConnection(await app.GetConnectionStringAsync(database, role, password));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task<PostgresException> FailureOfAsync(NpgsqlConnection connection, string statement)
    {
#pragma warning disable CA2100 // constant statements in a test
        await using var command = new NpgsqlCommand(statement, connection);
#pragma warning restore CA2100
        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_service_session_runs_as_that_services_own_role_never_as_the_administrator()
    {
        await using var admin = await OpenAsync("projectsdb");
        await using var command = new NpgsqlCommand(
            "SELECT datname, usename FROM pg_stat_activity WHERE datname IN ('projectsdb','tasksdb','notificationsdb') AND application_name LIKE 'Taskify.%'", admin);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var sessions = new List<(string Database, string User)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            sessions.Add((reader.GetString(0), reader.GetString(1)));
        }

        // Services tag their sessions with their application name ("Taskify.Projects.Api" and so on); the AppHost's
        // own health probe and the tests' sessions are not services and are left out by the query.
        Assert.Contains(("projectsdb", "projects_app"), sessions);
        Assert.Contains(("tasksdb", "tasks_app"), sessions);
        Assert.All(sessions.Where(s => s.Database == "projectsdb"), s => Assert.Equal("projects_app", s.User));
        Assert.All(sessions.Where(s => s.Database == "tasksdb"), s => Assert.Equal("tasks_app", s.User));
        Assert.All(sessions.Where(s => s.Database == "notificationsdb"), s => Assert.Equal("notifications_app", s.User));
    }

    [Fact]
    public async Task The_service_roles_have_no_administrative_rights()
    {
        await using var admin = await OpenAsync("projectsdb");
        await using var command = new NpgsqlCommand(
            "SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolcanlogin FROM pg_roles WHERE rolname LIKE '%\\_app' ORDER BY rolname", admin);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var roles = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            roles.Add(reader.GetString(0));
            Assert.False(reader.GetBoolean(1), $"{reader.GetString(0)} is a superuser");
            Assert.False(reader.GetBoolean(2), $"{reader.GetString(0)} can create databases");
            Assert.False(reader.GetBoolean(3), $"{reader.GetString(0)} can create roles");
            Assert.False(reader.GetBoolean(4), $"{reader.GetString(0)} can replicate");
            Assert.True(reader.GetBoolean(5));
        }

        Assert.Equal(["notifications_app", "projects_app", "tasks_app"], roles);
    }

    [Theory]
    [InlineData("tasksdb", "tasks_app", TaskifyAppFixture.TasksDbPassword, "projectsdb")]
    [InlineData("tasksdb", "tasks_app", TaskifyAppFixture.TasksDbPassword, "notificationsdb")]
    [InlineData("projectsdb", "projects_app", TaskifyAppFixture.ProjectsDbPassword, "tasksdb")]
    [InlineData("notificationsdb", "notifications_app", TaskifyAppFixture.NotificationsDbPassword, "tasksdb")]
    public async Task A_service_role_cannot_even_connect_to_another_services_database(string ownDatabase, string role, string password, string otherDatabase)
    {
        // Prove the credentials are good for the role's own database, then try another one with the same credentials.
        await using (await OpenAsync(ownDatabase, role, password))
        {
        }

        var otherConnectionString = new NpgsqlConnectionStringBuilder(await app.GetConnectionStringAsync(otherDatabase, role, password));
        await using var other = new NpgsqlConnection(otherConnectionString.ConnectionString);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => other.OpenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("42501", failure.SqlState);
    }

    [Fact]
    public async Task The_projects_role_cannot_create_change_or_delete_users()
    {
        await using var connection = await OpenAsync("projectsdb", "projects_app", TaskifyAppFixture.ProjectsDbPassword);

        await using (var read = new NpgsqlCommand("SELECT count(*) FROM users", connection))
        {
            Assert.Equal(5L, await read.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }

        foreach (var statement in new[]
        {
            "INSERT INTO users (\"Id\", \"DisplayName\", \"Role\") VALUES (gen_random_uuid(), 'Intruder', 'Engineer')",
            "UPDATE users SET \"DisplayName\" = 'Renamed'",
            "DELETE FROM users",
        })
        {
            Assert.Equal("42501", (await FailureOfAsync(connection, statement)).SqlState);
        }
    }

    [Fact]
    public async Task A_service_role_cannot_tamper_with_the_migration_history_or_create_tables()
    {
        await using var connection = await OpenAsync("tasksdb", "tasks_app", TaskifyAppFixture.TasksDbPassword);

        Assert.Equal("42501", (await FailureOfAsync(connection, "DELETE FROM \"__EFMigrationsHistory\"")).SqlState);
        Assert.Equal("42501", (await FailureOfAsync(connection, "CREATE TABLE sneaky (id int)")).SqlState);
        Assert.Equal("42501", (await FailureOfAsync(connection, "DROP TABLE tasks")).SqlState);
    }

    [Fact]
    public async Task The_tasks_role_can_still_do_its_normal_work()
    {
        await using var connection = await OpenAsync("tasksdb", "tasks_app", TaskifyAppFixture.TasksDbPassword);

        await using var read = new NpgsqlCommand("SELECT count(*) FROM tasks", connection);
        Assert.True((long)(await read.ExecuteScalarAsync(TestContext.Current.CancellationToken))! >= 30);

        // A harmless real change: an update that sets a column to its current value.
        await using var update = new NpgsqlCommand("UPDATE tasks SET \"Title\" = \"Title\" WHERE false", connection);
        Assert.Equal(0, await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }
}
