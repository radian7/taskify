using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Security;

/// <summary>
/// Audit log tests (spec FR-022, FR-032; research R13; constitution Security Requirements). Each refusal answers with
/// a Problem Details body whose <c>traceId</c> is also the <c>correlationId</c> of the audit line, so a test finds the
/// exact line for its own request even though all tests share one log. The <c>429</c> audit is covered by
/// <c>RateLimitTests</c> (it must exhaust a limit, which this class avoids), and the <c>taskify.rejections</c> counter
/// by the unit test <c>AuditLoggerMetricTests</c>, because the counter lives in another process.
/// </summary>
/// <param name="app">The running application.</param>
public partial class AuditLogTests(TaskifyAppFixture app)
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<string> TraceIdAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("traceId").GetString()!;

    /// <summary>Finds the audit line that carries the given correlation ID, waiting briefly for the log to catch up.</summary>
    private async Task<string> AuditLineAsync(string resource, string correlationId)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var logs = await app.GetLogsAsync(resource);
            var line = logs.Split('\n').LastOrDefault(l => l.Contains("Audit ", StringComparison.Ordinal) && l.Contains($"correlationId={correlationId}", StringComparison.Ordinal));
            if (line is not null)
            {
                return line;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        throw new Xunit.Sdk.XunitException($"No audit line with correlationId={correlationId} in the {resource} log.");
    }

    private static string SourceIp(int last) => $"198.51.100.{last}";

    [Fact]
    public async Task A_validation_failure_is_audited_with_user_outcome_caller_and_forwarded_source_ip()
    {
        var user = SeedIds.Tomasz;
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user, SourceIp(11));

        using var response = await client.PostAsync("/api/tasks", Json($$"""{"projectId":"{{SeedIds.MobileAppLaunch}}","title":"   "}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var line = await AuditLineAsync("tasks-api", await TraceIdAsync(response));
        Assert.Contains("Audit RequestRejected outcome=Validation", line);
        Assert.Contains($"user={user}", line);
        Assert.Contains("caller=web", line);
        Assert.Contains($"sourceIp={SourceIp(11)}", line);
    }

    [Fact]
    public async Task A_missing_acting_user_is_a_400_audit_with_the_caller()
    {
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, actingUser: null, clientIp: SourceIp(12));

        using var response = await client.GetAsync($"/api/tasks?projectId={SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var line = await AuditLineAsync("tasks-api", await TraceIdAsync(response));
        Assert.Contains("Audit RequestRejected outcome=Validation", line);
        Assert.Contains("caller=web", line);
        Assert.Contains($"sourceIp={SourceIp(12)}", line);
    }

    [Fact]
    public async Task An_unknown_api_key_is_a_401_audit_that_never_contains_the_key()
    {
        const string BadKey = "audit-test-wrong-key-0123456789";
        using var client = app.CreateClient("tasks-api", BadKey, SeedIds.Liam);

        using var response = await client.GetAsync($"/api/tasks?projectId={SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var line = await AuditLineAsync("tasks-api", await TraceIdAsync(response));
        Assert.Contains("Audit RequestRejected outcome=Unauthorized", line);
        Assert.Matches(@"sourceIp=(::1|127\.0\.0\.1)", line);
        Assert.DoesNotContain(BadKey, await app.GetLogsAsync("tasks-api"));
    }

    [Fact]
    public async Task A_known_caller_on_a_forbidden_route_is_a_403_audit_naming_the_calling_service()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.NotificationsKey);

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var line = await AuditLineAsync("projects-api", await TraceIdAsync(response));
        Assert.Contains("Audit RequestRejected outcome=Forbidden", line);
        Assert.Contains("caller=notifications", line);
    }

    [Fact]
    public async Task Changing_a_deleted_comment_is_a_409_audit_and_the_text_stays_out_of_every_log()
    {
        var user = app.NextUser();
        const string Secret = "audit-secret-comment-words";
        using var projects = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, user);
        using var projectResponse = await projects.PostAsync("/api/projects", Json("""{"name":"Audit secret project name"}"""), TestContext.Current.CancellationToken);
        var project = JsonDocument.Parse(await projectResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("id").GetGuid();

        using var tasks = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user, SourceIp(14));
        using var taskResponse = await tasks.PostAsync("/api/tasks", Json($$"""{"projectId":"{{project}}","title":"Audit secret title","description":"audit-secret-description"}"""), TestContext.Current.CancellationToken);
        var task = JsonDocument.Parse(await taskResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("id").GetGuid();

        using var created = await tasks.PostAsync($"/api/tasks/{task}/comments", Json($$"""{"text":"{{Secret}}"}"""), TestContext.Current.CancellationToken);
        var comment = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("id").GetGuid();
        using var deleted = await tasks.DeleteAsync($"/api/tasks/{task}/comments/{comment}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        using var edit = await tasks.PutAsync($"/api/tasks/{task}/comments/{comment}", Json("""{"text":"audit-secret-edit"}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);

        var line = await AuditLineAsync("tasks-api", await TraceIdAsync(edit));
        Assert.Contains("Audit RequestRejected outcome=Conflict", line);
        Assert.Contains($"user={user}", line);
        Assert.Contains("entityType=Comment", line);
        Assert.Contains($"entityId={comment}", line);
        Assert.Contains($"sourceIp={SourceIp(14)}", line);

        // Successful changes are audited too, with the acting user and entity.
        var tasksLog = await app.GetLogsAsync("tasks-api");
        Assert.Contains($"Audit CommentAdded outcome=Succeeded user={user}", tasksLog);
        Assert.Contains($"Audit CommentDeleted outcome=Succeeded user={user}", tasksLog);

        // FR-022: no title, description, comment or project name appears in any service's log.
        foreach (var resource in new[] { "projects-api", "tasks-api", "notifications-api", "web" })
        {
            var logs = await app.GetLogsAsync(resource);
            Assert.DoesNotContain(Secret, logs);
            Assert.DoesNotContain("audit-secret-edit", logs);
            Assert.DoesNotContain("audit-secret-description", logs);
            Assert.DoesNotContain("Audit secret title", logs);
            Assert.DoesNotContain("Audit secret project name", logs);
        }
    }

    [Fact]
    public async Task An_oversized_body_is_a_413_audit()
    {
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, SeedIds.Jordan, SourceIp(15));
        var body = $$"""{"projectId":"{{SeedIds.MobileAppLaunch}}","title":"{{new string('x', 1_100_000)}}"}""";

        using var response = await client.PostAsync("/api/tasks", Json(body), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var line = await AuditLineAsync("tasks-api", await TraceIdAsync(response));
        Assert.Contains("Audit RequestRejected outcome=TooLarge", line);
        Assert.Contains("caller=web", line);
        Assert.Contains($"sourceIp={SourceIp(15)}", line);

        // Audited once, not twice.
        var traceId = await TraceIdAsync(response);
        Assert.Single((await app.GetLogsAsync("tasks-api")).Split('\n'), l => l.Contains("Audit ") && l.Contains($"correlationId={traceId}"));
    }

    [Fact]
    public async Task An_unknown_project_reference_is_a_422_audit()
    {
        var unknown = Guid.NewGuid();
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, SeedIds.Liam, SourceIp(16));

        using var response = await client.PostAsync("/api/tasks", Json($$"""{"projectId":"{{unknown}}","title":"No such project"}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var line = await AuditLineAsync("tasks-api", await TraceIdAsync(response));
        Assert.Contains("Audit RequestRejected outcome=UnknownReference", line);
        Assert.Contains($"user={SeedIds.Liam}", line);
        Assert.Contains("entityType=Project", line);
        Assert.Contains($"entityId={unknown}", line);
        Assert.Contains($"sourceIp={SourceIp(16)}", line);
    }

    [Fact]
    public async Task Choosing_and_switching_users_is_audited_with_the_previous_user_and_the_ip()
    {
        using var browser = app.CreateBrowser(out _);

        async Task SelectAsync(Guid user)
        {
            using var page = await browser.GetAsync("/", TestContext.Current.CancellationToken);
            var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var token = WebUtility.HtmlDecode(TokenPattern().Match(html).Groups[1].Value);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["userId"] = user.ToString("D"), ["__RequestVerificationToken"] = token });
            using var response = await browser.PostAsync("/session/select", form, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        await SelectAsync(SeedIds.Liam);
        await SelectAsync(SeedIds.Jordan);

        var logs = await app.GetLogsAsync("web");
        Assert.Matches($@"Audit UserSelected outcome=Succeeded user={SeedIds.Jordan} previousUser={SeedIds.Liam} .*sourceIp=(::1|127\.0\.0\.1)", logs);
    }
}
