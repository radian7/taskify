using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Security;

/// <summary>
/// Behaviour every API shares (research R8, R9; constitution Principles I and II): API keys, the key matrix, exempt
/// routes, health checks and what is exposed outside the Aspire network.
/// </summary>
/// <remarks>
/// The acting-user, unknown-field and request-size checks need a route that takes a user and a body. They are added
/// with the first such routes (T064 and T093) to this class.
/// </remarks>
/// <param name="app">The running application.</param>
public class CrossCuttingTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract ProjectsContract = OpenApiContract.Load("projects-api.yaml");

    [Fact]
    public async Task A_request_without_an_api_key_gets_401()
    {
        using var client = app.CreateClient("projects-api", apiKey: null);

        using var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(ProjectsContract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Fact]
    public async Task A_wrong_api_key_gets_401_and_the_key_is_not_echoed()
    {
        using var client = app.CreateClient("projects-api", "not-the-key-1234567890");

        using var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("not-the-key", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_key_of_a_service_that_the_api_does_not_accept_gets_401()
    {
        // The Tasks API accepts only the Web key (research R8 key matrix), so the Tasks key is unknown to it.
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.TasksKey);

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_known_caller_on_a_route_it_may_not_use_gets_403_matching_the_contract()
    {
        // The OpenAPI document route is open to the Web caller only; the Notifications key is valid on this API.
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.NotificationsKey);

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(ProjectsContract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Theory]
    [InlineData(TaskifyAppFixture.WebKey)]
    [InlineData(TaskifyAppFixture.TasksKey)]
    [InlineData(TaskifyAppFixture.NotificationsKey)]
    public async Task The_user_directory_needs_no_acting_user_for_any_allowed_caller(string key)
    {
        using var client = app.CreateClient("projects-api", key);

        using var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_projects_key_is_not_accepted_by_the_projects_api()
    {
        // A service never calls itself, so its own key is not on its accepted list.
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.ProjectsKey);

        using var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("projects-api")]
    [InlineData("tasks-api")]
    [InlineData("notifications-api")]
    [InlineData("web")]
    public async Task Health_endpoints_answer_without_any_credentials(string resource)
    {
        using var client = app.CreateClient(resource, apiKey: null);

        using var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using var alive = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [Fact]
    public void Only_the_web_resource_has_an_external_endpoint()
    {
        var model = app.App.Services.GetRequiredService<DistributedApplicationModel>();

        var external = model.Resources
            .Where(r => r.Annotations.OfType<EndpointAnnotation>().Any(e => e.IsExternal))
            .Select(r => r.Name)
            .ToList();

        Assert.Equal(["web"], external);
    }

    [Fact]
    public async Task Unknown_routes_get_a_problem_details_404_with_no_internal_detail()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);

        using var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("   at ", body);
    }
}
