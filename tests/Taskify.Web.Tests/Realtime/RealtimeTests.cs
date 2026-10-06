using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Taskify.Contracts;
using Taskify.Web.Services;
using Taskify.Web.Services.ApiClients;
using Taskify.Web.Tests.Support;
using BoardPage = Taskify.Web.Components.Pages.Board;
using ProjectsPage = Taskify.Web.Components.Pages.Projects;
using TaskDetailsPage = Taskify.Web.Components.Pages.TaskDetails;

namespace Taskify.Web.Tests.Realtime;

/// <summary>
/// Realtime signals (spec FR-026, research R5): every update is a re-fetch from the REST API, at most one per second per
/// screen, and a resync makes every open screen re-fetch.
/// </summary>
public sealed class RealtimeTests : BunitContext
{
    private static readonly Guid ProjectId = SeedIds.MobileAppLaunch;
    private static readonly Guid TaskId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static DateTimeOffset Day(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);

    private readonly StubHandler handler = new();
    private readonly FakeTimeProvider time = new();
    private readonly FakeRealtimeBoard realtime;

    /// <summary>Registers the clients, the fake clock and the fake realtime service.</summary>
    public RealtimeTests()
    {
        var identity = TestIdentity.For(SeedIds.Priya);
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton(identity);
        Services.AddSingleton(TestClients.Projects(handler, identity));
        Services.AddSingleton(TestClients.Tasks(handler, identity));
        Services.AddSingleton<Taskify.Security.Users.IUserDirectory>(new FakeUserDirectory());
        Services.AddSingleton<Taskify.Security.Audit.IAuditLogger>(new RecordingAuditLogger());
        Services.AddSingleton<CurrentUserService>();
        realtime = Services.AddFakeRealtime();

        handler.On("/api/users", Json(SampleUsers.All));
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, ContractJson.Options);

    private static TaskSummaryDto Summary(string title) =>
        new(Guid.NewGuid(), ProjectId, title, TaskStatus.ToDo, SeedIds.Jordan, 0, Day(2), Day(2));

    private int Fetches(string pathAndQuery) => handler.Sent.Count(s => s.Method == "GET" && s.PathAndQuery == pathAndQuery);

    private static string TasksPath => $"/api/tasks?projectId={ProjectId}";

    private IRenderedComponent<BoardPage> RenderBoard(params TaskSummaryDto[] tasks)
    {
        handler
            .On($"/api/projects/{ProjectId}", Json(new ProjectDto(ProjectId, "Mobile App Launch", null, SeedIds.Maya, Day(1))))
            .On(TasksPath, Json(tasks));
        return Render<BoardPage>(p => p.Add(c => c.ProjectId, ProjectId));
    }

    private IRenderedComponent<TaskDetailsPage> RenderTaskDetails()
    {
        handler
            .On($"/api/tasks/{TaskId}", Json(new TaskDetailDto(TaskId, ProjectId, "Design login", null, TaskStatus.ToDo, null, SeedIds.Priya, 0, Day(2), Day(2))))
            .On($"/api/tasks/{TaskId}/history", "[]")
            .On($"/api/tasks/{TaskId}/comments", "[]");
        return Render<TaskDetailsPage>(p => p.Add(c => c.ProjectId, ProjectId).Add(c => c.TaskId, TaskId));
    }

    private IRenderedComponent<ProjectsPage> RenderProjects()
    {
        handler.On("/api/projects", "[]");
        return Render<ProjectsPage>();
    }

    [Fact]
    public void A_board_signal_makes_the_board_re_fetch_and_show_the_new_data()
    {
        var cut = RenderBoard(Summary("Old task"));
        Assert.Contains("Old task", cut.Markup);
        Assert.Equal([$"project:{ProjectId}"], realtime.Subscribed);

        handler.On(TasksPath, Json(new[] { Summary("Old task"), Summary("Added elsewhere") }));
        realtime.Raise($"project:{ProjectId}");

        cut.WaitForAssertion(() => Assert.Contains("Added elsewhere", cut.Markup));
        Assert.Equal(2, Fetches(TasksPath));
    }

