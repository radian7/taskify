using Taskify.Security.Data;

namespace Taskify.UnitTests.Security;

/// <summary>
/// The least-privilege database role setup (plan: Constitution Check "Least privilege"). The SQL is built from names
/// and a password, so every input must be validated against an allow-list (constitution Principle II).
/// </summary>
public class DatabaseRolesTests
{
    private const string Password = "TestTasksDbPassword0123456789ab";

    private static readonly DatabaseRolePlan Plan = new("tasksdb", "tasks_app", ReadOnlyTables: ["users"], AppendOnlyTables: ["status_changes"]);

    [Fact]
    public void The_role_can_log_in_but_has_no_administrative_rights()
    {
        var statements = DatabaseRoles.BuildStatements(Plan, Password);

        Assert.Contains(statements, s => s.StartsWith("ALTER ROLE \"tasks_app\" WITH LOGIN PASSWORD", StringComparison.Ordinal)
            && s.Contains("NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION", StringComparison.Ordinal));
    }

    [Fact]
    public void The_role_reaches_only_its_own_database()
    {
        var statements = DatabaseRoles.BuildStatements(Plan, Password);

        Assert.Contains("REVOKE ALL ON DATABASE \"tasksdb\" FROM PUBLIC", statements);
        Assert.Contains("GRANT CONNECT ON DATABASE \"tasksdb\" TO \"tasks_app\"", statements);
    }

    [Fact]
    public void Append_only_tables_lose_update_and_delete_after_the_general_grant()
    {
        var statements = DatabaseRoles.BuildStatements(Plan, Password).ToList();

        var grant = statements.FindIndex(s => s.StartsWith("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES", StringComparison.Ordinal));
        var revoke = statements.FindIndex(s => s.Contains("REVOKE UPDATE, DELETE ON TABLE \"status_changes\"", StringComparison.Ordinal));

        Assert.True(grant >= 0 && revoke > grant, "the revoke must come after the broad grant, or it would be undone");
    }

    [Fact]
    public void Read_only_tables_lose_insert_update_and_delete()
    {
        var statements = DatabaseRoles.BuildStatements(Plan, Password);

        Assert.Contains(statements, s => s.Contains("REVOKE INSERT, UPDATE, DELETE ON TABLE \"users\" FROM \"tasks_app\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_migration_history_is_not_the_roles_to_change()
    {
        var statements = DatabaseRoles.BuildStatements(Plan, Password);

        Assert.Contains(statements, s => s.Contains("REVOKE ALL ON TABLE \"__EFMigrationsHistory\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Statements_about_tables_that_a_later_migration_creates_are_conditional()
    {
        var statements = DatabaseRoles.BuildStatements(Plan, Password);

        Assert.All(
            statements.Where(s => s.Contains("status_changes", StringComparison.Ordinal) || s.Contains("\"users\"", StringComparison.Ordinal)),
            s => Assert.Contains("to_regclass", s, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tasksdb; DROP DATABASE tasksdb")]
    [InlineData("Tasks")]
    [InlineData("tasks db")]
    [InlineData("tasks\"db")]
    [InlineData("")]
    [InlineData("1tasks")]
    public void A_database_name_outside_the_allow_list_is_rejected(string database) =>
        Assert.Throws<ArgumentException>(() => DatabaseRoles.BuildStatements(Plan with { Database = database }, Password));

    [Theory]
    [InlineData("tasks_app'; DROP ROLE postgres; --")]
    [InlineData("Tasks_App")]
    [InlineData("")]
    public void A_role_name_outside_the_allow_list_is_rejected(string role) =>
        Assert.Throws<ArgumentException>(() => DatabaseRoles.BuildStatements(Plan with { Role = role }, Password));

    [Theory]
    [InlineData("status_changes\"; DROP TABLE tasks; --")]
    [InlineData("Status")]
    public void A_table_name_outside_the_allow_list_is_rejected(string table) =>
        Assert.Throws<ArgumentException>(() => DatabaseRoles.BuildStatements(Plan with { AppendOnlyTables = [table] }, Password));

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("TestTasksDbPassword0123456789'ab")]
    [InlineData("TestTasksDbPassword0123456789 ab")]
    [InlineData("TestTasksDbPassword0123456789;ab")]
    public void A_weak_or_unsafe_password_is_rejected(string password) =>
        Assert.Throws<ArgumentException>(() => DatabaseRoles.BuildStatements(Plan, password));

    [Fact]
    public void The_password_is_hidden_when_the_statements_are_described_for_a_log()
    {
        var description = DatabaseRoles.Describe(DatabaseRoles.BuildStatements(Plan, Password));

        Assert.DoesNotContain(Password, description, StringComparison.Ordinal);
        Assert.Contains("PASSWORD '***'", description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_running_service_connects_as_its_role_not_the_administrator()
    {
        var connectionString = DatabaseRoles.WithRole("Host=localhost;Port=5432;Username=postgres;Password=admin-secret;Database=tasksdb", "tasks_app", Password);

        Assert.Contains("Username=tasks_app", connectionString, StringComparison.Ordinal);
        Assert.Contains($"Password={Password}", connectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-secret", connectionString, StringComparison.Ordinal);
        Assert.Contains("Database=tasksdb", connectionString, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Require", "SSL Mode=Require")]
    [InlineData("VerifyFull", "SSL Mode=VerifyFull")]
    [InlineData(null, "SSL Mode=Require")]
    public void Tls_is_always_on(string? mode, string expected) =>
        Assert.Contains(expected, DatabaseSetup.WithTls("Host=localhost;Database=x", mode), StringComparison.Ordinal);

    [Fact]
    public void An_unknown_ssl_mode_is_refused_rather_than_silently_downgraded() =>
        Assert.Throws<InvalidOperationException>(() => DatabaseSetup.WithTls("Host=localhost;Database=x", "Disable"));
}
