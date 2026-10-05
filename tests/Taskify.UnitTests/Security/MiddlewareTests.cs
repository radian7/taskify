using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Users;

namespace Taskify.UnitTests.Security;

/// <summary>Tests for the API key matrix (research R8, deviation D2): 401 for unknown keys, 403 for the wrong route.</summary>
public class ApiKeyMiddlewareTests
{
    private const string WebKey = "web-key-0123456789";
    private const string TasksKey = "tasks-key-0123456789";

    private readonly RecordingAuditLogger audit = new();

    private (ApiKeyMiddleware Middleware, Func<bool> NextCalled) Create()
    {
        var called = false;
        var options = Options.Create(new ApiKeyOptions
        {
            Accepted = new Dictionary<string, string> { [Callers.Web] = WebKey, [Callers.Tasks] = TasksKey },
        });
        var middleware = new ApiKeyMiddleware(_ => { called = true; return Task.CompletedTask; }, options, audit);
        return (middleware, () => called);
    }

    private static DefaultHttpContext Request(string? key, params object[]? metadata)
    {
        var context = HttpContextFactory.Create(metadata);
        context.Request.Path = "/api/projects";
        if (key is not null)
        {
            context.Request.Headers[TaskifyHeaders.ApiKey] = key;
        }

        return context;
    }

    [Fact]
    public async Task Missing_key_gets_401()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, new AllowCallersAttribute(Callers.Web));

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled());
        Assert.Equal(AuditOutcome.Unauthorized, Assert.Single(audit.Entries).Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("web-key-012345678")]
    [InlineData("WEB-KEY-0123456789")]
    public async Task Unknown_key_gets_401(string key)
    {
        var (middleware, nextCalled) = Create();
        var context = Request(key, new AllowCallersAttribute(Callers.Web));

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Repeated_key_header_gets_401()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, new AllowCallersAttribute(Callers.Web));
        context.Request.Headers[TaskifyHeaders.ApiKey] = new[] { WebKey, WebKey };

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Known_caller_on_a_route_that_does_not_allow_it_gets_403()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(TasksKey, new AllowCallersAttribute(Callers.Web));

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(nextCalled());
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditOutcome.Forbidden, entry.Outcome);
        Assert.Equal(Callers.Tasks, context.GetCaller());
    }

    [Fact]
    public async Task Route_without_allowed_callers_is_closed_to_everyone()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(WebKey, new object());

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(nextCalled());
    }

    [Theory]
    [InlineData(WebKey, Callers.Web)]
    [InlineData(TasksKey, Callers.Tasks)]
    public async Task Allowed_caller_passes_and_is_recorded(string key, string expectedCaller)
    {
        var (middleware, nextCalled) = Create();
        var context = Request(key, new AllowCallersAttribute(Callers.Web, Callers.Tasks));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
        Assert.Equal(expectedCaller, context.GetCaller());
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Unknown_route_with_a_valid_key_falls_through_to_the_404()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(WebKey, metadata: null);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/HEALTH")]
    public async Task Health_endpoints_need_no_key(string path)
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, metadata: null);
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task A_path_that_only_starts_with_health_is_not_exempt()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, metadata: null);
        context.Request.Path = "/health/secrets";

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task The_401_body_is_problem_details_without_the_presented_key()
    {
        var (middleware, _) = Create();
        var context = Request("super-secret-guess", new AllowCallersAttribute(Callers.Web));

        await middleware.InvokeAsync(context);

        var body = HttpContextFactory.ReadBody(context);
        Assert.Contains("\"status\":401", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("super-secret-guess", body);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
    }
}

/// <summary>Tests for validation of the acting-user header (spec FR-021; contracts/*-api.yaml).</summary>
public class ActingUserMiddlewareTests
{
    private static readonly Guid Priya = new("11111111-1111-1111-1111-000000000003");

    private readonly RecordingAuditLogger audit = new();
    private readonly FakeUserDirectory users = new(Priya);

    private (ActingUserMiddleware Middleware, Func<bool> NextCalled) Create()
    {
        var called = false;
        return (new ActingUserMiddleware(_ => { called = true; return Task.CompletedTask; }, audit), () => called);
    }

