using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using Taskify.Web.Components.Board;
using Taskify.Web.Components.Shared;
using Taskify.Web.Services.ApiClients;
using Taskify.Web.Tests.Support;
using BoardPage = Taskify.Web.Components.Pages.Board;

namespace Taskify.Web.Tests.Board;

/// <summary>Moving tasks from the UI (spec FR-012; User Story 2; plan research R6: drag and keyboard menu).</summary>
public sealed class MoveTests : BunitContext
{
    private static readonly Guid ProjectId = SeedIds.MobileAppLaunch;
    private static readonly Guid TaskId = Guid.Parse("33333333-3333-3333-0001-000000000001");

    private readonly StubHandler handler = new();
    private readonly RecordingAuditLogger audit = new();

    private static DateTimeOffset Day(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);

    private static TaskSummaryDto Item(Guid id, string title, TaskStatus status) =>
        new(id, ProjectId, title, status, SeedIds.Jordan, 0, Day(2), Day(2));

    private static string Json(object value) => JsonSerializer.Serialize(value, ContractJson.Options);

    private IRenderedComponent<BoardPage> RenderBoard(params TaskSummaryDto[] tasks)
    {
        var identity = TestIdentity.For(SeedIds.Priya);
        handler
            .On("/api/users", Json(SampleUsers.All))
            .On($"/api/projects/{ProjectId}", Json(new ProjectDto(ProjectId, "Mobile App Launch", null, SeedIds.Maya, Day(1))))
            .On($"/api/tasks?projectId={ProjectId}", Json(tasks));

        Services.AddSingleton(identity);
        Services.AddSingleton(TestClients.Projects(handler, identity));
        Services.AddSingleton(TestClients.Tasks(handler, identity));
        Services.AddSingleton<Taskify.Security.Users.IUserDirectory>(new FakeUserDirectory());
        Services.AddSingleton<Taskify.Security.Audit.IAuditLogger>(audit);
        Services.AddSingleton<Web.Services.CurrentUserService>();
        Services.AddFakeRealtime();

        return Render<BoardPage>(parameters => parameters.Add(p => p.ProjectId, ProjectId));
    }

    private void RespondToMove(Guid taskId, TaskStatus from, TaskStatus to, bool changed = true) =>
        handler.On($"/api/tasks/{taskId}/moves", Json(new MoveTaskResultDto(
            changed,
            new TaskDetailDto(taskId, ProjectId, "Login page", null, to, SeedIds.Jordan, SeedIds.Maya, 0, Day(2), Day(5)),
            changed ? new StatusChangeDto(Guid.NewGuid(), taskId, from, to, SeedIds.Priya, Day(5)) : null)));

    private static List<string> TitlesIn(IRenderedComponent<BoardPage> cut, TaskStatus status) =>
        cut.Find($"section[data-status=\"{status}\"]").QuerySelectorAll(".task-title").Select(e => e.TextContent.Trim()).ToList();

    private static void OpenMenuAndChoose(IRenderedComponent<BoardPage> cut, string taskTitle, string column)
    {
        var card = cut.FindAll("article.task-card").Single(c => c.QuerySelector(".task-title")!.TextContent.Trim() == taskTitle);
        card.QuerySelector("button.move-toggle")!.Click();
        cut.FindAll("article.task-card").Single(c => c.QuerySelector(".task-title")!.TextContent.Trim() == taskTitle)
            .QuerySelectorAll("[role=menuitem]").Single(i => i.TextContent.Trim() == column).Click();
    }

    [Fact]
    public void The_move_menu_lists_the_three_other_columns_and_not_the_current_one()
    {
        var chosen = new List<TaskStatus>();
        var cut = Render<MoveToMenu>(p => p
            .Add(c => c.Current, TaskStatus.InProgress)
            .Add(c => c.TaskTitle, "Login page")
            .Add(c => c.OnMove, (TaskStatus s) => chosen.Add(s)));

        Assert.Empty(cut.FindAll("[role=menuitem]"));
        cut.Find("button.move-toggle").Click();

        var items = cut.FindAll("[role=menuitem]").Select(i => i.TextContent.Trim()).ToList();
        Assert.Equal(["To Do", "In Review", "Done"], items);

        cut.FindAll("[role=menuitem]")[2].Click();
        Assert.Equal([TaskStatus.Done], chosen);
        Assert.Empty(cut.FindAll("[role=menuitem]")); // the menu closes after a choice
    }

