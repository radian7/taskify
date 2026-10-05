using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Creating projects and tasks, assigning and editing them (spec FR-006, FR-009 to FR-011, FR-019, FR-021; User Story 3;
/// contracts/projects-api.yaml and tasks-api.yaml). Every test creates its own project so it cannot disturb the sample data.
/// </summary>
/// <param name="app">The running application.</param>
[Collection(SeedData.Collection)]
public class CreateEditContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract ProjectsContract = OpenApiContract.Load("projects-api.yaml");
    private static readonly OpenApiContract TasksContract = OpenApiContract.Load("tasks-api.yaml");

    private HttpClient Projects(Guid user) => app.CreateClient("projects-api", TaskifyAppFixture.WebKey, user);

    private HttpClient Tasks(Guid user) => app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Esc(string text) => JsonSerializer.Serialize(text);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    private async Task<Guid> NewProjectAsync(Guid user, string? name = null)
    {
        using var client = Projects(user);
        using var response = await client.PostAsync("/api/projects", Json($$"""{"name":{{Esc(name ?? "Test project " + Guid.NewGuid().ToString("N")[..8])}}}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> NewTaskAsync(Guid user, Guid project, string title = "A task", string? description = null, Guid? assignee = null)
    {
        using var client = Tasks(user);
        var body = $$"""{"projectId":"{{project}}","title":{{Esc(title)}},"description":{{(description is null ? "null" : Esc(description))}},"assigneeUserId":{{(assignee is null ? "null" : $"\"{assignee}\"")}}}""";
        using var response = await client.PostAsync("/api/tasks", Json(body), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<List<JsonElement>> BoardAsync(HttpClient client, Guid project) =>
        JsonDocument.Parse(await client.GetStringAsync($"/api/tasks?projectId={project}", TestContext.Current.CancellationToken))
            .RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

    private async Task<List<string>> OutboxPayloadsAsync(string database, string type, string key, Guid id)
    {
        await using var connection = new NpgsqlConnection(await app.GetConnectionStringAsync(database));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT \"Payload\"::text FROM outbox_messages WHERE \"Type\" = @type AND \"Payload\"->>@key = @id ORDER BY \"OccurredAt\"", connection);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("id", id.ToString());
        var payloads = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads;
    }

    // ---------------------------------------------------------------------------------------------- projects

    [Fact]
    public async Task Creating_a_project_returns_201_with_a_location_and_the_acting_user_as_creator()
    {
        using var client = Projects(SeedIds.Priya);

        using var response = await client.PostAsync("/api/projects", Json("""{"name":"  QA Demo  ","description":"  For the demo.  "}"""), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(ProjectsContract.Validate("Project", body).IsValid, body);
        var project = JsonDocument.Parse(body).RootElement;
        Assert.Equal("QA Demo", project.GetProperty("name").GetString());              // trimmed
        Assert.Equal("For the demo.", project.GetProperty("description").GetString());
        Assert.Equal(SeedIds.Priya, project.GetProperty("createdByUserId").GetGuid());   // FR-004
        Assert.Equal($"/api/projects/{project.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.True((DateTimeOffset.UtcNow - project.GetProperty("createdAt").GetDateTimeOffset()).Duration() < TimeSpan.FromMinutes(5));
    }

    [Theory]
    [InlineData("""{"name":"No description"}""")]
    [InlineData("""{"name":"Null description","description":null}""")]
    [InlineData("""{"name":"Empty description","description":""}""")]
    [InlineData("""{"name":"Blank description","description":"    "}""")]
    public async Task A_missing_empty_or_blank_description_is_stored_as_none(string body)
    {
        using var client = Projects(app.NextUser());

        using var response = await client.PostAsync("/api/projects", Json(body), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(response)).GetProperty("description").ValueKind);
    }

    [Fact]
    public async Task A_new_project_is_listed_first_for_every_user_and_two_projects_may_share_a_name()
    {
        var first = await NewProjectAsync(app.NextUser(), "Shared name");
        var second = await NewProjectAsync(SeedIds.Jordan, "Shared name");
        using var liam = Projects(SeedIds.Liam);

        var list = JsonDocument.Parse(await liam.GetStringAsync("/api/projects", TestContext.Current.CancellationToken)).RootElement
            .EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(first, list);
        Assert.Contains(second, list);
        Assert.True(list.IndexOf(second) < list.IndexOf(first), "the newer project should be listed first");
    }

    [Theory]
    [InlineData("""{"name":""}""")]
    [InlineData("""{"name":"   "}""")]
    [InlineData("""{"description":"no name"}""")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":"ok","unknown":1}""")]
    [InlineData("""{"name":"ok","createdByUserId":"11111111-1111-1111-1111-000000000001"}""")]
    [InlineData("not json")]
    public async Task An_invalid_project_is_rejected_whole_with_a_clear_message_and_nothing_is_saved(string body)
    {
        using var client = Projects(app.NextUser());
        var before = (await client.GetStringAsync("/api/projects", TestContext.Current.CancellationToken));

        using var response = await client.PostAsync("/api/projects", Json(body), TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(ProjectsContract.Validate("ProblemDetails", problem).IsValid, problem);
        Assert.Equal(before, await client.GetStringAsync("/api/projects", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_name_of_a_hundred_characters_is_accepted_and_a_hundred_and_one_is_not_with_the_limit_in_the_message()
    {
        using var client = Projects(app.NextUser());

        using var ok = await client.PostAsync("/api/projects", Json($$"""{"name":"{{new string('n', 100)}}"}"""), TestContext.Current.CancellationToken);
        using var tooLong = await client.PostAsync("/api/projects", Json($$"""{"name":"{{new string('n', 101)}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var problem = await ReadAsync(tooLong);
        Assert.Equal("Name must be between 1 and 100 characters.", problem.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.DoesNotContain(new string('n', 101), problem.GetRawText());
    }

    [Fact]
    public async Task A_request_body_over_one_megabyte_gets_413_and_an_unknown_field_gets_400()
    {
        using var client = Projects(app.NextUser());

        using var tooLarge = await client.PostAsync("/api/projects", Json($$"""{"name":"x","description":"{{new string('d', 1_100_000)}}"}"""), TestContext.Current.CancellationToken);
        using var unknownField = await client.PostAsync("/api/projects", Json("""{"name":"x","extra":true}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownField.StatusCode);
    }

    [Fact]
    public async Task Creating_a_project_writes_one_event_with_its_id_and_name_and_never_the_description()
    {
        using var client = Projects(app.NextUser());
        using var response = await client.PostAsync("/api/projects", Json("""{"name":"Event project","description":"private details"}"""), TestContext.Current.CancellationToken);
        var id = (await ReadAsync(response)).GetProperty("id").GetGuid();

        var payload = Assert.Single(await OutboxPayloadsAsync("projectsdb", "ProjectCreated", "projectId", id));

        var json = JsonDocument.Parse(payload).RootElement;
        Assert.Equal("Event project", json.GetProperty("name").GetString());
        Assert.DoesNotContain("private details", payload);
        Assert.False(json.TryGetProperty("description", out _));
    }

    // ------------------------------------------------------------------------------------------------ tasks

    [Fact]
    public async Task Creating_a_task_puts_it_in_to_do_with_the_acting_user_as_creator_and_it_appears_on_the_board()
    {
        var project = await NewProjectAsync(SeedIds.Priya);
        using var client = Tasks(SeedIds.Tomasz);

        using var response = await client.PostAsync(
            "/api/tasks",
            Json($$"""{"projectId":"{{project}}","title":"  Write the spec  ","description":"  Details here.  ","assigneeUserId":"{{SeedIds.Jordan}}"}"""),
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(TasksContract.Validate("TaskDetail", body).IsValid, body);
        var task = JsonDocument.Parse(body).RootElement;
        Assert.Equal("ToDo", task.GetProperty("status").GetString());
        Assert.Equal("Write the spec", task.GetProperty("title").GetString());
        Assert.Equal("Details here.", task.GetProperty("description").GetString());
        Assert.Equal(SeedIds.Jordan, task.GetProperty("assigneeUserId").GetGuid());
        Assert.Equal(SeedIds.Tomasz, task.GetProperty("createdByUserId").GetGuid());
        Assert.Equal($"/api/tasks/{task.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);

        var board = await BoardAsync(client, project);
        var onBoard = Assert.Single(board);
        Assert.Equal("ToDo", onBoard.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_task_can_be_created_unassigned_and_a_later_edit_can_assign_reassign_and_unassign_it()
    {
        var project = await NewProjectAsync(app.NextUser());
        var task = await NewTaskAsync(app.NextUser(), project, "Assignable");
        var id = task.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, task.GetProperty("assigneeUserId").ValueKind);
        using var client = Tasks(SeedIds.Liam);   // any user may edit any task

        foreach (var (assignee, expected) in new (string?, string?)[] { ($"\"{SeedIds.Jordan}\"", SeedIds.Jordan.ToString()), ($"\"{SeedIds.Tomasz}\"", SeedIds.Tomasz.ToString()), ("null", null) })
        {
            using var response = await client.PutAsync($"/api/tasks/{id}", Json($$"""{"title":"Assignable","assigneeUserId":{{assignee}}}"""), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = await ReadAsync(response);
            Assert.True(TasksContract.Validate("TaskDetail", updated.GetRawText()).IsValid);
            Assert.Equal(expected, updated.GetProperty("assigneeUserId").ValueKind == JsonValueKind.Null ? null : updated.GetProperty("assigneeUserId").GetString());
        }
    }

    [Fact]
    public async Task Editing_replaces_title_and_description_and_a_task_in_done_is_edited_like_any_other()
    {
        var project = await NewProjectAsync(app.NextUser());
        var id = (await NewTaskAsync(app.NextUser(), project, "Before", "old")).GetProperty("id").GetGuid();
        using var client = Tasks(app.NextUser());
        (await client.PostAsync($"/api/tasks/{id}/moves", Json("""{"toStatus":"Done"}"""), TestContext.Current.CancellationToken)).Dispose();

        using var response = await client.PutAsync($"/api/tasks/{id}", Json("""{"title":"  After  ","description":"new","assigneeUserId":null}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await ReadAsync(response);
        Assert.Equal("After", task.GetProperty("title").GetString());
        Assert.Equal("new", task.GetProperty("description").GetString());
        Assert.Equal("Done", task.GetProperty("status").GetString());   // the column is not changed by an edit
        Assert.True(task.GetProperty("updatedAt").GetDateTimeOffset() >= task.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_leaves_the_task_and_its_events_alone()
    {
        var project = await NewProjectAsync(app.NextUser());
        var created = await NewTaskAsync(app.NextUser(), project, "Same", "same text", SeedIds.Jordan);
        var id = created.GetProperty("id").GetGuid();
        using var client = Tasks(app.NextUser());

        using var response = await client.PutAsync($"/api/tasks/{id}", Json($$"""{"title":"Same","description":"same text","assigneeUserId":"{{SeedIds.Jordan}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.GetProperty("updatedAt").GetDateTimeOffset(), (await ReadAsync(response)).GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Empty(await OutboxPayloadsAsync("tasksdb", "TaskUpdated", "taskId", id));
        Assert.Empty(await OutboxPayloadsAsync("tasksdb", "TaskAssigned", "taskId", id));
    }

    [Fact]
    public async Task A_task_in_an_unknown_project_or_with_an_unknown_assignee_is_422_and_nothing_is_saved()
    {
        var project = await NewProjectAsync(app.NextUser());
        using var client = Tasks(app.NextUser());

        using var unknownProject = await client.PostAsync("/api/tasks", Json($$"""{"projectId":"{{Guid.NewGuid()}}","title":"x"}"""), TestContext.Current.CancellationToken);
        using var unknownAssignee = await client.PostAsync("/api/tasks", Json($$"""{"projectId":"{{project}}","title":"x","assigneeUserId":"{{Guid.NewGuid()}}"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownProject.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownAssignee.StatusCode);
        Assert.True(TasksContract.Validate("ProblemDetails", await unknownProject.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Empty(await BoardAsync(client, project));
    }

    [Fact]
    public async Task Editing_with_an_unknown_assignee_is_422_and_an_unknown_task_is_404()
    {
        var project = await NewProjectAsync(app.NextUser());
        var id = (await NewTaskAsync(app.NextUser(), project, "Stays")).GetProperty("id").GetGuid();
        using var client = Tasks(app.NextUser());

        using var unknownAssignee = await client.PutAsync($"/api/tasks/{id}", Json($$"""{"title":"Changed","assigneeUserId":"{{Guid.NewGuid()}}"}"""), TestContext.Current.CancellationToken);
        using var unknownTask = await client.PutAsync($"/api/tasks/{Guid.NewGuid()}", Json("""{"title":"x","assigneeUserId":null}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownAssignee.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownTask.StatusCode);
        Assert.True(TasksContract.Validate("ProblemDetails", await unknownTask.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Equal("Stays", (await BoardAsync(client, project)).Single().GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("""{"projectId":"PROJECT","title":""}""")]
    [InlineData("""{"projectId":"PROJECT","title":"   "}""")]
    [InlineData("""{"projectId":"PROJECT"}""")]
    [InlineData("""{"projectId":"00000000-0000-0000-0000-000000000000","title":"x"}""")]
    [InlineData("""{"projectId":"PROJECT","title":"x","assigneeUserId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"projectId":"PROJECT","title":"x","status":"Done"}""")]
    [InlineData("""{"projectId":"PROJECT","title":"x","createdByUserId":"11111111-1111-1111-1111-000000000001"}""")]
    [InlineData("{}")]
    public async Task An_invalid_task_is_rejected_whole_and_nothing_is_saved(string template)
    {
        var project = await NewProjectAsync(app.NextUser());
        using var client = Tasks(app.NextUser());

        using var response = await client.PostAsync("/api/tasks", Json(template.Replace("PROJECT", project.ToString())), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(TasksContract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Empty(await BoardAsync(client, project));
    }

    [Theory]
    [InlineData("""{"title":""}""")]
    [InlineData("""{"title":"x"}""")]
    [InlineData("""{"assigneeUserId":null}""")]
    [InlineData("""{"title":"x","assigneeUserId":null,"projectId":"11111111-1111-1111-1111-000000000001"}""")]
    public async Task An_invalid_edit_is_rejected_whole_and_the_task_is_unchanged(string body)
    {
        var project = await NewProjectAsync(app.NextUser());
        var id = (await NewTaskAsync(app.NextUser(), project, "Original", "text", SeedIds.Liam)).GetProperty("id").GetGuid();
        using var client = Tasks(app.NextUser());
        var before = await client.GetStringAsync($"/api/tasks/{id}", TestContext.Current.CancellationToken);

        using var response = await client.PutAsync($"/api/tasks/{id}", Json(body), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await client.GetStringAsync($"/api/tasks/{id}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_title_of_two_hundred_emoji_is_accepted_and_two_hundred_and_one_is_not()
    {
        const string family = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";
        var project = await NewProjectAsync(app.NextUser());
        using var client = Tasks(app.NextUser());

        using var ok = await client.PostAsync("/api/tasks", Json($$"""{"projectId":"{{project}}","title":{{Esc(string.Concat(Enumerable.Repeat(family, 200)))}}}"""), TestContext.Current.CancellationToken);
        using var tooLong = await client.PostAsync("/api/tasks", Json($$"""{"projectId":"{{project}}","title":{{Esc(string.Concat(Enumerable.Repeat(family, 201)))}}}"""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Theory]
    [InlineData("""<script>alert(1)</script>""")]
    [InlineData("""<img src=x onerror=alert(1)>""")]
    [InlineData("""'; DROP TABLE tasks; --""")]
    [InlineData("""javascript:alert(1)""")]
    public async Task Markup_and_injection_text_is_stored_and_returned_exactly_as_plain_text(string text)
    {
        var project = await NewProjectAsync(app.NextUser(), text);
        var task = await NewTaskAsync(app.NextUser(), project, text, text);

        using var client = Tasks(app.NextUser());
        var read = JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{task.GetProperty("id").GetGuid()}", TestContext.Current.CancellationToken)).RootElement;
        using var projects = Projects(app.NextUser());
        var readProject = JsonDocument.Parse(await projects.GetStringAsync($"/api/projects/{project}", TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal(text, read.GetProperty("title").GetString());
        Assert.Equal(text, read.GetProperty("description").GetString());
        Assert.Equal(text, readProject.GetProperty("name").GetString());

        // The tables still exist: the text was a parameter, never part of a statement.
        Assert.NotEmpty(await BoardAsync(client, project));
    }

    [Fact]
    public async Task Two_edits_at_the_same_time_both_succeed_and_the_last_one_wins_as_a_whole_not_a_mix()
    {
        var project = await NewProjectAsync(app.NextUser());
        var id = (await NewTaskAsync(app.NextUser(), project, "Original", "original text", SeedIds.Maya)).GetProperty("id").GetGuid();
        using var priya = Tasks(app.NextUser());
        using var jordan = Tasks(SeedIds.Jordan);

        for (var round = 0; round < 5; round++)
        {
            var a = priya.PutAsync($"/api/tasks/{id}", Json($$"""{"title":"Edit A","description":"text A","assigneeUserId":"{{SeedIds.Liam}}"}"""), TestContext.Current.CancellationToken);
            var b = jordan.PutAsync($"/api/tasks/{id}", Json("""{"title":"Edit B","description":null,"assigneeUserId":null}"""), TestContext.Current.CancellationToken);
            var results = await Task.WhenAll(a, b);
            Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

            var task = JsonDocument.Parse(await priya.GetStringAsync($"/api/tasks/{id}", TestContext.Current.CancellationToken)).RootElement;
            var title = task.GetProperty("title").GetString();
            var description = task.GetProperty("description").ValueKind == JsonValueKind.Null ? null : task.GetProperty("description").GetString();
            var assignee = task.GetProperty("assigneeUserId").ValueKind == JsonValueKind.Null ? (Guid?)null : task.GetProperty("assigneeUserId").GetGuid();

            // Either all of A or all of B: never "Edit A" with B's description, for example (spec FR-011).
            Assert.True(
                (title, description, assignee) == ("Edit A", "text A", SeedIds.Liam) || (title, description, assignee) == ("Edit B", null, null),
                $"mixed result: {title} / {description} / {assignee}");
        }
    }

    [Fact]
    public async Task Creating_and_editing_write_events_with_ids_and_titles_but_never_descriptions()
    {
        var project = await NewProjectAsync(app.NextUser());
        var id = (await NewTaskAsync(app.NextUser(), project, "Event task", "secret description", SeedIds.Jordan)).GetProperty("id").GetGuid();
        using var client = Tasks(app.NextUser());
        (await client.PutAsync($"/api/tasks/{id}", Json($$"""{"title":"Event task v2","description":"another secret","assigneeUserId":"{{SeedIds.Liam}}"}"""), TestContext.Current.CancellationToken)).Dispose();

        var created = Assert.Single(await OutboxPayloadsAsync("tasksdb", "TaskCreated", "taskId", id));
        var updated = Assert.Single(await OutboxPayloadsAsync("tasksdb", "TaskUpdated", "taskId", id));
        var assigned = Assert.Single(await OutboxPayloadsAsync("tasksdb", "TaskAssigned", "taskId", id));

        Assert.Equal(SeedIds.Jordan, JsonDocument.Parse(created).RootElement.GetProperty("assigneeUserId").GetGuid());
        Assert.Equal("Event task v2", JsonDocument.Parse(updated).RootElement.GetProperty("title").GetString());
        var assignment = JsonDocument.Parse(assigned).RootElement;
        Assert.Equal(SeedIds.Jordan, assignment.GetProperty("previousAssigneeUserId").GetGuid());
        Assert.Equal(SeedIds.Liam, assignment.GetProperty("assigneeUserId").GetGuid());
        Assert.All([created, updated, assigned], payload =>
        {
            Assert.DoesNotContain("secret description", payload);
            Assert.DoesNotContain("another secret", payload);
            Assert.False(JsonDocument.Parse(payload).RootElement.TryGetProperty("description", out _));
        });
    }

    [Fact]
    public async Task Changes_are_audited_with_ids_and_the_acting_user_but_no_text()
    {
        var project = await NewProjectAsync(SeedIds.Priya);
        var task = await NewTaskAsync(SeedIds.Priya, project, "Audit-me-not-" + Guid.NewGuid().ToString("N")[..6], "private words");

        var logs = await app.GetLogsAsync("tasks-api");

        Assert.Contains($"Audit TaskCreated outcome=Succeeded user={SeedIds.Priya}", logs);
        Assert.Contains($"entityId={task.GetProperty("id").GetGuid()}", logs);
        Assert.DoesNotContain(task.GetProperty("title").GetString()!, logs);
        Assert.DoesNotContain("private words", logs);
    }
}
