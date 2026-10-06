using System.Net;
using Taskify.Contracts;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Taskify.TestSupport;

/// <summary>
/// Starts the real AppHost (all three APIs, the Web app and PostgreSQL in a container) once for the whole test
/// run, with throwaway secrets (research R11). Tests talk to the APIs over HTTPS exactly as the Web app does.
/// </summary>
/// <remarks>
/// Needs a container runtime. With Podman, set <c>ASPIRE_CONTAINER_RUNTIME=podman</c>.
/// </remarks>
public class TaskifyAppFixture : IAsyncLifetime
{
    /// <summary>The API key of the Web app in this test run.</summary>
    public const string WebKey = "test-web-key-5c1f9d3a7e2b4c6d8a0f";

    /// <summary>The API key of the Projects API in this test run.</summary>
    public const string ProjectsKey = "test-projects-key-b7e2d9f1a3c5084e";

    /// <summary>The API key of the Tasks API in this test run.</summary>
    public const string TasksKey = "test-tasks-key-3a9c5e7b1d2f4086ab";

    /// <summary>The API key of the Notifications API in this test run.</summary>
    public const string NotificationsKey = "test-notifications-key-8d4b6f2a0c1e";

    /// <summary>The PostgreSQL administrator password in this test run.</summary>
    public const string DatabasePassword = "TestPostgresPassword0123456789";

    /// <summary>The password of the <c>projects_app</c> database role in this test run.</summary>
    public const string ProjectsDbPassword = "TestProjectsDbPassword012345678";

    /// <summary>The password of the <c>tasks_app</c> database role in this test run.</summary>
    public const string TasksDbPassword = "TestTasksDbPassword0123456789ab";

    /// <summary>The password of the <c>notifications_app</c> database role in this test run.</summary>
    public const string NotificationsDbPassword = "TestNotificationsDbPassword012345";

    private const string CertificatePassword = "TestCertificatePassword0123";

    /// <summary>The PostgreSQL <c>application_name</c> that every connection opened by a test carries.</summary>
    public const string TestApplicationName = "taskify-tests";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(6);

    private DistributedApplication? app;

    /// <summary>
    /// Gets command-line arguments added after the standard ones, so they win. A derived fixture uses this to start a
    /// second application with its own settings, for example the persistence test with its own data volume.
    /// </summary>
    protected virtual IReadOnlyList<string> ExtraArguments => [];

    /// <summary>Gets the running application.</summary>
    public DistributedApplication App => app ?? throw new InvalidOperationException("The fixture has not started.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Taskify_AppHost>(
            [
                "--Taskify:PersistData=false",   // a fresh database every run, never the developer's volume
                $"--Parameters:postgres-password={DatabasePassword}",
                $"--Parameters:projects-db-password={ProjectsDbPassword}",
                $"--Parameters:tasks-db-password={TasksDbPassword}",
                $"--Parameters:notifications-db-password={NotificationsDbPassword}",
                $"--Parameters:web-api-key={WebKey}",
                $"--Parameters:projects-api-key={ProjectsKey}",
                $"--Parameters:tasks-api-key={TasksKey}",
                $"--Parameters:notifications-api-key={NotificationsKey}",
                $"--Parameters:dataprotection-cert={CreateDataProtectionCertificate()}",
                $"--Parameters:dataprotection-cert-password={CertificatePassword}",
                .. ExtraArguments,
            ],
            (options, _) => options.DisableDashboard = true,
            cancellation);

        app = await builder.BuildAsync(cancellation);
        await app.StartAsync(cancellation);

