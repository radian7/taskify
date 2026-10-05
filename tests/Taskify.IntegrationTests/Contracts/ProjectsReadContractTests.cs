using System.Net;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>Contract tests for reading projects (spec FR-005, FR-007; contracts/projects-api.yaml).</summary>
/// <param name="app">The running application.</param>
[Collection(SeedData.Collection)]
public class ProjectsReadContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract Contract = OpenApiContract.Load("projects-api.yaml");

    [Fact]
    public async Task Listing_projects_includes_the_three_sample_projects_newest_first_matching_the_contract()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Priya);

        using var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.ValidateItems("Project", body).IsValid, body);

        using var document = JsonDocument.Parse(body);
        var projects = document.RootElement.EnumerateArray().ToList();
        // Other tests create projects of their own, so the three samples are present rather than the only ones.
        Assert.True(projects.Count >= 3);
        var ids = projects.Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.All(SeedIds.AllProjects, sample => Assert.Contains(sample, ids));

        var created = projects.Select(p => p.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(created.OrderByDescending(c => c), created);
    }

    [Fact]
    public async Task Every_user_sees_the_same_projects()
    {
        using var priya = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Priya);
        using var jordan = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Jordan);

        var forPriya = await priya.GetStringAsync("/api/projects", TestContext.Current.CancellationToken);
        var forJordan = await jordan.GetStringAsync("/api/projects", TestContext.Current.CancellationToken);

        Assert.Equal(forPriya, forJordan);
    }

    [Fact]
    public async Task Getting_a_sample_project_returns_it_matching_the_contract()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Priya);

        using var response = await client.GetAsync($"/api/projects/{SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.Validate("Project", body).IsValid, body);
        Assert.Equal("Mobile App Launch", JsonDocument.Parse(body).RootElement.GetProperty("name").GetString());
        Assert.Equal(SeedIds.Maya, JsonDocument.Parse(body).RootElement.GetProperty("createdByUserId").GetGuid());
    }

    [Fact]
    public async Task Getting_an_unknown_project_returns_404_problem_details()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Priya);

        using var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task A_malformed_project_id_is_not_a_route()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, SeedIds.Priya);

        using var response = await client.GetAsync("/api/projects/not-a-guid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_tasks_api_may_read_one_project_but_not_list_them()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.TasksKey, SeedIds.Priya);

        using var single = await client.GetAsync($"/api/projects/{SeedIds.MobileAppLaunch}", TestContext.Current.CancellationToken);
        using var list = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task The_notifications_api_cannot_read_projects()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.NotificationsKey, SeedIds.Priya);

        using var response = await client.GetAsync("/api/projects", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("99999999-9999-9999-9999-999999999999")]
    public async Task A_missing_or_unknown_acting_user_gets_400(string? user)
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        if (user is not null)
        {
            request.Headers.Add("X-Taskify-User", user);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }
}
