using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Taskify.Security.Data;

/// <summary>
/// Registers a service's database context over an encrypted connection, running as its least-privilege role
/// (constitution Principle I; research R3, R16). Migrations run separately, as the administrator
/// (see <see cref="MigrateAndSecureAsync{TContext}"/>).
/// </summary>
public static class DatabaseSetup
{
    /// <summary>The configuration key that selects the TLS mode (<c>Require</c>, <c>VerifyCA</c> or <c>VerifyFull</c>).</summary>
    public const string SslModeKey = "Taskify:Database:SslMode";

    /// <summary>
    /// Adds a context using the Aspire connection <paramref name="connectionName"/> over TLS. When a service role is
    /// configured (<see cref="DatabaseRolePlan.RoleKey"/>), the context connects as that role, not as the administrator.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="builder">The host builder.</param>
    /// <param name="connectionName">The Aspire database resource name, for example <c>tasksdb</c>.</param>
    public static void AddTaskifyDbContext<TContext>(this IHostApplicationBuilder builder, string connectionName)
        where TContext : DbContext
    {
        var configuration = builder.Configuration;
        var applicationName = builder.Environment.ApplicationName;
        builder.AddNpgsqlDbContext<TContext>(
            connectionName,
            settings =>
            {
                settings.ConnectionString = ForService(settings.ConnectionString, configuration, applicationName);

                // Aspire's retrying execution strategy refuses explicit transactions, which the move uses to lock the
                // task row (research R14). A transient failure surfaces as a generic error and the user simply retries.
                settings.DisableRetry = true;
            });
    }

    /// <summary>
    /// Applies the migrations as the administrator and then creates or refreshes the service's database role
    /// (see <see cref="DatabaseRoles"/>). Without a configured role (for example in a plain test host) it only migrates.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="services">The application services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="connectionName">The Aspire database resource name.</param>
    /// <param name="plan">The role plan, or <see langword="null"/> to skip role setup.</param>
    /// <param name="createContext">Creates a context from options that use the administrator connection.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the database is ready.</returns>
    public static async Task MigrateAndSecureAsync<TContext>(
        this IServiceProvider services,
        IConfiguration configuration,
        string connectionName,
        DatabaseRolePlan? plan,
        Func<DbContextOptions<TContext>, TContext> createContext,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(createContext);

        var administrator = WithTls(
            configuration.GetConnectionString(connectionName)
                ?? throw new InvalidOperationException($"Connection string '{connectionName}' is missing."),
            configuration[SslModeKey]);

        var options = new DbContextOptionsBuilder<TContext>().UseNpgsql(administrator).Options;
        await using (var context = createContext(options))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        var password = configuration[DatabaseRolePlan.PasswordKey];
        if (plan is not null && !string.IsNullOrEmpty(password))
        {
            await DatabaseRoles.ApplyAsync(administrator, plan, password, cancellationToken);
        }

        // Close the administrator's pooled connections: from here on the service runs only as its own role, and an idle
        // administrator session left in the pool would be needless standing privilege (least privilege).
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>
    /// Forces TLS on a connection string. <c>Require</c> (development) encrypts the connection but does not verify
    /// the server certificate, which is acceptable for the local container on the private Aspire network.
    /// <c>VerifyFull</c> (deployment) also verifies the certificate and the host name.
    /// </summary>
    /// <param name="connectionString">The connection string provided by Aspire.</param>
    /// <param name="sslMode">The configured mode, or <see langword="null"/> for <c>Require</c>.</param>
    /// <returns>The connection string with TLS enforced.</returns>
    public static string WithTls(string? connectionString, string? sslMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        switch (sslMode)
        {
            case "VerifyFull":
                builder.SslMode = SslMode.VerifyFull;
                break;
            case "VerifyCA":
                builder.SslMode = SslMode.VerifyCA;
                break;
            case null:
            case "":
            case "Require":
                // Development only: encrypts without verifying the self-signed certificate that the AppHost
                // generates for the local container (Npgsql 10 does not validate the certificate in Require mode).
                builder.SslMode = SslMode.Require;
                break;
            default:
                throw new InvalidOperationException($"Unsupported value for {SslModeKey}. Use Require, VerifyCA or VerifyFull.");
        }

        return builder.ConnectionString;
    }

    private static string ForService(string? administratorConnectionString, IConfiguration configuration, string applicationName)
    {
        var connectionString = WithTls(administratorConnectionString, configuration[SslModeKey]);

        // Shows up as application_name in pg_stat_activity, so an operator can see which service owns a session.
        connectionString = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = applicationName }.ConnectionString;

        var role = configuration[DatabaseRolePlan.RoleKey];
        var password = configuration[DatabaseRolePlan.PasswordKey];
        return !string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(password)
            ? DatabaseRoles.WithRole(connectionString, role, password)
            : connectionString;
    }
}
