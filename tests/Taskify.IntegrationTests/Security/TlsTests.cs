using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Security;

/// <summary>Encryption in transit everywhere (constitution Principle I; research R16).</summary>
/// <param name="app">The running application.</param>
public class TlsTests(TaskifyAppFixture app)
{
    private static readonly string[] Apis = ["projects-api", "tasks-api", "notifications-api"];

    [Theory]
    [InlineData("projects-api")]
    [InlineData("tasks-api")]
    [InlineData("notifications-api")]
    [InlineData("web")]
    public void No_service_has_a_plain_http_endpoint(string resource)
    {
        var model = app.App.Services.GetRequiredService<DistributedApplicationModel>();
        var endpoints = model.Resources.Single(r => r.Name == resource).Annotations.OfType<EndpointAnnotation>().ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.Equal("https", endpoint.UriScheme));
    }

    [Theory]
    [InlineData("projects-api")]
    [InlineData("tasks-api")]
    [InlineData("notifications-api")]
    public async Task A_plain_http_request_to_an_api_port_is_refused(string resource)
    {
        var plain = new UriBuilder(app.GetUri(resource)) { Scheme = Uri.UriSchemeHttp }.Uri;
        using var client = new HttpClient { BaseAddress = plain, Timeout = TimeSpan.FromSeconds(10) };

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Every_service_connection_to_postgresql_is_encrypted()
    {
        var model = app.App.Services.GetRequiredService<DistributedApplicationModel>();
        var database = model.Resources.OfType<IResourceWithConnectionString>().Single(r => r.Name == "projectsdb");
        var connectionString = await database.GetConnectionStringAsync(TestContext.Current.CancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { SslMode = SslMode.Require };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // The session of this test itself.
        await using (var own = new NpgsqlCommand("SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection))
        {
            Assert.True((bool)(await own.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        }

        // Every other session on the three service databases (the services' own connections).
        await using var others = new NpgsqlCommand(
            """
            SELECT a.datname, s.ssl
            FROM pg_stat_ssl s JOIN pg_stat_activity a ON a.pid = s.pid
            WHERE a.datname IN ('projectsdb', 'tasksdb', 'notificationsdb') AND a.pid <> pg_backend_pid()
            """,
            connection);
        await using var reader = await others.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var sessions = new List<(string Database, bool Ssl)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            sessions.Add((reader.GetString(0), reader.GetBoolean(1)));
        }

        Assert.NotEmpty(sessions);
        Assert.All(sessions, session => Assert.True(session.Ssl, $"a connection to {session.Database} is not encrypted"));
    }

    [Fact]
    public void The_api_list_in_this_test_matches_the_services_that_exist()
    {
        var model = app.App.Services.GetRequiredService<DistributedApplicationModel>();
        var names = model.Resources.Select(r => r.Name).ToHashSet();

        Assert.All(Apis, api => Assert.Contains(api, names));
    }
}
