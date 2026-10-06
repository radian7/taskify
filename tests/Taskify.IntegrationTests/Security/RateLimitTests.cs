using System.Net;
using System.Text;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Security;

/// <summary>
/// Rate limits at the API level (spec FR-031, SC-010; research R9): 60 writes and 300 reads a minute per user and per
/// service.
/// </summary>
/// <remarks>
/// <para>
/// These tests deliberately exhaust limits, so they act as Maya Chen, the one seeded user that no other test class
/// writes or reads as on the services used here (<see cref="TaskifyAppFixture.NextUser"/> leaves her out on purpose;
/// the limits are per acting user, per service and per policy, so nothing the other classes do is counted against, or
/// blocked by, her budget). Writes are spent on the Projects API and reads on the Notifications API.
/// </para>
/// <para>
/// The first 60 writes are invalid requests (<c>400</c>): the limiter counts every request before the handler runs, so
/// they use up the budget without creating 60 projects in the shared database.
/// </para>
/// </remarks>
/// <param name="app">The running application.</param>
public class RateLimitTests(TaskifyAppFixture app)
{
    private const string ForwardedIp = "198.51.100.77";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private HttpClient Maya(string resource) => app.CreateClient(resource, TaskifyAppFixture.WebKey, SeedIds.Maya, ForwardedIp);

    [Fact]
    public async Task The_61st_write_in_a_minute_is_429_with_Retry_After_nothing_is_saved_and_the_rejection_is_audited()
    {
        var name = "Rate limited " + Guid.NewGuid().ToString("N");
        using var maya = Maya("projects-api");

        for (var i = 1; i <= 60; i++)
        {
            using var counted = await maya.PostAsync("/api/projects", Json("""{"name":""}"""), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, counted.StatusCode);
        }

        using var response = await maya.PostAsync("/api/projects", Json($$"""{"name":"{{name}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.InRange(int.Parse(retryAfter!.Single(), System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        var traceId = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("traceId").GetString();

        // Nothing was saved: another user fetches the list and the project is not in it. (Her own reads are not limited.)
        using var other = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, app.NextUser());
        var list = await other.GetStringAsync("/api/projects", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(name, list);

        // Another user is unaffected, and so is the same user on a different service (limits count per service, FR-031).
        using var otherWrite = await other.PostAsync("/api/projects", Json($$"""{"name":"Unaffected {{Guid.NewGuid():N}}"}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, otherWrite.StatusCode);

        using var notifications = Maya("notifications-api");
        using var readAll = await notifications.PostAsync("/api/notifications/read-all", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);

        // The Projects API still refuses her, because the minute has not passed.
        using var again = await maya.PostAsync("/api/projects", Json("""{"name":""}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);

        // The rejection is audited with the user, the caller and the forwarded source IP.
        string? line = null;
        for (var attempt = 0; attempt < 5 && line is null; attempt++)
        {
            line = (await app.GetLogsAsync("projects-api")).Split('\n')
                .LastOrDefault(l => l.Contains($"correlationId={traceId}", StringComparison.Ordinal) && l.Contains("Audit ", StringComparison.Ordinal));
            if (line is null)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
        }

        Assert.NotNull(line);
        Assert.Contains("Audit RequestRejected outcome=RateLimited", line);
        Assert.Contains($"user={SeedIds.Maya}", line);
        Assert.Contains("caller=web", line);
        Assert.Contains($"sourceIp={ForwardedIp}", line);
        Assert.DoesNotContain(name, line);
    }

    [Fact]
    public async Task The_301st_read_in_a_minute_is_429_with_Retry_After_while_other_users_can_still_read()
    {
        using var maya = Maya("notifications-api");

        for (var i = 1; i <= 300; i++)
        {
            using var counted = await maya.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        }

        using var response = await maya.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));

        using var other = app.CreateClient("notifications-api", TaskifyAppFixture.WebKey, app.NextUser());
        using var otherRead = await other.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
    }
}
