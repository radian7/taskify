using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using Taskify.Web.Components.Shared;
using Taskify.Web.Services.ApiClients;
using Taskify.Web.Tests.Support;
using BoardPage = Taskify.Web.Components.Pages.Board;

namespace Taskify.Web.Tests.Forms;

/// <summary>Creating projects and tasks, assigning and editing them from the UI (spec FR-006, FR-009 to FR-011; User Story 3).</summary>
public sealed class FormTests : BunitContext
{
    // 👨‍👩‍👧‍👦: one user-perceived character made of 11 UTF-16 code units.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    private static readonly Guid ProjectId = SeedIds.MobileAppLaunch;
    private static readonly Guid NewTaskId = Guid.Parse("33333333-3333-3333-0009-000000000001");

    private readonly StubHandler handler = new();

    private static DateTimeOffset Day(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);

    private static string Json(object value) => JsonSerializer.Serialize(value, ContractJson.Options);

    private void RegisterClients(Guid? user = null)
    {
        var identity = TestIdentity.For(user ?? SeedIds.Priya);
        Services.AddSingleton(identity);
        Services.AddSingleton(TestClients.Projects(handler, identity));
        Services.AddSingleton(TestClients.Tasks(handler, identity));
        Services.AddSingleton<Taskify.Security.Users.IUserDirectory>(new FakeUserDirectory());
        Services.AddSingleton<Taskify.Security.Audit.IAuditLogger>(new RecordingAuditLogger());
        Services.AddSingleton<Web.Services.CurrentUserService>();
    }

    private static TaskDetailDto Detail(string title, TaskStatus status = TaskStatus.ToDo, Guid? assignee = null, string? description = null) =>
        new(NewTaskId, ProjectId, title, description, status, assignee, SeedIds.Priya, 0, Day(2), Day(2));

    private static string ProblemJson(string field, string message) =>
        $$$"""{"title":"One or more validation errors occurred.","status":400,"errors":{"{{{field}}}":["{{{message}}}"]}}""";

    // ------------------------------------------------------------------------------------------------ project form

    [Fact]
    public void The_project_form_has_name_and_description_with_live_character_counters()
    {
        RegisterClients();
        var cut = Render<CreateProjectForm>();

        Assert.Equal("0 / 100", cut.FindAll(".char-counter")[0].TextContent);
        Assert.Equal("0 / 1000", cut.FindAll(".char-counter")[1].TextContent);

        cut.Find("#project-name").Input("QA Demo");
        cut.Find("#project-description").Input("For the demo");

        Assert.Equal("7 / 100", cut.FindAll(".char-counter")[0].TextContent);
        Assert.Equal("12 / 1000", cut.FindAll(".char-counter")[1].TextContent);
    }