        foreach (var resource in new[] { "projects-api", "tasks-api", "notifications-api", "web" })
        {
            await app.ResourceNotifications
                .WaitForResourceHealthyAsync(resource, cancellation)
                .WaitAsync(StartupTimeout, cancellation);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private static readonly Guid[] RotatingUsers = [SeedIds.Priya, SeedIds.Liam, SeedIds.Tomasz, SeedIds.Jordan];
    private int nextUser;

    /// <summary>
    /// Returns the next of four users in turn, for tests that do not care who the acting user is. Every user is limited
    /// to 60 changes a minute (spec FR-031), so spreading the load keeps a long run from hitting that limit by accident.
    /// The Product Manager is left out on purpose: the rate-limit tests use that user, so nothing else spends its budget.
    /// </summary>
    /// <returns>A user ID.</returns>
    public Guid NextUser() => RotatingUsers[(uint)Interlocked.Increment(ref nextUser) % (uint)RotatingUsers.Length];

    /// <summary>Gets the HTTPS address of a resource.</summary>
    /// <param name="resource">The resource name, for example <c>tasks-api</c>.</param>
    /// <returns>The base address.</returns>
    public Uri GetUri(string resource) => App.GetEndpoint(resource, "https");

    /// <summary>
    /// Creates a client for one service with the given credentials. Pass <see langword="null"/> to leave a header
    /// out. Only the localhost development certificate is accepted.
    /// </summary>
    /// <param name="resource">The resource name.</param>
    /// <param name="apiKey">The <c>X-Api-Key</c> value, or <see langword="null"/> for none.</param>
    /// <param name="actingUser">The <c>X-Taskify-User</c> value, or <see langword="null"/> for none.</param>
    /// <param name="clientIp">The <c>X-Taskify-Client-Ip</c> value, or <see langword="null"/> for none.</param>
    /// <returns>The client. The caller disposes it.</returns>
    public HttpClient CreateClient(string resource, string? apiKey, Guid? actingUser = null, string? clientIp = null)
    {
        var handler = new SocketsHttpHandler
        {
            // Accept only the ASP.NET Core development certificate that the services present on localhost.
            SslOptions = { RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.Subject == "CN=localhost" },
        };
        var client = new HttpClient(handler, disposeHandler: true) { BaseAddress = GetUri(resource), Timeout = TimeSpan.FromSeconds(30) };

        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        if (actingUser is { } user)
        {
            client.DefaultRequestHeaders.Add("X-Taskify-User", user.ToString("D"));
        }

        if (clientIp is not null)
        {
            client.DefaultRequestHeaders.Add("X-Taskify-Client-Ip", clientIp);
        }

        return client;
    }

    /// <summary>
    /// Creates a client that behaves like a browser against the Web app: it keeps cookies and does not follow
    /// redirects, so a test can look at each response. Only the localhost development certificate is accepted.
    /// </summary>
    /// <param name="cookies">The cookie jar, so a test can inspect or tamper with cookies.</param>
    /// <returns>The client. The caller disposes it.</returns>
    public HttpClient CreateBrowser(out System.Net.CookieContainer cookies)
    {
        cookies = new System.Net.CookieContainer();
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = cookies,
            SslOptions = { RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.Subject == "CN=localhost" },
        };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = GetUri("web"), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Gets a connection string for one service database. By default it logs in as the PostgreSQL administrator; pass a
    /// role and password to log in as a service's least-privilege role instead. TLS is required either way.
    /// </summary>
    /// <param name="database">The database resource name, for example <c>tasksdb</c>.</param>
    /// <param name="role">The role to log in as, or <see langword="null"/> for the administrator.</param>
    /// <param name="password">The role's password.</param>
    /// <returns>The connection string.</returns>
    public async Task<string> GetConnectionStringAsync(string database, string? role = null, string? password = null)
    {
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = model.Resources.OfType<IResourceWithConnectionString>().Single(r => r.Name == database);
        var connectionString = await resource.GetConnectionStringAsync(TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"No connection string for {database}.");

        // The application name lets tests tell their own sessions from the services' (see DatabaseRoleTests).
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { SslMode = Npgsql.SslMode.Require, ApplicationName = TestApplicationName };
        if (role is not null)
        {
            builder.Username = role;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    /// <summary>Gets the log text a resource has written so far.</summary>
    /// <param name="resource">The resource name.</param>
    /// <returns>All log lines, joined with newlines.</returns>
    public async Task<string> GetLogsAsync(string resource)
    {
        var logger = App.Services.GetRequiredService<ResourceLoggerService>();
        // Aspire names each running instance with a random suffix, so look the resource up by its model name.
        var model = App.Services.GetRequiredService<DistributedApplicationModel>();
        var target = model.Resources.Single(r => r.Name == resource);
        var lines = new List<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            await foreach (var batch in logger.WatchAsync(target).WithCancellation(timeout.Token))
            {
                lines.AddRange(batch.Select(l => l.Content));
            }
        }
        catch (OperationCanceledException)
        {
            // WatchAsync streams until cancelled; stopping after the backlog is the intent.
        }

        return string.Join('\n', lines);
    }

    private static string CreateDataProtectionCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=taskify-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, CertificatePassword));
    }
}
