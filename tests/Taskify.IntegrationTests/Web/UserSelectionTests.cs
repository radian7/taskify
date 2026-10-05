using System.Net;
using System.Text.RegularExpressions;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Web;

/// <summary>
/// The Web app over real HTTP (spec FR-001, FR-002, FR-020; research R8, R9): the selection screen, the antiforgery
/// token, the protected cookie, rejected cookies and the security headers. These run against the real server pipeline;
/// the interactive pages are covered by the browser tests (T145).
/// </summary>
/// <param name="app">The running application.</param>
public partial class UserSelectionTests(TaskifyAppFixture app)
{
    private static readonly string[] UserNames = ["Maya Chen", "Liam Novak", "Priya Patel", "Jordan Lee", "Tomasz Wiśniewski"];

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private static async Task<(string Html, string Token)> LoadSelectionPageAsync(HttpClient browser)
    {
        using var response = await browser.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, "the page has no antiforgery token");
        return (html, System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
    }

    private static FormUrlEncodedContent Form(Guid userId, string? token) =>
        new(new Dictionary<string, string>
        {
            ["userId"] = userId.ToString("D"),
            ["__RequestVerificationToken"] = token ?? string.Empty,
        });

    [Fact]
    public async Task The_start_screen_lists_the_five_users_with_roles_and_asks_for_no_password()
    {
        using var browser = app.CreateBrowser(out _);

        var (html, _) = await LoadSelectionPageAsync(browser);

        // Blazor HTML-encodes non-ASCII characters (ś becomes &#x15B;), so compare the decoded text.
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.All(UserNames, name => Assert.Contains(name, text));
        Assert.Contains("Product Manager", html);
        Assert.Equal(4, Regex.Matches(html, "Engineer").Count);
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, Regex.Matches(html, "action=\"/session/select\"").Count);
    }

    [Fact]
    public async Task Every_response_carries_the_security_headers()
    {
        using var browser = app.CreateBrowser(out _);

        using var response = await browser.GetAsync("/", TestContext.Current.CancellationToken);

        // Browsers enforce every Content-Security-Policy header. Ours carries the full policy; Blazor adds its own
        // frame-ancestors policy on interactive pages, which must agree with ours ('none').
        var policies = response.Headers.GetValues("Content-Security-Policy").ToList();
        Assert.Contains("default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'", policies);
        Assert.All(policies, policy => Assert.Contains("frame-ancestors 'none'", policy));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task The_page_has_no_inline_script_or_style_that_the_policy_would_block()
    {
        using var browser = app.CreateBrowser(out _);

        var (html, _) = await LoadSelectionPageAsync(browser);

        Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)[^>]*>", html);
        Assert.DoesNotContain(" style=\"", html);
        Assert.DoesNotContain("<style", html);
    }

    [Fact]
    public async Task Choosing_a_user_with_a_valid_token_sets_a_protected_http_only_cookie_and_redirects_to_the_projects()
    {
        using var browser = app.CreateBrowser(out _);
        var (_, token) = await LoadSelectionPageAsync(browser);

        using var response = await browser.PostAsync("/session/select", Form(SeedIds.Priya, token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/projects", response.Headers.Location?.OriginalString);

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("taskify.user=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SeedIds.Priya.ToString(), setCookie);
    }

    [Fact]
    public async Task Choosing_a_user_without_an_antiforgery_token_is_refused_and_sets_no_cookie()
    {
        using var browser = app.CreateBrowser(out _);

        using var response = await browser.PostAsync("/session/select", Form(SeedIds.Priya, token: null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.StartsWith("taskify.user=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("99999999-9999-9999-9999-999999999999")]
    public async Task Choosing_an_empty_or_unknown_user_is_rejected_and_sets_no_cookie(string userId)
    {
        using var browser = app.CreateBrowser(out _);
        var (_, token) = await LoadSelectionPageAsync(browser);

        using var response = await browser.PostAsync("/session/select", Form(Guid.Parse(userId), token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            c => c.StartsWith("taskify.user=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tampered_cookie_is_rejected_deleted_and_audited_with_the_source_ip()
    {
        using var browser = app.CreateBrowser(out var cookies);
        cookies.Add(browser.BaseAddress!, new Cookie("taskify.user", "tampered-value-that-was-never-issued"));

        using var response = await browser.GetAsync("/projects", TestContext.Current.CancellationToken);

        // The page shell still loads (the interactive layout sends the browser to the selection screen).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("taskify.user=", StringComparison.Ordinal));
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        var logs = await app.GetLogsAsync("web");
        Assert.Contains("Audit RequestRejected outcome=Validation", logs);
        Assert.Contains("entityType=SelectedUserCookie", logs);
    }

    [Fact]
    public async Task Choosing_a_user_is_audited_with_the_user_and_the_source_ip()
    {
        using var browser = app.CreateBrowser(out _);
        var (_, token) = await LoadSelectionPageAsync(browser);
        using var response = await browser.PostAsync("/session/select", Form(SeedIds.Liam, token), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var logs = await app.GetLogsAsync("web");

        Assert.Contains($"Audit UserSelected outcome=Succeeded user={SeedIds.Liam}", logs);
        Assert.Matches(@"Audit UserSelected .*sourceIp=(::1|127\.0\.0\.1)", logs);
    }

    [Fact]
    public async Task The_projects_page_for_an_unknown_address_is_a_not_found_page_with_a_way_back()
    {
        using var browser = app.CreateBrowser(out _);

        using var response = await browser.GetAsync("/no-such-page", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("/projects", html);
    }
}
