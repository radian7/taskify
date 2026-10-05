using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Taskify.Contracts;
using Taskify.Security.Audit;
using Taskify.Web.Services;
using Taskify.Web.Tests.Support;

namespace Taskify.Web.Tests.Services;

/// <summary>The protected selected-user cookie (research R8; constitution Principle II: reject untrusted input).</summary>
public class SelectedUserCookieTests
{
    private static SelectedUserCookie Create() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void A_protected_value_round_trips()
    {
        var cookie = Create();

        Assert.Equal(SeedIds.Priya, cookie.TryUnprotect(cookie.Protect(SeedIds.Priya)));
    }

    [Fact]
    public void The_protected_value_does_not_contain_the_user_id()
    {
        var value = Create().Protect(SeedIds.Priya);

        Assert.DoesNotContain(SeedIds.Priya.ToString(), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-protected-at-all")]
    [InlineData("11111111-1111-1111-1111-000000000003")]
    public void Anything_that_was_not_issued_by_the_app_is_rejected(string? value) =>
        Assert.Null(Create().TryUnprotect(value));

    [Fact]
    public void A_tampered_value_is_rejected()
    {
        var cookie = Create();
        var value = cookie.Protect(SeedIds.Priya);
        var tampered = value[..^2] + (value[^2] == 'A' ? "B" : "A") + value[^1];

        Assert.Null(cookie.TryUnprotect(tampered));
    }

    [Fact]
    public void A_truncated_value_is_rejected()
    {
        var cookie = Create();
        var value = cookie.Protect(SeedIds.Priya);

        Assert.Null(cookie.TryUnprotect(value[..(value.Length / 2)]));
    }

    [Fact]
    public void A_value_issued_under_another_key_ring_is_rejected()
    {
        var issuedElsewhere = Create().Protect(SeedIds.Priya);

        Assert.Null(Create().TryUnprotect(issuedElsewhere));
    }

    [Fact]
    public void A_value_protected_for_another_purpose_is_rejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var cookie = new SelectedUserCookie(provider);
        var foreign = provider.CreateProtector("some.other.purpose").Protect(SeedIds.Priya.ToString("D"));

        Assert.Null(cookie.TryUnprotect(foreign));
    }

    [Fact]
    public void A_correctly_protected_empty_guid_is_rejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var cookie = new SelectedUserCookie(provider);

        // Same purpose and lifetime wrapper as the real cookie, but the payload is the empty GUID.
        var protector = provider.CreateProtector("Taskify.Web.SelectedUser.v1").ToTimeLimitedDataProtector();
        var value = protector.Protect(Guid.Empty.ToString("D"), TimeSpan.FromHours(1));

        Assert.Null(cookie.TryUnprotect(value));
    }

    [Fact]
    public void The_cookie_options_are_http_only_secure_and_same_site_strict()
    {
        var options = SelectedUserCookie.Options;

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal(SelectedUserCookie.Lifetime, options.MaxAge);
    }
}

/// <summary>Reading the cookie on every page request (spec FR-002, FR-032; plan: rejected cookies are audited).</summary>
public class SelectedUserMiddlewareTests
{
    private readonly RecordingAuditLogger audit = new();
    private readonly FakeUserDirectory users = new();
    private readonly SelectedUserCookie cookie = new(new EphemeralDataProtectionProvider());

    private async Task<(DefaultHttpContext Context, bool NextCalled)> RunAsync(string? cookieValue, string path = "/projects")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        context.Request.Path = path;
        if (cookieValue is not null)
        {
            context.Request.Headers.Cookie = $"{SelectedUserCookie.CookieName}={cookieValue}";
        }

