using System.Net;
using System.Text;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Security;

/// <summary>
/// Injection test set (spec SC-006; constitution Principle I; research R9). Hostile text must be stored verbatim and
/// returned verbatim (the APIs never rewrite it), and encoding happens where it is rendered. The Web pages render with
/// Blazor components and never with <c>MarkupString</c> (bUnit tests cover the encoded output); here the interactive
/// pages are checked to carry none of the text in the served HTML. SQL-injection strings must never cause a
/// <c>500</c> or change data.
/// </summary>
/// <remarks>
/// Writes are spread over the rotating users with <see cref="TaskifyAppFixture.NextUser"/> and kept few (one project,
/// task and comment per payload), so the per-user write limit (SC-010) is not at risk.
/// </remarks>
/// <param name="app">The running application.</param>
public class InjectionTests(TaskifyAppFixture app)
{
    /// <summary>Common XSS payloads: script tag, event attribute, <c>javascript:</c> URL, SVG, markup breaking out of an attribute.</summary>
    public static TheoryData<string> XssPayloads =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<a href=\"javascript:alert(1)\">click</a>",
        "<svg/onload=alert(1)>",
        "\"><script>alert('x')</script>",
    ];

    private static readonly string[] SqlPayloads =
    [
        "'; DROP TABLE tasks; --",
        "' OR '1'='1",
        "1; DELETE FROM projects WHERE 1=1",
        "Robert'); DROP TABLE comments;--",
    ];

    private HttpClient Projects(Guid user) => app.CreateClient("projects-api", TaskifyAppFixture.WebKey, user);

    private HttpClient Tasks(Guid user) => app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Esc(string text) => JsonSerializer.Serialize(text);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    private async Task<JsonElement> CreateProjectAsync(string name, string? description = null)
    {
        using var client = Projects(app.NextUser());
        using var response = await client.PostAsync("/api/projects", Json($$"""{"name":{{Esc(name)}},"description":{{(description is null ? "null" : Esc(description))}}}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> CreateTaskAsync(Guid user, Guid project, string title, string? description, Guid? assignee = null)
    {
        using var client = Tasks(user);
        var body = $$"""{"projectId":"{{project}}","title":{{Esc(title)}},"description":{{(description is null ? "null" : Esc(description))}},"assigneeUserId":{{(assignee is null ? "null" : $"\"{assignee}\"")}}}""";
        using var response = await client.PostAsync("/api/tasks", Json(body), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public async Task An_xss_payload_is_stored_and_returned_verbatim_in_every_text_field(string payload)
    {
        var project = await CreateProjectAsync(payload, payload);
        var projectId = project.GetProperty("id").GetGuid();
        Assert.Equal(payload, project.GetProperty("name").GetString());
        Assert.Equal(payload, project.GetProperty("description").GetString());

        var task = await CreateTaskAsync(app.NextUser(), projectId, payload, payload);
        var taskId = task.GetProperty("id").GetGuid();
        Assert.Equal(payload, task.GetProperty("title").GetString());

        using var tasks = Tasks(app.NextUser());
        using var comment = await tasks.PostAsync($"/api/tasks/{taskId}/comments", Json($$"""{"text":{{Esc(payload)}}}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        Assert.Equal(payload, (await ReadAsync(comment)).GetProperty("text").GetString());

        // Read everything back: the stored values are the payload, byte for byte.
        using var projects = Projects(app.NextUser());
        using var fetchedProject = await projects.GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken);
        var fetched = await ReadAsync(fetchedProject);
        Assert.Equal(payload, fetched.GetProperty("name").GetString());
        Assert.Equal(payload, fetched.GetProperty("description").GetString());

        using var fetchedTask = await tasks.GetAsync($"/api/tasks/{taskId}", TestContext.Current.CancellationToken);
        var detail = await ReadAsync(fetchedTask);
        Assert.Equal(payload, detail.GetProperty("title").GetString());
        Assert.Equal(payload, detail.GetProperty("description").GetString());

        var comments = await ReadAsync(await tasks.GetAsync($"/api/tasks/{taskId}/comments", TestContext.Current.CancellationToken));
        Assert.Equal(payload, comments.EnumerateArray().Single().GetProperty("text").GetString());

        // The API serves it as JSON, never as HTML, so a browser does not interpret it as markup.
        Assert.Equal("application/json", fetchedTask.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_hostile_title_reaches_the_notification_summary_verbatim_and_the_web_pages_never_serve_it_as_markup()
    {
        const string Payload = "<img src=x onerror=alert(1)>";
        var project = (await CreateProjectAsync("Injection notifications")).GetProperty("id").GetGuid();

        // Priya assigns the task to Tomasz, which notifies Tomasz.
        var taskId = (await CreateTaskAsync(SeedIds.Priya, project, Payload, null, SeedIds.Tomasz)).GetProperty("id").GetGuid();

        string? summary = null;
        using var notifications = app.CreateClient("notifications-api", TaskifyAppFixture.WebKey, SeedIds.Tomasz);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (summary is null && DateTimeOffset.UtcNow < deadline)
        {
            var list = await ReadAsync(await notifications.GetAsync("/api/notifications?limit=100", TestContext.Current.CancellationToken));
            summary = list.EnumerateArray()
                .Where(n => n.GetProperty("taskId").GetGuid() == taskId)
                .Select(n => n.GetProperty("summary").GetString())
                .FirstOrDefault();
            if (summary is null)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
        }

        // The API keeps the text as typed; the bell component encodes it when it renders (see the bUnit tests).
        Assert.NotNull(summary);
        Assert.Contains(Payload, summary);

        // The Web pages are interactive without prerendering, so the served HTML carries no user text at all.
        using var browser = app.CreateBrowser(out _);
        foreach (var path in new[] { "/", "/projects", $"/projects/{project}", $"/projects/{project}/tasks/{taskId}" })
        {
            using var page = await browser.GetAsync(path, TestContext.Current.CancellationToken);
            var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("onerror=alert", html);
        }
    }

    [Fact]
    public async Task Sql_injection_strings_are_stored_as_text_cause_no_500_and_change_no_other_data()
    {
        var project = (await CreateProjectAsync("Injection SQL")).GetProperty("id").GetGuid();
        using var tasks = Tasks(app.NextUser());
        var sampleBefore = (await ReadAsync(await tasks.GetAsync($"/api/tasks?projectId={SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken))).GetArrayLength();

        // As stored text (a parameterized query keeps it as data).
        foreach (var payload in SqlPayloads)
        {
            var created = await CreateTaskAsync(app.NextUser(), project, payload, payload);
            Assert.Equal(payload, created.GetProperty("title").GetString());
        }

        // As query-string and path values: rejected or not found, never a server error.
        var hostileUrls = new (string Resource, string Url)[]
        {
            ("tasks-api", "/api/tasks?projectId=" + Uri.EscapeDataString("1' OR '1'='1")),
            ("tasks-api", "/api/tasks?projectId=" + Uri.EscapeDataString("'; DROP TABLE tasks; --")),
            ("tasks-api", "/api/tasks/" + Uri.EscapeDataString("1; DROP TABLE tasks")),
            ("tasks-api", "/api/tasks/" + Uri.EscapeDataString("1; DROP TABLE tasks") + "/comments"),
            ("projects-api", "/api/projects/" + Uri.EscapeDataString("' OR 1=1 --")),
            ("notifications-api", "/api/notifications?limit=" + Uri.EscapeDataString("1; DROP TABLE notifications")),
            ("notifications-api", "/api/notifications?unreadOnly=" + Uri.EscapeDataString("' OR '1'='1")),
        };
        foreach (var (resource, url) in hostileUrls)
        {
            using var client = app.CreateClient(resource, TaskifyAppFixture.WebKey, app.NextUser());
            using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
            Assert.True((int)response.StatusCode is >= 400 and < 500, $"{url} returned {(int)response.StatusCode}");
        }

        // Nothing else changed: the sample project has the same tasks, and the new project has exactly the test's tasks.
        Assert.Equal(sampleBefore, (await ReadAsync(await tasks.GetAsync($"/api/tasks?projectId={SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken))).GetArrayLength());
        Assert.Equal(SqlPayloads.Length, (await ReadAsync(await tasks.GetAsync($"/api/tasks?projectId={project}", TestContext.Current.CancellationToken))).GetArrayLength());
    }
}