    [Fact]
    public void Submitting_a_valid_project_posts_it_and_reports_the_new_project()
    {
        var created = new ProjectDto(Guid.NewGuid(), "QA Demo", null, SeedIds.Priya, Day(4));
        handler.On("/api/projects", Json(created), HttpStatusCode.Created);
        RegisterClients();
        ProjectDto? reported = null;
        var cut = Render<CreateProjectForm>(p => p.Add(c => c.OnCreated, (ProjectDto project) => reported = project));

        cut.Find("#project-name").Input("QA Demo");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(created, reported));
        var post = Assert.Single(handler.Sent, s => s.Method == "POST");
        Assert.Equal("/api/projects", post.PathAndQuery);
        Assert.Equal("""{"name":"QA Demo","description":""}""", post.Body);
        Assert.Empty(cut.FindAll(".error-banner"));
    }

    [Fact]
    public void A_rejected_project_shows_the_reason_and_keeps_what_the_user_typed()
    {
        handler.On("/api/projects", ProblemJson("name", "Name must be between 1 and 100 characters."), HttpStatusCode.BadRequest);
        RegisterClients();
        var reported = false;
        var cut = Render<CreateProjectForm>(p => p.Add(c => c.OnCreated, (ProjectDto _) => reported = true));

        cut.Find("#project-name").Input("   ");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Name must be between 1 and 100 characters.", cut.Find(".error-banner").TextContent));
        Assert.False(reported);
        Assert.Equal("   ", cut.Find("#project-name").GetAttribute("value"));
    }

    [Fact]
    public void The_project_form_can_be_cancelled()
    {
        RegisterClients();
        var cancelled = false;
        var cut = Render<CreateProjectForm>(p => p.Add(c => c.OnCancel, () => cancelled = true));

        cut.Find("button.secondary").Click();

        Assert.True(cancelled);
        Assert.Empty(handler.Sent);
    }

    // -------------------------------------------------------------------------------------------------- task form

    [Fact]
    public void The_assignee_picker_offers_unassigned_and_the_five_users()
    {
        RegisterClients();
        var cut = Render<TaskForm>(p => p.Add(c => c.ProjectId, ProjectId).Add(c => c.Users, SampleUsers.All));

        var options = cut.FindAll("#task-assignee option").Select(o => o.TextContent.Trim()).ToList();

        Assert.Equal(6, options.Count);
        Assert.Equal("Unassigned", options[0]);
        Assert.Equal(["Maya Chen", "Jordan Lee", "Liam Novak", "Priya Patel", "Tomasz Wiśniewski"], options.Skip(1));
    }

    [Fact]
    public void The_counter_counts_what_the_user_sees_so_two_hundred_emoji_fit_and_two_hundred_and_one_do_not()
    {
        RegisterClients();
        var cut = Render<TaskForm>(p => p.Add(c => c.ProjectId, ProjectId).Add(c => c.Users, SampleUsers.All));

        cut.Find("#task-title").Input(string.Concat(Enumerable.Repeat(FamilyEmoji, 200)));
        Assert.Equal("200 / 200", cut.FindAll(".char-counter")[0].TextContent);
        Assert.Empty(cut.FindAll(".char-counter--over"));

        cut.Find("#task-title").Input(string.Concat(Enumerable.Repeat(FamilyEmoji, 201)));
        Assert.Equal("201 / 200", cut.FindAll(".char-counter")[0].TextContent);
        Assert.Single(cut.FindAll(".char-counter--over"));
    }

    [Fact]
    public void The_counter_ignores_whitespace_around_the_text_the_way_the_server_does()
    {
        RegisterClients();
        var cut = Render<TaskForm>(p => p.Add(c => c.ProjectId, ProjectId).Add(c => c.Users, SampleUsers.All));

        cut.Find("#task-title").Input("   Hello   ");

        Assert.Equal("5 / 200", cut.FindAll(".char-counter")[0].TextContent);
    }

    [Fact]
    public void Creating_a_task_posts_project_title_description_and_assignee_and_reports_the_new_task()
    {
        var created = Detail("Write the spec", assignee: SeedIds.Jordan, description: "Details");
        handler.On("/api/tasks", Json(created), HttpStatusCode.Created);
        RegisterClients();
        TaskDetailDto? reported = null;
        var cut = Render<TaskForm>(p => p
            .Add(c => c.ProjectId, ProjectId)
            .Add(c => c.Users, SampleUsers.All)
            .Add(c => c.OnSaved, (TaskDetailDto task) => reported = task));

        cut.Find("#task-title").Input("Write the spec");
        cut.Find("#task-description").Input("Details");
        cut.Find("#task-assignee").Change(SeedIds.Jordan.ToString());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(created, reported));
        var post = Assert.Single(handler.Sent, s => s.Method == "POST");
        Assert.Equal("/api/tasks", post.PathAndQuery);
        using var body = JsonDocument.Parse(post.Body);
        Assert.Equal(ProjectId, body.RootElement.GetProperty("projectId").GetGuid());
        Assert.Equal("Write the spec", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("Details", body.RootElement.GetProperty("description").GetString());
        Assert.Equal(SeedIds.Jordan, body.RootElement.GetProperty("assigneeUserId").GetGuid());
    }

    [Fact]
    public void A_task_can_be_created_without_an_assignee()
    {
        handler.On("/api/tasks", Json(Detail("No owner")), HttpStatusCode.Created);
        RegisterClients();
        var cut = Render<TaskForm>(p => p.Add(c => c.ProjectId, ProjectId).Add(c => c.Users, SampleUsers.All));

        cut.Find("#task-title").Input("No owner");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(handler.Sent, s => s.Method == "POST"));
        using var body = JsonDocument.Parse(handler.Sent.Single(s => s.Method == "POST").Body);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("assigneeUserId").ValueKind);
    }

    [Fact]
    public void A_rejected_task_shows_the_reason_and_creates_nothing()
    {
        handler.On("/api/tasks", ProblemJson("title", "Title must be between 1 and 200 characters."), HttpStatusCode.BadRequest);
        RegisterClients();
        var reported = false;
        var cut = Render<TaskForm>(p => p
            .Add(c => c.ProjectId, ProjectId)
            .Add(c => c.Users, SampleUsers.All)
            .Add(c => c.OnSaved, (TaskDetailDto _) => reported = true));

        cut.Find("#task-title").Input(new string('t', 201));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Title must be between 1 and 200 characters.", cut.Find(".error-banner").TextContent));
        Assert.False(reported);
    }

    [Fact]
    public void Editing_starts_from_the_current_values_and_puts_the_changes()
    {
        var existing = Detail("Old title", TaskStatus.Done, SeedIds.Liam, "old text");
        var updated = existing with { Title = "New title", AssigneeUserId = null, Description = "old text" };
        handler.On($"/api/tasks/{existing.Id}", Json(updated));
        RegisterClients();
        TaskDetailDto? reported = null;
        var cut = Render<TaskForm>(p => p
            .Add(c => c.Existing, existing)
            .Add(c => c.Users, SampleUsers.All)
            .Add(c => c.OnSaved, (TaskDetailDto task) => reported = task));

        Assert.Equal("Old title", cut.Find("#task-title").GetAttribute("value"));
        Assert.Equal("old text", cut.Find("#task-description").GetAttribute("value"));
        Assert.Equal(SeedIds.Liam.ToString(), cut.Find("#task-assignee option[selected]").GetAttribute("value"));
        Assert.Contains("Edit task", cut.Markup);

        cut.Find("#task-title").Input("New title");
        cut.Find("#task-assignee").Change(string.Empty);   // unassign
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(updated, reported));
        var put = Assert.Single(handler.Sent, s => s.Method == "PUT");
        Assert.Equal($"/api/tasks/{existing.Id}", put.PathAndQuery);
        using var body = JsonDocument.Parse(put.Body);
        Assert.Equal("New title", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("assigneeUserId").ValueKind);   // explicit null unassigns
        Assert.Equal("old text", body.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void A_task_in_the_done_column_can_be_edited_like_any_other()
    {
        var existing = Detail("Finished", TaskStatus.Done, SeedIds.Liam);
        handler.On($"/api/tasks/{existing.Id}", Json(existing with { Title = "Finished and tidied" }));
        RegisterClients();
        var cut = Render<TaskForm>(p => p.Add(c => c.Existing, existing).Add(c => c.Users, SampleUsers.All));

        cut.Find("#task-title").Input("Finished and tidied");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(handler.Sent, s => s.Method == "PUT"));
    }

    // -------------------------------------------------------------------------------------------------- the board

    [Fact]
    public void A_task_added_on_the_board_appears_in_to_do_and_markup_in_its_title_stays_plain_text()
    {
        const string hostile = "<script>alert(1)</script>";
        handler
            .On("/api/users", Json(SampleUsers.All))
            .On($"/api/projects/{ProjectId}", Json(new ProjectDto(ProjectId, "Mobile App Launch", null, SeedIds.Maya, Day(1))))
            .On($"/api/tasks?projectId={ProjectId}", "[]")
            .On("/api/tasks", Json(Detail(hostile, assignee: SeedIds.Priya)), HttpStatusCode.Created);
        RegisterClients();
        var cut = Render<BoardPage>(p => p.Add(c => c.ProjectId, ProjectId));
        Assert.Contains("Add the first task", cut.Markup);

        cut.Find("button.add-task").Click();
        cut.Find("#task-title").Input(hostile);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var todo = cut.Find("section[data-status=\"ToDo\"]");
            Assert.Equal(1, todo.QuerySelectorAll("article.task-card").Length);
        });
        Assert.Empty(cut.FindAll("article.task-card script"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", cut.Markup);
        Assert.Empty(cut.FindAll("form.entity-form"));   // the form closes after saving
    }
}
