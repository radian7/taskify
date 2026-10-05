using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using BoardPage = Taskify.Web.Components.Pages.Board;
using Taskify.Web.Services;
using Taskify.Web.Services.ApiClients;
using Taskify.Web.Tests.Support;

namespace Taskify.Web.Tests.Board;

/// <summary>Board rendering (spec FR-008, FR-013, FR-014; User Story 1).</summary>
public sealed class BoardTests : BunitContext
{
    private static readonly Guid ProjectId = SeedIds.MobileAppLaunch;

    private static DateTimeOffset Day(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);

    private readonly StubHandler handler = new();
    private readonly RecordingAuditLogger audit = new();

    private IRenderedComponent<BoardPage> RenderBoard(Guid? userId, params object[] tasks)
    {
        var identity = TestIdentity.For(userId);
        handler
            .On("/api/users", JsonSerializer.Serialize(SampleUsers.All, ContractJson.Options))
            .On($"/api/projects/{ProjectId}", JsonSerializer.Serialize(
                new ProjectDto(ProjectId, "Mobile App Launch", null, SeedIds.Maya, Day(1)), ContractJson.Options))
            .On($"/api/tasks?projectId={ProjectId}", JsonSerializer.Serialize(tasks, ContractJson.Options));

        Services.AddSingleton(identity);
        Services.AddSingleton(TestClients.Projects(handler, identity));
        Services.AddSingleton(TestClients.Tasks(handler, identity));
        Services.AddSingleton<Taskify.Security.Users.IUserDirectory>(new FakeUserDirectory());
        Services.AddSingleton<Taskify.Security.Audit.IAuditLogger>(audit);
        Services.AddSingleton<CurrentUserService>();

        return Render<BoardPage>(parameters => parameters.Add(p => p.ProjectId, ProjectId));
    }

    private static TaskSummaryDto Task(string title, TaskStatus status, Guid? assignee, DateTimeOffset? created = null) =>
        new(Guid.NewGuid(), ProjectId, title, status, assignee, 0, created ?? Day(2), Day(2));

    [Fact]
    public void Four_columns_are_shown_in_the_order_To_Do_In_Progress_In_Review_Done()
    {
        var cut = RenderBoard(SeedIds.Priya, Task("A", TaskStatus.Done, null));

        var headings = cut.FindAll("section.board-column h2").Select(h => h.TextContent.Trim().Split('\n')[0].Trim()).ToList();

        Assert.Equal(["To Do", "In Progress", "In Review", "Done"], headings.Select(h => h.Replace(" ", " ")).Select(h => string.Concat(h.TakeWhile(c => !char.IsDigit(c))).Trim()));
    }

    [Fact]
    public void Each_task_appears_in_exactly_one_column_with_its_title_and_assignee()
    {
        var cut = RenderBoard(
            SeedIds.Priya,
            Task("Design login", TaskStatus.ToDo, SeedIds.Jordan),
            Task("Write docs", TaskStatus.InProgress, null),
            Task("Review PR", TaskStatus.InReview, SeedIds.Liam),
            Task("Ship it", TaskStatus.Done, SeedIds.Maya));

        var columns = cut.FindAll("section.board-column");
        Assert.Equal(4, columns.Count);
        Assert.All(columns, column => Assert.Single(column.QuerySelectorAll("article.task-card")));

        Assert.Contains("Design login", columns[0].TextContent);
        Assert.Contains("Jordan Lee", columns[0].TextContent);
        Assert.Contains("Write docs", columns[1].TextContent);
        Assert.Contains("Unassigned", columns[1].TextContent);
        Assert.Contains("Review PR", columns[2].TextContent);
        Assert.Contains("Ship it", columns[3].TextContent);
        Assert.Contains("Maya Chen", columns[3].TextContent);
    }

    [Fact]
    public void Only_cards_assigned_to_the_selected_user_are_highlighted_with_a_text_label_too()
    {
        var cut = RenderBoard(
            SeedIds.Priya,
            Task("Mine", TaskStatus.ToDo, SeedIds.Priya),
            Task("Theirs", TaskStatus.ToDo, SeedIds.Jordan),
            Task("Nobody", TaskStatus.ToDo, null));

        var mine = cut.FindAll("article.card--mine");

        var card = Assert.Single(mine);
        Assert.Contains("Mine", card.TextContent);
        Assert.Contains("Assigned to you", card.TextContent);
        Assert.Single(cut.FindAll(".mine-label"));
    }

    [Fact]
    public void A_project_with_no_tasks_shows_four_empty_columns_and_a_prompt_to_add_the_first_task()
    {
        var cut = RenderBoard(SeedIds.Priya);

        Assert.Equal(4, cut.FindAll("section.board-column").Count);
        Assert.Empty(cut.FindAll("article.task-card"));
        Assert.Contains("Add the first task", cut.Markup);
    }

    [Fact]
    public void Task_titles_are_shown_as_plain_text_never_as_markup()
    {
        var cut = RenderBoard(SeedIds.Priya, Task("<script>alert(1)</script>", TaskStatus.ToDo, null));

        Assert.Empty(cut.FindAll("article.task-card script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);
    }

    [Fact]
    public void Newer_tasks_are_listed_first_within_a_column()
    {
        var cut = RenderBoard(
            SeedIds.Priya,
            Task("Older", TaskStatus.ToDo, null, Day(1)),
            Task("Newer", TaskStatus.ToDo, null, Day(5)));

        var titles = cut.FindAll("section.board-column:first-of-type .task-title").Select(t => t.TextContent.Trim()).ToList();

        Assert.Equal(["Newer", "Older"], titles);
    }

    [Fact]
    public void An_unknown_project_shows_not_found_with_a_way_back()
    {
        var identity = TestIdentity.For(SeedIds.Priya);
        handler.On("/api/users", JsonSerializer.Serialize(SampleUsers.All, ContractJson.Options));
        Services.AddSingleton(identity);
        Services.AddSingleton(TestClients.Projects(handler, identity));
        Services.AddSingleton(TestClients.Tasks(handler, identity));
        Services.AddSingleton<Taskify.Security.Users.IUserDirectory>(new FakeUserDirectory());
        Services.AddSingleton<Taskify.Security.Audit.IAuditLogger>(audit);
        Services.AddSingleton<CurrentUserService>();

        var cut = Render<BoardPage>(parameters => parameters.Add(p => p.ProjectId, Guid.NewGuid()));

        Assert.Contains("not found", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/projects", cut.Markup);
    }
}
