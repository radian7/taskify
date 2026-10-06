using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Npgsql;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.E2ETests;

/// <summary>
/// Board performance (spec SC-007): a project with 200 tasks loads and is interactive within 2 seconds.
/// </summary>
/// <remarks>
/// The tasks are inserted straight into the Tasks database in one statement, because creating 200 tasks through the API
/// would (rightly) run into the per-user write limit (spec FR-031). The project itself is created through the API.
/// </remarks>
/// <param name="app">The running application.</param>
/// <param name="browser">The shared browser.</param>
[Collection(SeedData.Collection)]
public class PerformanceTests(TaskifyAppFixture app, BrowserFixture browser)
{
    private const int TaskCount = 200;

    [Fact]
    public async Task A_board_with_200_tasks_loads_and_is_interactive_within_two_seconds()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var name = "Perf " + Guid.NewGuid().ToString("N")[..6];

        using var projects = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Tomasz);
        using var content = new StringContent($$"""{"name":"{{name}}"}""", Encoding.UTF8, "application/json");
        using var created = await projects.PostAsync("/api/projects", content, cancellation);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var projectId = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellation)).RootElement.GetProperty("id").GetGuid();

        await using (var connection = new NpgsqlConnection(await app.GetConnectionStringAsync("tasksdb")))
        {
            await connection.OpenAsync(cancellation);
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO tasks ("Id", "ProjectId", "Title", "Description", "Status", "AssigneeUserId", "CreatedByUserId", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), @project, 'Task ' || n,
                       'A description for task ' || n,
                       (ARRAY['ToDo','InProgress','InReview','Done'])[1 + n % 4],
                       CASE WHEN n % 5 = 0 THEN NULL ELSE @assignee END,
                       @creator, now() - make_interval(secs => n), now() - make_interval(secs => n)
                FROM generate_series(1, @count) AS n
                """, connection);
            insert.Parameters.AddWithValue("project", projectId);
            insert.Parameters.AddWithValue("assignee", SeedIds.Priya);
            insert.Parameters.AddWithValue("creator", SeedIds.Tomasz);
            insert.Parameters.AddWithValue("count", TaskCount);
            Assert.Equal(TaskCount, await insert.ExecuteNonQueryAsync(cancellation));
        }

        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.Locator("button.user-button", new PageLocatorOptions { HasText = "Tomasz" }).ClickAsync();
        await page.WaitForURLAsync("**/projects");
        var link = page.Locator("a.project-name", new PageLocatorOptions { HasText = name });
        await Assertions.Expect(link).ToBeVisibleAsync();

        var clock = Stopwatch.StartNew();
        await link.ClickAsync();

        // Loaded: every card is on the board. Interactive: the first card's move menu opens.
        await Assertions.Expect(page.Locator("article.task-card")).ToHaveCountAsync(TaskCount, new() { Timeout = 2000 });
        await page.Locator("article.task-card button.move-toggle").First.ClickAsync();
        await Assertions.Expect(page.Locator("[role=menu]")).ToBeVisibleAsync(new() { Timeout = 2000 });
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"The board took {clock.Elapsed.TotalMilliseconds:F0} ms to load and respond (limit 2000 ms).");
    }
}
