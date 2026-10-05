using System.Net;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>Contract tests for the read-only user directory (spec FR-001, FR-003; contracts/projects-api.yaml).</summary>
/// <param name="app">The running application.</param>
public class UsersContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract Contract = OpenApiContract.Load("projects-api.yaml");

    [Fact]
    public async Task Listing_users_returns_the_five_predefined_users_matching_the_contract()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);

        using var response = await client.GetAsync("/api/users", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.ValidateItems("User", body).IsValid, body);

        using var document = JsonDocument.Parse(body);
        var users = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(5, users.Count);
        Assert.Equal(1, users.Count(u => u.GetProperty("role").GetString() == "ProductManager"));
        Assert.Equal(4, users.Count(u => u.GetProperty("role").GetString() == "Engineer"));
        Assert.Equal(SeedIds.AllUsers.Order(), users.Select(u => u.GetProperty("id").GetGuid()).Order());
        Assert.Equal(5, users.Select(u => u.GetProperty("displayName").GetString()).Distinct().Count());
    }

    [Fact]
    public async Task The_product_manager_is_listed_first()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);

        var body = await client.GetStringAsync("/api/users", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("ProductManager", document.RootElement[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Getting_a_known_user_returns_it_matching_the_contract()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);

        using var response = await client.GetAsync($"/api/users/{SeedIds.Priya}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Contract.Validate("User", body).IsValid, body);
        Assert.Equal("Priya Patel", JsonDocument.Parse(body).RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Getting_an_unknown_user_returns_404_problem_details()
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);

        using var response = await client.GetAsync($"/api/users/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(Contract.Validate("ProblemDetails", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).IsValid);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Users_cannot_be_created_changed_or_deleted(string method)
    {
        using var client = app.CreateClient("projects-api", TaskifyAppFixture.WebKey);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/users/{SeedIds.Priya}")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Refused one way or another: there is no route that changes users (the router's own "method not allowed"
        // endpoint lists no allowed callers, so the API-key check closes it with 403).
        Assert.True(
            response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"got {(int)response.StatusCode}");
    }
}