    [Fact]
    public void Ten_signals_within_a_second_cause_at_most_two_re_fetches_and_the_trailing_one_runs_when_the_window_closes()
    {
        var cut = RenderBoard(Summary("A"));
        Assert.Equal(1, Fetches(TasksPath));   // the first load

        for (var i = 0; i < 10; i++)
        {
            realtime.Raise($"project:{ProjectId}");
            time.Advance(TimeSpan.FromMilliseconds(50));
        }

        cut.WaitForAssertion(() => Assert.Equal(2, Fetches(TasksPath)));   // one at once, nine merged and still waiting
        Assert.Equal(2, Fetches(TasksPath));

        time.Advance(TimeSpan.FromSeconds(1));
        cut.WaitForAssertion(() => Assert.Equal(3, Fetches(TasksPath)));   // the one trailing re-fetch
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, Fetches(TasksPath));                               // nothing more is pending
    }

    [Fact]
    public void A_resync_makes_the_board_re_fetch()
    {
        var cut = RenderBoard(Summary("A"));

        realtime.Resync();

        cut.WaitForAssertion(() => Assert.Equal(2, Fetches(TasksPath)));
    }

    [Fact]
    public void A_resync_makes_the_task_page_re_fetch_the_task_the_history_and_the_comments()
    {
        var cut = RenderTaskDetails();
        Assert.Equal([$"task:{TaskId}"], realtime.Subscribed);
        Assert.Equal(1, Fetches($"/api/tasks/{TaskId}"));
        Assert.Equal(1, Fetches($"/api/tasks/{TaskId}/history"));
        Assert.Equal(1, Fetches($"/api/tasks/{TaskId}/comments"));

        realtime.Resync();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, Fetches($"/api/tasks/{TaskId}"));
            Assert.Equal(2, Fetches($"/api/tasks/{TaskId}/history"));
            Assert.Equal(2, Fetches($"/api/tasks/{TaskId}/comments"));
        });
    }

    [Fact]
    public void A_task_signal_refreshes_the_page_but_keeps_what_the_user_typed_in_the_open_edit_form()
    {
        var cut = RenderTaskDetails();
        cut.Find("button.edit-task").Click();
        cut.Find("#task-title").Input("My unsaved title");

        handler.On($"/api/tasks/{TaskId}", Json(new TaskDetailDto(TaskId, ProjectId, "Changed elsewhere", null, TaskStatus.InProgress, null, SeedIds.Priya, 0, Day(2), Day(3))));
        realtime.Raise($"task:{TaskId}");

        cut.WaitForAssertion(() => Assert.Equal(2, Fetches($"/api/tasks/{TaskId}")));
        cut.WaitForAssertion(() => Assert.Contains("In Progress", cut.Find("dl.task-facts").TextContent));   // read-only parts refreshed
        Assert.Equal("My unsaved title", cut.Find("#task-title").GetAttribute("value"));
    }

    [Fact]
    public void A_resync_makes_the_project_list_re_fetch()
    {
        var cut = RenderProjects();
        Assert.Equal(["projects"], realtime.Subscribed);
        Assert.Equal(1, Fetches("/api/projects"));

        handler.On("/api/projects", Json(new[] { new ProjectDto(Guid.NewGuid(), "Created elsewhere", null, SeedIds.Maya, Day(3)) }));
        realtime.Resync();

        cut.WaitForAssertion(() => Assert.Contains("Created elsewhere", cut.Markup));
        Assert.Equal(2, Fetches("/api/projects"));
    }

    [Fact]
    public void Each_page_leaves_its_group_when_it_is_disposed()
    {
        var board = RenderBoard(Summary("A"));
        var details = RenderTaskDetails();
        var projects = RenderProjects();
        Assert.Equal(3, realtime.ActiveCount);

        board.Instance.Dispose();
        Assert.Equal(2, realtime.ActiveCount);
        details.Instance.Dispose();
        projects.Instance.Dispose();
        Assert.Equal(0, realtime.ActiveCount);
    }

    [Fact]
    public void A_signal_after_the_page_is_disposed_causes_no_re_fetch()
    {
        var cut = RenderBoard(Summary("A"));
        cut.Instance.Dispose();

        realtime.Raise($"project:{ProjectId}");
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Fetches(TasksPath));
    }

    [Fact]
    public async Task The_refresher_runs_the_first_signal_at_once_and_merges_the_rest_into_one_trailing_run()
    {
        var runs = 0;
        using var refresher = new CoalescingRefresher(() => { runs++; return Task.CompletedTask; }, time);

        for (var i = 0; i < 10; i++)
        {
            refresher.Signal();
        }

        Assert.Equal(1, runs);
        time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => runs == 2);
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);   // let the second window start
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, runs);

        refresher.Signal();   // a quiet window has passed, so this one runs at once again
        await WaitUntilAsync(() => runs == 3);
    }

    [Fact]
    public async Task The_refresher_survives_a_failing_re_fetch_and_reports_it()
    {
        var runs = 0;
        var errors = 0;
        using var refresher = new CoalescingRefresher(() => { runs++; throw new InvalidOperationException("boom"); }, time, onError: _ => errors++);

        refresher.Signal();
        time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);   // the window closes and the refresher goes idle
        refresher.Signal();

        Assert.Equal(2, runs);
        Assert.Equal(2, errors);
    }

    [Fact]
    public void The_recent_set_ignores_a_repeated_key_and_forgets_the_oldest_when_full()
    {
        var set = new BoundedRecentSet<int>(3);

        Assert.True(set.TryAdd(1));
        Assert.False(set.TryAdd(1));
        Assert.True(set.TryAdd(2));
        Assert.True(set.TryAdd(3));
        Assert.True(set.TryAdd(4));   // pushes 1 out
        Assert.Equal(3, set.Count);
        Assert.True(set.TryAdd(1));   // forgotten, so it is new again
        Assert.False(set.TryAdd(4));
    }

    [Fact]
    public void The_recent_set_keeps_up_to_1000_event_IDs()
    {
        var set = new BoundedRecentSet<Guid>(1000);
        var first = Guid.NewGuid();
        set.TryAdd(first);
        for (var i = 0; i < 999; i++)
        {
            set.TryAdd(Guid.NewGuid());
        }

        Assert.False(set.TryAdd(first));   // still remembered after 1,000 keys
        Assert.Equal(1000, set.Count);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10, Xunit.TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
