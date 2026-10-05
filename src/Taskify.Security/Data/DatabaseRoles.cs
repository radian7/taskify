using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Taskify.Security.Data;

/// <summary>
/// Describes the least-privilege PostgreSQL role one service runs as (constitution Principle I; plan: Constitution
/// Check "Least privilege"). The service migrates its database as the administrator, then runs as this role, which can
/// reach only its own database and can change only the tables it is meant to change.
/// </summary>
/// <param name="Database">The service's database, for example <c>tasksdb</c>.</param>
/// <param name="Role">The role name, for example <c>tasks_app</c>.</param>
/// <param name="ReadOnlyTables">Tables the role may only read (no INSERT, UPDATE or DELETE): fixed seed data.</param>
/// <param name="AppendOnlyTables">Tables the role may read and insert into but never change or delete: history.</param>
public sealed record DatabaseRolePlan(
    string Database,
    string Role,
    IReadOnlyList<string> ReadOnlyTables,
    IReadOnlyList<string> AppendOnlyTables)
{
    /// <summary>The configuration key holding the role's login name.</summary>
    public const string RoleKey = "Taskify:Database:AppRole";

    /// <summary>The configuration key holding the role's password (an Aspire secret parameter).</summary>
    public const string PasswordKey = "Taskify:Database:AppPassword";
}

/// <summary>
/// Creates and secures the service's database role. The statements are built from names that are validated against a
/// strict allow-list and a password that must be alphanumeric, because DDL cannot take parameters (constitution
/// Principle II). All of it is idempotent and runs on every start, after the migrations.
/// </summary>
public static partial class DatabaseRoles
{
    private const int MinimumPasswordLength = 24;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9]{24,128}$")]
    private static partial Regex PasswordPattern();

    /// <summary>Builds the SQL that creates the role and grants exactly what the plan allows.</summary>
    /// <param name="plan">The role plan.</param>
    /// <param name="password">The role's password. Alphanumeric, at least 24 characters.</param>
    /// <returns>The statements, in the order to run them.</returns>
    /// <exception cref="ArgumentException">A name or the password is not acceptable.</exception>
    public static IReadOnlyList<string> BuildStatements(DatabaseRolePlan plan, string password)
    {
        ArgumentNullException.ThrowIfNull(plan);

        RequireIdentifier(plan.Database, nameof(plan.Database));
        RequireIdentifier(plan.Role, nameof(plan.Role));
        foreach (var table in plan.ReadOnlyTables.Concat(plan.AppendOnlyTables))
        {
            RequireIdentifier(table, "table");
        }

        if (!PasswordPattern().IsMatch(password ?? string.Empty))
        {
            throw new ArgumentException($"The database role password must be alphanumeric and at least {MinimumPasswordLength} characters.", nameof(password));
        }

        var role = $"\"{plan.Role}\"";
        var database = $"\"{plan.Database}\"";
        var statements = new List<string>
        {
            // Create the role once, then (re)set its login and password so a rotated secret takes effect.
            $"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{plan.Role}') THEN CREATE ROLE {role} LOGIN; END IF; END $$",
            $"ALTER ROLE {role} WITH LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION",

            // A service reaches its own database and no other.
            $"REVOKE ALL ON DATABASE {database} FROM PUBLIC",
            $"GRANT CONNECT ON DATABASE {database} TO {role}",
            $"GRANT USAGE ON SCHEMA public TO {role}",

            // Ordinary data tables.
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {role}",

            // The migration history belongs to whoever runs migrations, never to the running service.
            IfTableExists("__EFMigrationsHistory", $"REVOKE ALL ON TABLE \"__EFMigrationsHistory\" FROM {role}"),
        };

        // Conditional, so a table that a later migration creates does not break the setup of an earlier version.
        foreach (var table in plan.ReadOnlyTables)
        {
            statements.Add(IfTableExists(table, $"REVOKE INSERT, UPDATE, DELETE ON TABLE \"{table}\" FROM {role}"));
        }

        foreach (var table in plan.AppendOnlyTables)
        {
            statements.Add(IfTableExists(table, $"REVOKE UPDATE, DELETE ON TABLE \"{table}\" FROM {role}"));
        }

        return statements;
    }

    private static string IfTableExists(string table, string statement) =>
        $"DO $$ BEGIN IF to_regclass('public.\"{table}\"') IS NOT NULL THEN {statement}; END IF; END $$";

    /// <summary>Runs <see cref="BuildStatements"/> against the database as the administrator.</summary>
    /// <param name="adminConnectionString">The administrator's connection string for the service database.</param>
    /// <param name="plan">The role plan.</param>
    /// <param name="password">The role's password.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the role is set up.</returns>
    public static async Task ApplyAsync(string adminConnectionString, DatabaseRolePlan plan, string password, CancellationToken cancellationToken = default)
    {
        var statements = BuildStatements(plan, password);

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var statement in statements)
        {
            // Safe: every identifier was validated against an allow-list and the password is alphanumeric (above).
#pragma warning disable CA2100
            await using var command = new NpgsqlCommand(statement, connection, transaction);
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Replaces the login in a connection string with the service role's (used for the running service).</summary>
    /// <param name="connectionString">The administrator's connection string.</param>
    /// <param name="role">The role name.</param>
    /// <param name="password">The role's password.</param>
    /// <returns>The connection string for the role.</returns>
    public static string WithRole(string connectionString, string role, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        RequireIdentifier(role, nameof(role));

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = role,
            Password = password,
        };
        return builder.ConnectionString;
    }

    private static void RequireIdentifier(string value, string what)
    {
        if (!IdentifierPattern().IsMatch(value ?? string.Empty))
        {
            throw new ArgumentException($"Invalid {what} name.", what);
        }
    }

    /// <summary>Describes the statements, for logging, without the password.</summary>
    /// <param name="statements">The statements built by <see cref="BuildStatements"/>.</param>
    /// <returns>The statements with the password hidden.</returns>
    public static string Describe(IEnumerable<string> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var text = new StringBuilder();
        foreach (var statement in statements)
        {
            text.AppendLine(Regex.Replace(statement, "PASSWORD '[^']*'", "PASSWORD '***'"));
        }

        return text.ToString();
    }
}