        var called = false;
        var middleware = new SelectedUserMiddleware(_ => { called = true; return Task.CompletedTask; }, cookie, audit);
        await middleware.InvokeAsync(context, users);
        return (context, called);
    }

    private static string? UserClaim(HttpContext context) => context.User.FindFirstValue(TaskifyClaims.UserId);

    [Fact]
    public async Task Without_a_cookie_nobody_is_selected_but_the_ip_is_recorded()
    {
        var (context, nextCalled) = await RunAsync(null);

        Assert.True(nextCalled);
        Assert.Null(UserClaim(context));
        Assert.Equal("203.0.113.9", context.User.FindFirstValue(TaskifyClaims.ClientIp));
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task A_valid_cookie_for_a_known_user_selects_that_user()
    {
        var (context, _) = await RunAsync(cookie.Protect(SeedIds.Priya));

        Assert.Equal(SeedIds.Priya.ToString("D"), UserClaim(context));
        Assert.Empty(audit.Entries);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Theory]
    [InlineData("garbage-value")]
    [InlineData("CfDJ8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_cookie_that_fails_data_protection_is_rejected_audited_and_deleted(string value)
    {
        var (context, nextCalled) = await RunAsync(value);

        Assert.True(nextCalled);
        Assert.Null(UserClaim(context));
        AssertRejectedAndDeleted(context);
    }

    [Fact]
    public async Task A_valid_cookie_naming_an_unknown_user_is_rejected_audited_and_deleted()
    {
        var (context, nextCalled) = await RunAsync(cookie.Protect(Guid.NewGuid()));

        Assert.True(nextCalled);
        Assert.Null(UserClaim(context));
        AssertRejectedAndDeleted(context);
    }

    [Fact]
    public async Task When_the_directory_is_unreachable_the_request_has_no_selection_but_the_cookie_is_kept()
    {
        users.Unavailable = true;

        var (context, nextCalled) = await RunAsync(cookie.Protect(SeedIds.Priya));

        Assert.True(nextCalled);
        Assert.Null(UserClaim(context));
        Assert.Empty(audit.Entries);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_endpoints_are_not_touched(string path)
    {
        users.Unavailable = true;

        var (context, nextCalled) = await RunAsync("garbage", path);

        Assert.True(nextCalled);
        Assert.Empty(audit.Entries);
    }

    private void AssertRejectedAndDeleted(HttpContext context)
    {
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.RequestRejected, entry.Action);
        Assert.Equal(AuditOutcome.Validation, entry.Outcome);
        Assert.Equal("203.0.113.9", entry.SourceIp);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains(SelectedUserCookie.CookieName, setCookie);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Choosing a user (spec FR-002, FR-032): validation, the cookie and the audit record.</summary>
public class SessionEndpointsTests
{
    private readonly RecordingAuditLogger audit = new();
    private readonly FakeUserDirectory users = new();
    private readonly SelectedUserCookie cookie = new(new EphemeralDataProtectionProvider());

    private static DefaultHttpContext Request(Guid? previousUser = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        var claims = new List<Claim>();
        if (previousUser is { } previous)
        {
            claims.Add(new Claim(TaskifyClaims.UserId, previous.ToString("D")));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, TaskifyClaims.AuthenticationType));
        return context;
    }

    [Fact]
    public async Task Choosing_a_user_sets_the_cookie_audits_the_choice_and_goes_to_the_projects()
    {
        var context = Request();

        var result = await SessionEndpoints.SelectAsync(SeedIds.Priya, context, users, cookie, audit);

        var redirect = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult>(result);
        Assert.Equal("/projects", redirect.Url);
        Assert.Contains(SelectedUserCookie.CookieName, context.Response.Headers.SetCookie.ToString());
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.UserSelected, entry.Action);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(SeedIds.Priya, entry.ActingUserId);
        Assert.Null(entry.PreviousUserId);
        Assert.Equal("203.0.113.9", entry.SourceIp);
    }

    [Fact]
    public async Task Switching_records_the_previous_user_too()
    {
        var context = Request(previousUser: SeedIds.Priya);

        await SessionEndpoints.SelectAsync(SeedIds.Jordan, context, users, cookie, audit);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.UserSelected, entry.Action);
        Assert.Equal(SeedIds.Jordan, entry.ActingUserId);
        Assert.Equal(SeedIds.Priya, entry.PreviousUserId);
    }

    [Fact]
    public async Task The_cookie_value_that_is_set_identifies_the_chosen_user()
    {
        var context = Request();

        await SessionEndpoints.SelectAsync(SeedIds.Tomasz, context, users, cookie, audit);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        var value = Uri.UnescapeDataString(setCookie.Split(';')[0][(SelectedUserCookie.CookieName.Length + 1)..]);
        Assert.Equal(SeedIds.Tomasz, cookie.TryUnprotect(value));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("99999999-9999-9999-9999-999999999999")]
    public async Task An_empty_or_unknown_user_is_rejected_audited_and_sets_no_cookie(string id)
    {
        var context = Request(previousUser: SeedIds.Priya);

        var result = await SessionEndpoints.SelectAsync(Guid.Parse(id), context, users, cookie, audit);

        var redirect = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult>(result);
        Assert.Equal("/", redirect.Url);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.RequestRejected, entry.Action);
        Assert.Equal(AuditOutcome.Validation, entry.Outcome);
        Assert.Equal(SeedIds.Priya, entry.PreviousUserId);
    }
}

/// <summary>The selection a circuit sees (spec FR-002, FR-032).</summary>
public class CurrentUserServiceTests
{
    private readonly RecordingAuditLogger audit = new();

    private CurrentUserService Create(Guid? userId) =>
        new(TestIdentity.For(userId), new FakeUserDirectory(), audit);

    [Fact]
    public async Task A_selected_user_is_loaded_with_name_and_role_and_the_restore_is_audited_with_the_ip()
    {
        var service = Create(SeedIds.Priya);

        await service.InitializeAsync();

        Assert.Equal("Priya Patel", service.Current?.DisplayName);
        Assert.Equal(UserRole.Engineer, service.Current?.Role);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.UserSelectionRestored, entry.Action);
        Assert.Equal(SeedIds.Priya, entry.ActingUserId);
        Assert.Equal("203.0.113.9", entry.SourceIp);
    }

    [Fact]
    public async Task Nobody_selected_means_no_current_user_and_nothing_audited()
    {
        var service = Create(null);

        await service.InitializeAsync();

        Assert.Null(service.Current);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task A_user_id_the_directory_does_not_know_is_not_selected()
    {
        var service = Create(Guid.NewGuid());

        await service.InitializeAsync();

        Assert.Null(service.Current);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Initialising_twice_audits_once()
    {
        var service = Create(SeedIds.Priya);

        await service.InitializeAsync();
        await service.InitializeAsync();

        Assert.Single(audit.Entries);
    }
}
