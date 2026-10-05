using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Contract and behaviour tests for moving tasks and their history (spec FR-011, FR-012, FR-023; User Story 2;
/// contracts/tasks-api.yaml). They move sample tasks, so they run in the sequential <see cref="SeedData.Collection"/>
/// and put every task back where it was.
/// </summary>
/// <param name="app">The running application.</param>
[Collection(SeedData.Collection)]
public class MoveContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract Contract = OpenApiContract.Load("tasks-api.yaml");

    private HttpClient As(Guid user) => app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user);

    private static async Task<JsonElement> FirstTaskInAsync(HttpClient client, Guid project, string status)
    {
        var body = await client.GetStringAsync($"/api/tasks?projectId={project}", TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().First(t => t.GetProperty("status").GetString() == status).Clone();
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid taskId, string toStatus) =>
        client.PostAsync($"/api/tasks/{taskId}/moves", Json($$"""{"toStatus":"{{toStatus}}"}"""), TestContext.Current.CancellationToken);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<List<JsonElement>> HistoryAsync(HttpClient client, Guid taskId)
    {
        var body = await client.GetStringAsync($"/api/tasks/{taskId}/history", TestContext.Current.CancellationToken);
        Assert.True(Contract.ValidateItems("StatusChange", body).IsValid, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task<string> StatusOfAsync(HttpClient client, Guid taskId) =>
        JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{taskId}", TestContext.Current.CancellationToken)).RootElement.GetProperty("status").GetString()!;

    private async Task<long> OutboxCountAsync(Guid taskId, string type)
    {
        await using var connection = new NpgsqlConnection(await app.GetConnectionStringAsync("tasksdb"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM outbox_messages WHERE \"Type\" = @type AND \"Payload\"->>'taskId' = @taskId", connection);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("taskId", taskId.ToString());
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Moving_a_task_returns_the_new_state_and_the_history_entry_and_it_stays_moved()
    {
        using var priya = As(SeedIds.Priya);
        var task = await FirstTaskInAsync(priya, SeedIds.MobileAppLaunch, "ToDo");
        var id = task.GetProperty("id").GetGuid();
        try
        {
            using var response = await MoveAsync(priya, id, "InProgress");
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(Contract.Validate("MoveTaskResult", body).IsValid, body);
            var result = JsonDocument.Parse(body).RootElement;
            Assert.True(result.GetProperty("changed").GetBoolean());
            Assert.Equal("InProgress", result.GetProperty("task").GetProperty("status").GetString());
            var change = result.GetProperty("statusChange");
            Assert.Equal("ToDo", change.GetProperty("fromStatus").GetString());
            Assert.Equal("InProgress", change.GetProperty("toStatus").GetString());
            Assert.Equal(SeedIds.Priya, change.GetProperty("movedByUserId").GetGuid());

            // Another user, later, sees it in the new column (User Story 2, scenario 2).
            using var jordan = As(SeedIds.Jordan);
            Assert.Equal("InProgress", await StatusOfAsync(jordan, id));
        }
        finally
        {
            using var restore = await MoveAsync(priya, id, "ToDo");
        }
    }

    [Fact]
    public async Task A_task_can_move_backwards_and_across_any_columns_whoever_created_or_is_assigned_to_it()
    {
        using var tomasz = As(app.NextUser());
        var task = await FirstTaskInAsync(tomasz, SeedIds.WebsiteRedesign, "Done");
        var id = task.GetProperty("id").GetGuid();
        try
        {
            using var back = await MoveAsync(tomasz, id, "InProgress");
            Assert.Equal(HttpStatusCode.OK, back.StatusCode);
            Assert.Equal("InProgress", await StatusOfAsync(tomasz, id));

            using var across = await MoveAsync(tomasz, id, "ToDo");
            Assert.Equal(HttpStatusCode.OK, across.StatusCode);
            Assert.Equal("ToDo", await StatusOfAsync(tomasz, id));
        }
        finally
        {
            using var restore = await MoveAsync(tomasz, id, "Done");
        }
    }

    [Fact]
    public async Task Moving_to_the_same_column_changes_nothing_and_writes_no_history_and_no_event()
    {
        using var priya = As(app.NextUser());
        var id = (await FirstTaskInAsync(priya, SeedIds.InternalTools, "InReview")).GetProperty("id").GetGuid();
        var historyBefore = (await HistoryAsync(priya, id)).Count;
        var eventsBefore = await OutboxCountAsync(id, "TaskMoved");

        using var response = await MoveAsync(priya, id, "InReview");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.Validate("MoveTaskResult", body).IsValid, body);
        var result = JsonDocument.Parse(body).RootElement;
        Assert.False(result.GetProperty("changed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("statusChange").ValueKind);
        Assert.Equal(historyBefore, (await HistoryAsync(priya, id)).Count);
        Assert.Equal(eventsBefore, await OutboxCountAsync(id, "TaskMoved"));
    }

    [Fact]
    public async Task A_move_writes_one_event_with_ids_and_title_but_never_the_description()
    {
        using var priya = As(app.NextUser());
        var id = (await FirstTaskInAsync(priya, SeedIds.MobileAppLaunch, "ToDo")).GetProperty("id").GetGuid();
        var before = await OutboxCountAsync(id, "TaskMoved");
        try
        {
            using var response = await MoveAsync(priya, id, "InReview");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Equal(before + 1, await OutboxCountAsync(id, "TaskMoved"));

            await using var connection = new NpgsqlConnection(await app.GetConnectionStringAsync("tasksdb"));
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT \"Payload\"::text FROM outbox_messages WHERE \"Type\" = 'TaskMoved' AND \"Payload\"->>'taskId' = @id ORDER BY \"OccurredAt\" DESC LIMIT 1", connection);
            command.Parameters.AddWithValue("id", id.ToString());
            var payload = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).RootElement;
            Assert.Equal("ToDo", payload.GetProperty("fromStatus").GetString());
            Assert.Equal("InReview", payload.GetProperty("toStatus").GetString());
            Assert.False(payload.TryGetProperty("description", out _));
        }
        finally
        {
            using var restore = await MoveAsync(priya, id, "ToDo");
        }
    }

    [Fact]
    public async Task Moving_an_unknown_task_returns_404_problem_details()
    {
        using var priya = As(app.NextUser());

        using var response = await MoveAsync(priya, Guid.NewGuid(), "Done");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"toStatus":"Blocked"}""")]
    [InlineData("""{"toStatus":"done"}""")]
    [InlineData("""{"toStatus":2}""")]
    [InlineData("""{"toStatus":null}""")]
    [InlineData("""{"toStatus":"Done","extra":true}""")]
    [InlineData("not json at all")]
    public async Task An_invalid_move_is_rejected_whole_and_changes_nothing(string body)
    {
        using var priya = As(app.NextUser());
        var id = (await FirstTaskInAsync(priya, SeedIds.MobileAppLaunch, "ToDo")).GetProperty("id").GetGuid();
        var statusBefore = await StatusOfAsync(priya, id);
        var historyBefore = (await HistoryAsync(priya, id)).Count;

        using var response = await priya.PostAsync($"/api/tasks/{id}/moves", Json(body), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
        Assert.Equal(statusBefore, await StatusOfAsync(priya, id));
        Assert.Equal(historyBefore, (await HistoryAsync(priya, id)).Count);
    }

    [Fact]
    public async Task The_history_is_newest_first_and_names_who_moved_the_task_from_and_to_where_and_when()
    {
        using var priya = As(SeedIds.Priya);
        using var liam = As(SeedIds.Liam);
        var id = (await FirstTaskInAsync(priya, SeedIds.WebsiteRedesign, "ToDo")).GetProperty("id").GetGuid();
        var before = (await HistoryAsync(priya, id)).Count;
        try
        {
            (await MoveAsync(priya, id, "InProgress")).Dispose();
            (await MoveAsync(liam, id, "Done")).Dispose();

            var history = await HistoryAsync(priya, id);

            Assert.Equal(before + 2, history.Count);
            Assert.Equal("Done", history[0].GetProperty("toStatus").GetString());
            Assert.Equal("InProgress", history[0].GetProperty("fromStatus").GetString());
            Assert.Equal(SeedIds.Liam, history[0].GetProperty("movedByUserId").GetGuid());
            Assert.Equal("InProgress", history[1].GetProperty("toStatus").GetString());
            Assert.Equal(SeedIds.Priya, history[1].GetProperty("movedByUserId").GetGuid());
            Assert.True(history[0].GetProperty("movedAt").GetDateTimeOffset() >= history[1].GetProperty("movedAt").GetDateTimeOffset());
        }
        finally
        {
            (await MoveAsync(priya, id, "ToDo")).Dispose();
        }
    }

    [Fact]
    public async Task The_history_of_an_unknown_task_is_404()
    {
        using var priya = As(app.NextUser());

        using var response = await priya.GetAsync($"/api/tasks/{Guid.NewGuid()}/history", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task There_is_no_way_to_change_or_delete_the_history_through_the_api(string method)
    {
        using var priya = As(app.NextUser());
        var id = (await FirstTaskInAsync(priya, SeedIds.MobileAppLaunch, "ToDo")).GetProperty("id").GetGuid();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/tasks/{id}/history") { Content = Json("{}") };

        using var response = await priya.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Simultaneous_moves_both_succeed_the_last_to_commit_wins_and_the_history_stays_a_chain()
    {
        using var priya = As(app.NextUser());
        using var jordan = As(app.NextUser());
        var id = (await FirstTaskInAsync(priya, SeedIds.InternalTools, "ToDo")).GetProperty("id").GetGuid();
        var original = await StatusOfAsync(priya, id);
        var before = (await HistoryAsync(priya, id)).Count;
        try
        {
            // Five rounds of two competing moves, to make an interleaving likely.
            for (var round = 0; round < 5; round++)
            {
                var results = await Task.WhenAll(MoveAsync(priya, id, "InProgress"), MoveAsync(jordan, id, "Done"));
                Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
                foreach (var result in results)
                {
                    result.Dispose();
                }
            }

            var history = (await HistoryAsync(priya, id)).AsEnumerable().Reverse().ToList(); // oldest first
            Assert.True(history.Count > before);

            // Each entry starts where the previous one ended, so no move was recorded from a stale state.
            var expectedFrom = history[before].GetProperty("fromStatus").GetString();
            for (var i = before; i < history.Count; i++)
            {
                Assert.Equal(expectedFrom, history[i].GetProperty("fromStatus").GetString());
                expectedFrom = history[i].GetProperty("toStatus").GetString();
            }

            // The task ends in the column of the move that committed last (last write wins, FR-011).
            Assert.Equal(expectedFrom, await StatusOfAsync(priya, id));
        }
        finally
        {
            (await MoveAsync(priya, id, original)).Dispose();
        }
    }

    [Fact]
    public async Task The_database_role_the_service_runs_as_cannot_update_or_delete_history()
    {
        using var priya = As(app.NextUser());
        var id = (await FirstTaskInAsync(priya, SeedIds.MobileAppLaunch, "ToDo")).GetProperty("id").GetGuid();
        try
        {
            (await MoveAsync(priya, id, "Done")).Dispose(); // make sure the table has at least one row

            await using var connection = new NpgsqlConnection(
                await app.GetConnectionStringAsync("tasksdb", "tasks_app", TaskifyAppFixture.TasksDbPassword));
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            // The role can read and append, which is what the service does...
            await using (var read = new NpgsqlCommand("SELECT count(*) FROM status_changes", connection))
            {
                Assert.True((long)(await read.ExecuteScalarAsync(TestContext.Current.CancellationToken))! >= 1);
            }

            // ...and nothing else (PostgreSQL error 42501: insufficient privilege).
            foreach (var statement in new[]
            {
                "UPDATE status_changes SET \"ToStatus\" = 'ToDo'",
                "DELETE FROM status_changes",
                "TRUNCATE status_changes",
            })
            {
#pragma warning disable CA2100 // constant statements
                await using var command = new NpgsqlCommand(statement, connection);
#pragma warning restore CA2100
                var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
                Assert.Equal("42501", failure.SqlState);
            }
        }
        finally
        {
            (await MoveAsync(priya, id, "ToDo")).Dispose();
        }
    }

    [Fact]
    public async Task A_move_is_audited_with_the_task_and_the_actor_but_no_text()
    {
        using var priya = As(SeedIds.Priya);
        var task = await FirstTaskInAsync(priya, SeedIds.WebsiteRedesign, "ToDo");
        var id = task.GetProperty("id").GetGuid();
        try
        {
            (await priya.PostAsJsonAsync($"/api/tasks/{id}/moves", new { toStatus = "InProgress" }, ContractJson.Options, TestContext.Current.CancellationToken)).Dispose();

            var logs = await app.GetLogsAsync("tasks-api");

            Assert.Contains($"Audit TaskMoved outcome=Succeeded user={SeedIds.Priya}", logs);
            Assert.Contains($"entityId={id}", logs);
            Assert.DoesNotContain(task.GetProperty("title").GetString()!, logs);
        }
        finally
        {
            (await MoveAsync(priya, id, "ToDo")).Dispose();
        }
    }
}