    [Fact]
    public void The_move_menu_is_keyboard_operable_and_announced_to_assistive_technology()
    {
        var cut = Render<MoveToMenu>(p => p
            .Add(c => c.Current, TaskStatus.ToDo)
            .Add(c => c.TaskTitle, "Login page"));
        var toggle = cut.Find("button.move-toggle");

        Assert.Equal("button", toggle.TagName.ToLowerInvariant()); // a real button: reachable and activatable with Enter and Space
        Assert.Equal("menu", toggle.GetAttribute("aria-haspopup"));
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Contains("Login page", toggle.GetAttribute("aria-label"));

        toggle.Click();
        Assert.Equal("true", cut.Find("button.move-toggle").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("[role=menu]"));

        cut.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role=menu]"));
    }

    [Fact]
    public void Choosing_a_column_in_the_menu_moves_the_card_and_tells_the_api()
    {
        RespondToMove(TaskId, TaskStatus.ToDo, TaskStatus.Done);
        var cut = RenderBoard(Item(TaskId, "Login page", TaskStatus.ToDo));

        OpenMenuAndChoose(cut, "Login page", "Done");

        cut.WaitForAssertion(() => Assert.Equal(["Login page"], TitlesIn(cut, TaskStatus.Done)));
        Assert.Empty(TitlesIn(cut, TaskStatus.ToDo));
        var move = Assert.Single(handler.Sent, s => s.Method == "POST");
        Assert.Equal($"/api/tasks/{TaskId}/moves", move.PathAndQuery);
        Assert.Equal("""{"toStatus":"Done"}""", move.Body);
        Assert.Empty(cut.FindAll(".error-banner"));
    }

    [Fact]
    public void Dragging_a_card_onto_another_column_does_the_same_as_the_menu()
    {
        RespondToMove(TaskId, TaskStatus.ToDo, TaskStatus.InReview);
        var cut = RenderBoard(Item(TaskId, "Login page", TaskStatus.ToDo));

        cut.Find("article.task-card").TriggerEvent("ondragstart", new DragEventArgs());
        cut.Find("section[data-status=\"InReview\"]").TriggerEvent("ondrop", new DragEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["Login page"], TitlesIn(cut, TaskStatus.InReview)));
        var move = Assert.Single(handler.Sent, s => s.Method == "POST");
        Assert.Equal("""{"toStatus":"InReview"}""", move.Body);
    }

    [Fact]
    public void Dropping_a_card_back_in_its_own_column_changes_nothing_and_sends_nothing()
    {
        var cut = RenderBoard(Item(TaskId, "Login page", TaskStatus.InProgress));

        cut.Find("article.task-card").TriggerEvent("ondragstart", new DragEventArgs());
        cut.Find("section[data-status=\"InProgress\"]").TriggerEvent("ondrop", new DragEventArgs());

        Assert.Equal(["Login page"], TitlesIn(cut, TaskStatus.InProgress));
        Assert.DoesNotContain(handler.Sent, s => s.Method == "POST");
    }

    [Fact]
    public void A_drop_with_nothing_being_dragged_does_nothing()
    {
        var cut = RenderBoard(Item(TaskId, "Login page", TaskStatus.ToDo));

        cut.Find("section[data-status=\"Done\"]").TriggerEvent("ondrop", new DragEventArgs());

        Assert.Equal(["Login page"], TitlesIn(cut, TaskStatus.ToDo));
        Assert.DoesNotContain(handler.Sent, s => s.Method == "POST");
    }

    [Fact]
    public void A_rejected_move_snaps_the_card_back_and_shows_why()
    {
        handler.On($"/api/tasks/{TaskId}/moves", "{}", HttpStatusCode.TooManyRequests);
        var cut = RenderBoard(Item(TaskId, "Login page", TaskStatus.ToDo));

        OpenMenuAndChoose(cut, "Login page", "Done");

        cut.WaitForAssertion(() => Assert.Contains("Too many requests, please wait a moment", cut.Find(".error-banner").TextContent));
        Assert.Equal(["Login page"], TitlesIn(cut, TaskStatus.ToDo));
        Assert.Empty(TitlesIn(cut, TaskStatus.Done));
    }

    [Fact]
    public void A_move_of_a_task_that_no_longer_exists_snaps_back_with_a_message()
    {
        handler.On($"/api/tasks/{TaskId}/moves", "{}", HttpStatusCode.NotFound);
        var cut = RenderBoard(Item(TaskId, "Login page", TaskStatus.ToDo));

        OpenMenuAndChoose(cut, "Login page", "InReview".Replace("InReview", "In Review"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".error-banner")));
        Assert.Equal(["Login page"], TitlesIn(cut, TaskStatus.ToDo));
    }

    [Fact]
    public void Status_history_shows_newest_first_who_moved_it_from_where_to_where_and_when()
    {
        var older = new StatusChangeDto(Guid.NewGuid(), TaskId, TaskStatus.ToDo, TaskStatus.InProgress, SeedIds.Priya, Day(3));
        var newer = new StatusChangeDto(Guid.NewGuid(), TaskId, TaskStatus.InProgress, TaskStatus.Done, SeedIds.Liam, Day(4));
        var users = SampleUsers.All.ToDictionary(u => u.Id);

        var cut = Render<StatusHistory>(p => p
            .Add(c => c.Items, [older, newer])
            .Add(c => c.Users, users));

        var entries = cut.FindAll("li").Select(li => li.TextContent).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains("Liam Novak", entries[0]);
        Assert.Contains("In Progress", entries[0]);
        Assert.Contains("Done", entries[0]);
        Assert.Contains("Priya Patel", entries[1]);
        Assert.Contains("To Do", entries[1]);
        Assert.Contains("4 Sep 2026", entries[0]);
    }

    [Fact]
    public void Status_history_with_no_moves_says_so()
    {
        var cut = Render<StatusHistory>(p => p
            .Add(c => c.Items, [])
            .Add(c => c.Users, new Dictionary<Guid, Taskify.Security.Users.UserInfo>()));

        Assert.Empty(cut.FindAll("li"));
        Assert.Contains("No moves yet", cut.Markup);
    }
}
