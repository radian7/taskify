using System.Net;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Contract tests for reading tasks (spec FR-005, FR-008, FR-010; contracts/tasks-api.yaml). The seed data has
/// 10 tasks per sample project, spread over all four columns, assigned to a mix of users, with some unassigned.
/// </summary>
/// <param name="app">The running application.</param>
[Collection(SeedData.Collection)]
public class TasksReadContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract Contract = OpenApiContract.Load("tasks-api.yaml");
    private static readonly string[] StatusOrder = ["ToDo", "InProgress", "InReview", "Done"];

    private HttpClient WebClient() => app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, SeedIds.Priya);

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, Guid projectId)
    {
        var body = await client.GetStringAsync($"/api/tasks?projectId={projectId}", TestContext.Current.CancellationToken);
        Assert.True(Contract.ValidateItems("TaskSummary", body).IsValid, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Theory]
    [MemberData(nameof(SampleProjects))]
    public async Task Each_sample_project_has_ten_tasks_in_all_four_columns_with_a_mix_of_assignees(Guid projectId)
    {
        using var client = WebClient();

        var tasks = await ListAsync(client, projectId);

        Assert.Equal(10, tasks.Count);
        Assert.Equal(10, tasks.Select(t => t.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.All(tasks, t => Assert.Equal(projectId, t.GetProperty("projectId").GetGuid()));

        foreach (var status in StatusOrder)
        {
            Assert.True(tasks.Count(t => t.GetProperty("status").GetString() == status) >= 2, $"fewer than 2 tasks in {status}");
        }

        var assignees = tasks.Select(t => t.GetProperty("assigneeUserId")).ToList();
        Assert.True(assignees.Count(a => a.ValueKind == JsonValueKind.Null) >= 2, "expected at least 2 unassigned tasks");
        Assert.True(assignees.Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetGuid()).Distinct().Count() >= 4, "expected tasks assigned to at least 4 different users");
        Assert.All(assignees.Where(a => a.ValueKind == JsonValueKind.String), a => Assert.Contains(a.GetGuid(), SeedIds.AllUsers));
    }

    [Fact]
    public async Task Tasks_are_ordered_by_column_then_newest_first()
    {
        using var client = WebClient();

        var tasks = await ListAsync(client, SeedIds.MobileAppLaunch);

        var keys = tasks.Select(t => (Status: Array.IndexOf(StatusOrder, t.GetProperty("status").GetString()), Created: t.GetProperty("createdAt").GetDateTimeOffset())).ToList();
        Assert.Equal(keys.OrderBy(k => k.Status).ThenByDescending(k => k.Created), keys);
    }

    [Fact]
    public async Task A_task_has_no_comments_yet_so_the_count_is_zero()
    {
        using var client = WebClient();

        var tasks = await ListAsync(client, SeedIds.WebsiteRedesign);

        Assert.All(tasks, t => Assert.Equal(0, t.GetProperty("commentCount").GetInt32()));
    }

    [Fact]
    public async Task Getting_a_task_returns_its_details_matching_the_contract()
    {
        using var client = WebClient();
        var id = (await ListAsync(client, SeedIds.InternalTools))[0].GetProperty("id").GetGuid();

        using var response = await client.GetAsync($"/api/tasks/{id}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.Validate("TaskDetail", body).IsValid, body);
        Assert.Equal(id, JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Getting_an_unknown_task_returns_404_problem_details()
    {
        using var client = WebClient();

        using var response = await client.GetAsync($"/api/tasks/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task An_unknown_project_has_an_empty_board()
    {
        using var client = WebClient();

        var tasks = await ListAsync(client, Guid.NewGuid());

        Assert.Empty(tasks);
    }

    [Theory]
    [InlineData("/api/tasks")]
    [InlineData("/api/tasks?projectId=")]
    [InlineData("/api/tasks?projectId=not-a-guid")]
    public async Task A_missing_or_malformed_project_id_gets_400(string path)
    {
        using var client = WebClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task Only_the_web_app_may_read_tasks()
    {
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, actingUser: null);

        using var withoutUser = await client.GetAsync($"/api/tasks?projectId={SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, withoutUser.StatusCode);
    }

    /// <summary>The three sample projects, as test data.</summary>
    /// <returns>One row per project.</returns>
    public static TheoryData<Guid> SampleProjects() => new(SeedIds.AllProjects);
}