    private static DefaultHttpContext Request(string? header, params object[]? metadata)
    {
        var context = HttpContextFactory.Create(metadata);
        context.Request.Path = "/api/projects";
        if (header is not null)
        {
            context.Request.Headers[TaskifyHeaders.ActingUser] = header;
        }

        return context;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("99999999-9999-9999-9999-999999999999")]
    public async Task Missing_malformed_empty_or_unknown_user_gets_400(string? header)
    {
        var (middleware, nextCalled) = Create();
        var context = Request(header, new object());

        await middleware.InvokeAsync(context, users);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.False(nextCalled());
        Assert.Equal(AuditOutcome.Validation, Assert.Single(audit.Entries).Outcome);
        Assert.DoesNotContain("not-a-guid", HttpContextFactory.ReadBody(context));
    }

    [Fact]
    public async Task Repeated_user_header_gets_400()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, new object());
        context.Request.Headers[TaskifyHeaders.ActingUser] = new[] { Priya.ToString(), Priya.ToString() };

        await middleware.InvokeAsync(context, users);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task Known_user_passes_and_is_recorded()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(Priya.ToString(), new object());

        await middleware.InvokeAsync(context, users);

        Assert.True(nextCalled());
        Assert.Equal(Priya, context.GetActingUserId());
    }

    [Fact]
    public async Task Routes_marked_to_skip_the_acting_user_pass_without_a_header()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, new SkipActingUserAttribute());

        await middleware.InvokeAsync(context, users);

        Assert.True(nextCalled());
        Assert.Null(context.GetActingUserId());
    }

    [Fact]
    public async Task Unknown_routes_fall_through_to_the_404()
    {
        var (middleware, nextCalled) = Create();
        var context = Request(null, metadata: null);

        await middleware.InvokeAsync(context, users);

        Assert.True(nextCalled());
    }
}

/// <summary>Tests for the source IP rules used in audit logs (spec FR-032, research R8).</summary>
public class ClientIpMiddlewareTests
{
    private static DefaultHttpContext Request(string? caller, string? forwarded)
    {
        var context = HttpContextFactory.Create();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.7");
        if (caller is not null)
        {
            context.Items["Taskify.Caller"] = caller;
        }

        if (forwarded is not null)
        {
            context.Request.Headers[TaskifyHeaders.ClientIp] = forwarded;
        }

        return context;
    }

    private static async Task<string?> RunAsync(DefaultHttpContext context)
    {
        await new ClientIpMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        return context.GetClientIp();
    }

    [Fact]
    public async Task The_forwarded_ip_is_used_when_the_caller_is_the_web_app() =>
        Assert.Equal("203.0.113.9", await RunAsync(Request(Callers.Web, "203.0.113.9")));

    [Fact]
    public async Task The_forwarded_ip_is_normalised_for_ipv6() =>
        Assert.Equal("2001:db8::1", await RunAsync(Request(Callers.Web, "2001:0db8:0000:0000:0000:0000:0000:0001")));

    [Theory]
    [InlineData(Callers.Tasks)]
    [InlineData(Callers.Projects)]
    [InlineData(Callers.Notifications)]
    public async Task The_forwarded_ip_is_ignored_from_other_callers(string caller) =>
        Assert.Equal("10.0.0.7", await RunAsync(Request(caller, "203.0.113.9")));

    [Theory]
    [InlineData("not an ip")]
    [InlineData("203.0.113.9; DROP TABLE")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("")]
    public async Task An_invalid_forwarded_ip_is_ignored(string forwarded) =>
        Assert.Equal("10.0.0.7", await RunAsync(Request(Callers.Web, forwarded)));

    [Fact]
    public async Task A_repeated_forwarded_ip_header_is_ignored()
    {
        var context = Request(Callers.Web, null);
        context.Request.Headers[TaskifyHeaders.ClientIp] = new[] { "203.0.113.9", "203.0.113.10" };

        Assert.Equal("10.0.0.7", await RunAsync(context));
    }

    [Fact]
    public async Task Without_a_caller_the_connection_address_is_used() =>
        Assert.Equal("10.0.0.7", await RunAsync(Request(null, "203.0.113.9")));
}
