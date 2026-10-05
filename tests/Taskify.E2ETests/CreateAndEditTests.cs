using Microsoft.Playwright;
using Taskify.TestSupport;

namespace Taskify.E2ETests;

/// <summary>
/// Creating projects and tasks, assigning and editing them in a real browser (spec FR-006, FR-009 to FR-011, FR-019,
/// FR-020; User Story 3). Every test creates its own project, so it does not disturb the sample data.
/// </summary>
/// <param name="app">The running application.</param>
/// <param name="browser">The shared browser.</param>
[Collection(SeedData.Collection)]
public class CreateAndEditTests(TaskifyAppFixture app, BrowserFixture browser)
{
    private static ILocator Column(IPage page, string status) => page.Locator($"section.board-column[data-status='{status}']");

    private static ILocator Card(IPage page, string status, string title) =>
        Column(page, status).Locator("article.task-card", new LocatorLocatorOptions { Has = page.Locator($".task-title a:text-is('{title}')") });

    private static async Task ChooseUserAsync(IPage page, string name)
    {
        await page.GotoAsync("/");
        await page.Locator("button.user-button", new PageLocatorOptions { HasText = name }).ClickAsync();
        await page.WaitForURLAsync("**/projects");
    }

    private static async Task CreateProjectAsync(IPage page, string name, string? description = null)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New project" }).ClickAsync();
        await page.Locator("#project-name").FillAsync(name);
        if (description is not null)
        {
            await page.Locator("#project-description").FillAsync(description);
        }

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create project" }).ClickAsync();
    }

    private static async Task AddTaskAsync(IPage page, string title, string? assignee = null, string? description = null)
    {
        await page.Locator("button.add-task").ClickAsync();
        await page.Locator("#task-title").FillAsync(title);
        if (description is not null)
        {
            await page.Locator("#task-description").FillAsync(description);
        }

        if (assignee is not null)
        {
            await page.Locator("#task-assignee").SelectOptionAsync(new SelectOptionValue { Label = assignee });
        }

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add task" }).ClickAsync();
        await Assertions.Expect(Card(page, "ToDo", title)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_user_creates_a_project_adds_and_assigns_tasks_edits_one_and_everyone_sees_it()
    {
        var name = "QA Demo " + Guid.NewGuid().ToString("N")[..6];
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await ChooseUserAsync(page, "Priya Patel");

        // A new project is listed first, and its board is empty with a prompt to add the first task.
        await CreateProjectAsync(page, name, "A demo project.");
        await Assertions.Expect(page.Locator("a.project-name").First).ToHaveTextAsync(name);
        await page.Locator("a.project-name", new PageLocatorOptions { HasText = name }).ClickAsync();
        await Assertions.Expect(page.GetByText("Add the first task")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("article.task-card")).ToHaveCountAsync(0);

        // Three tasks: two assigned to different users, one left unassigned. New tasks start in To Do.
        await AddTaskAsync(page, "Draft the plan", assignee: "Jordan Lee");
        await AddTaskAsync(page, "Review the plan", assignee: "Liam Novak", description: "Line one\nLine two");
        await AddTaskAsync(page, "Celebrate");
        await Assertions.Expect(Column(page, "ToDo").Locator("article.task-card")).ToHaveCountAsync(3);
        await Assertions.Expect(Card(page, "ToDo", "Draft the plan")).ToContainTextAsync("Jordan Lee");
        await Assertions.Expect(Card(page, "ToDo", "Celebrate")).ToContainTextAsync("Unassigned");

        // Reassign one task from its details page.
        await Card(page, "ToDo", "Draft the plan").Locator(".task-title a").ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit task" }).ClickAsync();
        await Assertions.Expect(page.Locator("#task-title")).ToHaveValueAsync("Draft the plan");
        await page.Locator("#task-assignee").SelectOptionAsync(new SelectOptionValue { Label = "Tomasz Wiśniewski" });
        await page.Locator("#task-title").FillAsync("Draft the plan (v2)");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Draft the plan (v2)");
        await Assertions.Expect(page.Locator(".task-facts")).ToContainTextAsync("Tomasz Wiśniewski");

        // The board shows the change.
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Back to the board" }).ClickAsync();
        await Assertions.Expect(Card(page, "ToDo", "Draft the plan (v2)")).ToContainTextAsync("Tomasz Wiśniewski");

        // Another user sees the project in the list, with the tasks, and their own tasks marked.
        await using var other = await browser.NewSessionAsync(app.GetUri("web"));
        var otherPage = await other.NewPageAsync();
        await ChooseUserAsync(otherPage, "Liam Novak");
        await otherPage.Locator("a.project-name", new PageLocatorOptions { HasText = name }).ClickAsync();
        await Assertions.Expect(Column(otherPage, "ToDo").Locator("article.task-card")).ToHaveCountAsync(3);
        await Assertions.Expect(otherPage.Locator("article.card--mine")).ToHaveCountAsync(1);
        await Assertions.Expect(otherPage.Locator("article.card--mine")).ToContainTextAsync("Review the plan");
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_a_clear_message_and_nothing_is_created()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await ChooseUserAsync(page, "Tomasz Wiśniewski");
        var projectsBefore = await page.Locator("a.project-name").CountAsync();

        // Only spaces is empty, so it is refused; the counter shows what the server counts.
        await CreateProjectAsync(page, "     ");
        await Assertions.Expect(page.Locator(".error-banner")).ToContainTextAsync("Name must be between 1 and 100 characters.");
        await Assertions.Expect(page.Locator(".char-counter").First).ToHaveTextAsync("0 / 100");
        await page.Locator("#project-name").FillAsync(new string('x', 101));
        await Assertions.Expect(page.Locator(".char-counter--over")).ToHaveCountAsync(1);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create project" }).ClickAsync();
        await Assertions.Expect(page.Locator(".error-banner")).ToContainTextAsync("Name must be between 1 and 100 characters.");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("a.project-name")).ToHaveCountAsync(projectsBefore);
    }

    [Fact]
    public async Task Markup_in_a_title_is_shown_as_text_and_never_runs()
    {
        var name = "Injection " + Guid.NewGuid().ToString("N")[..6];
        const string hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        var dialogs = new List<string>();
        page.Dialog += (_, dialog) =>
        {
            dialogs.Add(dialog.Message);
            _ = dialog.DismissAsync();
        };
        var console = new List<string>();
        page.Console += (_, message) => console.Add(message.Text);

        await ChooseUserAsync(page, "Jordan Lee");
        await CreateProjectAsync(page, name);
        await page.Locator("a.project-name", new PageLocatorOptions { HasText = name }).ClickAsync();
        await page.Locator("button.add-task").ClickAsync();
        await page.Locator("#task-title").FillAsync(hostile);
        await page.Locator("#task-description").FillAsync(hostile);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add task" }).ClickAsync();

        await Assertions.Expect(Column(page, "ToDo").Locator(".task-title")).ToHaveTextAsync(hostile);
        await Column(page, "ToDo").Locator(".task-title a").ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(hostile);
        await Assertions.Expect(page.Locator(".task-description")).ToHaveTextAsync(hostile);
        await page.GotoAsync("/projects");
        await Assertions.Expect(page.Locator("a.project-name").First).ToBeVisibleAsync();

        Assert.Empty(dialogs);
        Assert.Equal(0, await page.Locator("main img, main script").CountAsync());
        Assert.DoesNotContain(console, line => line.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase));
    }
}
