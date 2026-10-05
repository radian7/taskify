using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Comments (spec FR-015 to FR-017, FR-024; User Story 4; contracts/tasks-api.yaml). Every test creates its own project
/// and task, so it cannot disturb the sample data.
/// </summary>
/// <param name="app">The running application.</param>
public class CommentsContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract Contract = OpenApiContract.Load("tasks-api.yaml");

    private HttpClient Projects(Guid user) => app.CreateClient("projects-api", TaskifyAppFixture.WebKey, user);

    private HttpClient Tasks(Guid user) => app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string Text(string text) => $$"""{"text":{{JsonSerializer.Serialize(text)}}}""";

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    /// <summary>Creates a project and a task in it (assigned to Jordan) and returns the task ID.</summary>
    private async Task<Guid> NewTaskAsync()
    {
        using var projects = Projects(app.NextUser());
        using var projectResponse = await projects.PostAsync("/api/projects", Json($$"""{"name":"Comments {{Guid.NewGuid():N}}"}"""), TestContext.Current.CancellationToken);
        var project = (await ReadAsync(projectResponse)).GetProperty("id").GetGuid();

        using var tasks = Tasks(app.NextUser());
        using var taskResponse = await tasks.PostAsync("/api/tasks", Json($$"""{"projectId":"{{project}}","title":"Discuss me","assigneeUserId":"{{SeedIds.Jordan}}"}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, taskResponse.StatusCode);
        return (await ReadAsync(taskResponse)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> PostAsync(Guid task, Guid user, string text)
    {
        using var client = Tasks(user);
        using var response = await client.PostAsync($"/api/tasks/{task}/comments", Json(Text(text)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, Guid task)
    {
        var body = await client.GetStringAsync($"/api/tasks/{task}/comments", TestContext.Current.CancellationToken);
        Assert.True(Contract.ValidateItems("Comment", body).IsValid, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<string?> StoredTextAsync(Guid comment)
    {
        await using var connection = new NpgsqlConnection(await app.GetConnectionStringAsync("tasksdb"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT \"Text\" FROM comments WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", comment);
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is DBNull or null ? null : (string)value;
    }

    private async Task<List<string>> EventPayloadsAsync(string type, Guid comment)
    {
        await using var connection = new NpgsqlConnection(await app.GetConnectionStringAsync("tasksdb"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT \"Payload\"::text FROM outbox_messages WHERE \"Type\" = @type AND \"Payload\"->>'commentId' = @id ORDER BY \"OccurredAt\"", connection);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("id", comment.ToString());
        var payloads = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads;
    }

    // --------------------------------------------------------------------------------------- posting and reading

    [Fact]
    public async Task Posting_a_comment_returns_201_with_the_acting_user_as_author_and_trimmed_text()
    {
        var task = await NewTaskAsync();
        using var client = Tasks(SeedIds.Priya);

        using var response = await client.PostAsync($"/api/tasks/{task}/comments", Json(Text("  Looks good.\nShip it.  ")), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(Contract.Validate("Comment", body).IsValid, body);
        var comment = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Looks good.\nShip it.", comment.GetProperty("text").GetString());   // trimmed, line break kept
        Assert.Equal(SeedIds.Priya, comment.GetProperty("authorUserId").GetGuid());
        Assert.Equal(task, comment.GetProperty("taskId").GetGuid());
        Assert.False(comment.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, comment.GetProperty("editedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, comment.GetProperty("deletedAt").ValueKind);
        Assert.Equal($"/api/tasks/{task}/comments/{comment.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Comments_are_listed_oldest_first_with_author_and_time()
    {
        var task = await NewTaskAsync();
        await PostAsync(task, SeedIds.Priya, "first");
        await PostAsync(task, SeedIds.Liam, "second");
        await PostAsync(task, SeedIds.Tomasz, "third");
        using var client = Tasks(SeedIds.Jordan);

        var comments = await ListAsync(client, task);

        Assert.Equal(["first", "second", "third"], comments.Select(c => c.GetProperty("text").GetString()));
        Assert.Equal([SeedIds.Priya, SeedIds.Liam, SeedIds.Tomasz], comments.Select(c => c.GetProperty("authorUserId").GetGuid()));
        var times = comments.Select(c => c.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.Order(), times);
    }

    [Fact]
    public async Task A_task_with_no_comments_has_an_empty_thread()
    {
        var task = await NewTaskAsync();
        using var client = Tasks(SeedIds.Priya);

        Assert.Empty(await ListAsync(client, task));
    }

    [Fact]
    public async Task Any_user_can_comment_on_a_task_in_any_column_including_done()
    {
        var task = await NewTaskAsync();
        using var client = Tasks(SeedIds.Liam);
        (await client.PostAsync($"/api/tasks/{task}/moves", Json("""{"toStatus":"Done"}"""), TestContext.Current.CancellationToken)).Dispose();

        var comment = await PostAsync(task, SeedIds.Tomasz, "Still talking about it");

        Assert.Equal("Still talking about it", comment.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Commenting_on_or_reading_an_unknown_task_is_404()
    {
        using var client = Tasks(SeedIds.Priya);

        using var post = await client.PostAsync($"/api/tasks/{Guid.NewGuid()}/comments", Json(Text("x")), TestContext.Current.CancellationToken);
        using var list = await client.GetAsync($"/api/tasks/{Guid.NewGuid()}/comments", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await post.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Theory]
    [InlineData("""{"text":""}""")]
    [InlineData("""{"text":"   "}""")]
    [InlineData("{}")]
    [InlineData("""{"text":null}""")]
    [InlineData("""{"text":"ok","authorUserId":"11111111-1111-1111-1111-000000000001"}""")]
    [InlineData("not json")]
    public async Task An_invalid_comment_is_rejected_whole_and_nothing_is_saved(string body)
    {
        var task = await NewTaskAsync();
        using var client = Tasks(SeedIds.Priya);

        using var response = await client.PostAsync($"/api/tasks/{task}/comments", Json(body), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Empty(await ListAsync(client, task));
    }

    [Fact]
    public async Task Two_thousand_characters_are_accepted_and_two_thousand_and_one_are_not()
    {
        var task = await NewTaskAsync();
        using var client = Tasks(SeedIds.Priya);

        using var ok = await client.PostAsync($"/api/tasks/{task}/comments", Json(Text(new string('c', 2000))), TestContext.Current.CancellationToken);
        using var tooLong = await client.PostAsync($"/api/tasks/{task}/comments", Json(Text(new string('c', 2001))), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("Text must be between 1 and 2000 characters.", (await ReadAsync(tooLong)).GetProperty("errors").GetProperty("text")[0].GetString());
        Assert.Single(await ListAsync(client, task));
    }

    [Theory]
    [InlineData("""<script>alert(1)</script>""")]
    [InlineData("""<img src=x onerror=alert(1)>""")]
    [InlineData("""'; DROP TABLE comments; --""")]
    public async Task Markup_and_injection_text_is_stored_and_returned_exactly_as_plain_text(string text)
    {
        var task = await NewTaskAsync();
        var posted = await PostAsync(task, SeedIds.Priya, text);
        using var client = Tasks(SeedIds.Liam);

        var comments = await ListAsync(client, task);

        Assert.Equal(text, Assert.Single(comments).GetProperty("text").GetString());
        Assert.Equal(text, await StoredTextAsync(posted.GetProperty("id").GetGuid()));
    }

    // --------------------------------------------------------------------------------------------------- editing

    [Fact]
    public async Task The_author_can_edit_and_the_comment_then_shows_an_edited_time()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Priya, "first draft")).GetProperty("id").GetGuid();
        using var client = Tasks(SeedIds.Priya);

        using var response = await client.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("  second draft  ")), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.Validate("Comment", body).IsValid, body);
        var comment = JsonDocument.Parse(body).RootElement;
        Assert.Equal("second draft", comment.GetProperty("text").GetString());
        Assert.NotEqual(JsonValueKind.Null, comment.GetProperty("editedAt").ValueKind);
        Assert.Equal(SeedIds.Priya, comment.GetProperty("authorUserId").GetGuid());   // the author never changes
        Assert.Equal("second draft", (await ListAsync(client, task)).Single().GetProperty("text").GetString());
    }

    [Fact]
    public async Task Another_user_cannot_edit_the_comment_and_gets_403_with_the_text_unchanged()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Priya, "mine")).GetProperty("id").GetGuid();
        using var liam = Tasks(SeedIds.Liam);

        using var response = await liam.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("hijacked")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Equal("mine", await StoredTextAsync(id));
        Assert.Equal(JsonValueKind.Null, (await ListAsync(liam, task)).Single().GetProperty("editedAt").ValueKind);
    }

    [Fact]
    public async Task Even_the_product_manager_cannot_edit_or_delete_someone_elses_comment()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Priya, "mine")).GetProperty("id").GetGuid();
        using var maya = Tasks(SeedIds.Maya);

        using var edit = await maya.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("overruled")), TestContext.Current.CancellationToken);
        using var delete = await maya.DeleteAsync($"/api/tasks/{task}/comments/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal("mine", await StoredTextAsync(id));
    }

    [Fact]
    public async Task An_unknown_comment_or_a_comment_on_another_task_is_404()
    {
        var task = await NewTaskAsync();
        var otherTask = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Tomasz, "mine")).GetProperty("id").GetGuid();
        using var client = Tasks(SeedIds.Tomasz);

        using var unknown = await client.PutAsync($"/api/tasks/{task}/comments/{Guid.NewGuid()}", Json(Text("x")), TestContext.Current.CancellationToken);
        using var wrongTask = await client.PutAsync($"/api/tasks/{otherTask}/comments/{id}", Json(Text("x")), TestContext.Current.CancellationToken);
        using var wrongTaskDelete = await client.DeleteAsync($"/api/tasks/{otherTask}/comments/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongTask.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongTaskDelete.StatusCode);
        Assert.Equal("mine", await StoredTextAsync(id));
    }

    [Fact]
    public async Task An_invalid_edit_is_rejected_and_the_comment_is_unchanged()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Tomasz, "keep me")).GetProperty("id").GetGuid();
        using var client = Tasks(SeedIds.Tomasz);

        using var empty = await client.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("   ")), TestContext.Current.CancellationToken);
        using var tooLong = await client.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text(new string('c', 2001))), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("keep me", await StoredTextAsync(id));
    }

    // -------------------------------------------------------------------------------------------------- deleting

    [Fact]
    public async Task Deleting_leaves_a_placeholder_and_erases_the_text_from_the_database_and_the_events()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Tomasz, "very secret words")).GetProperty("id").GetGuid();
        using var client = Tasks(SeedIds.Tomasz);

        using var response = await client.DeleteAsync($"/api/tasks/{task}/comments/{id}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.Validate("Comment", body).IsValid, body);
        var placeholder = JsonDocument.Parse(body).RootElement;
        Assert.True(placeholder.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, placeholder.GetProperty("text").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, placeholder.GetProperty("deletedAt").ValueKind);
        Assert.Equal(SeedIds.Tomasz, placeholder.GetProperty("authorUserId").GetGuid());

        // The thread keeps the placeholder in place; the words are gone from storage and from every event (spec FR-024).
        var thread = await ListAsync(client, task);
        Assert.Equal(id, Assert.Single(thread).GetProperty("id").GetGuid());
        Assert.True(thread[0].GetProperty("isDeleted").GetBoolean());
        Assert.Null(await StoredTextAsync(id));
        var payloads = (await EventPayloadsAsync("CommentAdded", id)).Concat(await EventPayloadsAsync("CommentDeleted", id));
        Assert.All(payloads, payload => Assert.DoesNotContain("very secret words", payload));
    }

    [Fact]
    public async Task Another_user_cannot_delete_the_comment_and_gets_403()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Tomasz, "mine")).GetProperty("id").GetGuid();
        using var liam = Tasks(SeedIds.Liam);

        using var response = await liam.DeleteAsync($"/api/tasks/{task}/comments/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("mine", await StoredTextAsync(id));
    }

    [Fact]
    public async Task A_deleted_comment_cannot_be_edited_deleted_again_or_restored_and_gets_409()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Tomasz, "gone soon")).GetProperty("id").GetGuid();
        using var client = Tasks(SeedIds.Tomasz);
        (await client.DeleteAsync($"/api/tasks/{task}/comments/{id}", TestContext.Current.CancellationToken)).Dispose();

        using var edit = await client.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("back again")), TestContext.Current.CancellationToken);
        using var deleteAgain = await client.DeleteAsync($"/api/tasks/{task}/comments/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteAgain.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Null(await StoredTextAsync(id));
    }

    [Fact]
    public async Task An_edit_and_a_delete_at_the_same_time_never_leave_text_behind_on_a_deleted_comment()
    {
        var task = await NewTaskAsync();
        using var client = Tasks(SeedIds.Tomasz);

        for (var round = 0; round < 8; round++)
        {
            var id = (await PostAsync(task, SeedIds.Tomasz, $"round {round}")).GetProperty("id").GetGuid();

            var edit = client.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("edited at the same time")), TestContext.Current.CancellationToken);
            var delete = client.DeleteAsync($"/api/tasks/{task}/comments/{id}", TestContext.Current.CancellationToken);
            var results = await Task.WhenAll(edit, delete);

            // The delete always succeeds; the edit either got in first (200) or lost the race (409), never both ways.
            Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
            Assert.True(results[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"edit returned {(int)results[0].StatusCode}");
            Assert.Null(await StoredTextAsync(id));
        }
    }

    // --------------------------------------------------------------------------------------------- counts, events

    [Fact]
    public async Task The_comment_count_on_a_card_and_in_the_details_excludes_deleted_comments()
    {
        var task = await NewTaskAsync();
        var first = (await PostAsync(task, SeedIds.Priya, "one")).GetProperty("id").GetGuid();
        await PostAsync(task, SeedIds.Liam, "two");
        await PostAsync(task, SeedIds.Tomasz, "three");
        using var client = Tasks(SeedIds.Priya);

        async Task<(int Detail, int Summary)> CountsAsync()
        {
            var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{task}", TestContext.Current.CancellationToken)).RootElement;
            Assert.True(Contract.Validate("TaskDetail", detail.GetRawText()).IsValid);
            var project = detail.GetProperty("projectId").GetGuid();
            var summaries = JsonDocument.Parse(await client.GetStringAsync($"/api/tasks?projectId={project}", TestContext.Current.CancellationToken)).RootElement;
            return (detail.GetProperty("commentCount").GetInt32(), summaries.EnumerateArray().Single().GetProperty("commentCount").GetInt32());
        }

        Assert.Equal((3, 3), await CountsAsync());

        (await client.DeleteAsync($"/api/tasks/{task}/comments/{first}", TestContext.Current.CancellationToken)).Dispose();

        Assert.Equal((2, 2), await CountsAsync());
    }

    [Fact]
    public async Task Posting_writes_an_event_with_ids_and_the_task_title_but_never_the_comment_text()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Priya, "private opinion")).GetProperty("id").GetGuid();
        using var client = Tasks(SeedIds.Priya);
        (await client.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("another private opinion")), TestContext.Current.CancellationToken)).Dispose();

        var added = Assert.Single(await EventPayloadsAsync("CommentAdded", id));
        var edited = Assert.Single(await EventPayloadsAsync("CommentEdited", id));

        var payload = JsonDocument.Parse(added).RootElement;
        Assert.Equal("Discuss me", payload.GetProperty("title").GetString());
        Assert.Equal(SeedIds.Jordan, payload.GetProperty("assigneeUserId").GetGuid());   // the task's assignee, for notifications
        Assert.All([added, edited], json =>
        {
            Assert.DoesNotContain("private opinion", json);
            Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("text", out _));
        });
    }

    [Fact]
    public async Task A_refused_attempt_is_audited_with_the_ids_but_the_comment_text_is_never_logged()
    {
        var task = await NewTaskAsync();
        var id = (await PostAsync(task, SeedIds.Priya, "words that must not appear in logs")).GetProperty("id").GetGuid();
        using var liam = Tasks(SeedIds.Liam);
        (await liam.PutAsync($"/api/tasks/{task}/comments/{id}", Json(Text("sneaky edit that must not appear in logs")), TestContext.Current.CancellationToken)).Dispose();

        var logs = await app.GetLogsAsync("tasks-api");

        Assert.Contains($"Audit RequestRejected outcome=Forbidden user={SeedIds.Liam}", logs);
        Assert.Contains($"entityId={id}", logs);
        Assert.DoesNotContain("must not appear in logs", logs);
    }
}
