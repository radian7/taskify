using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Taskify.Contracts;
using Taskify.Security.Audit;
using Taskify.Security.Users;
using Taskify.Web.Services;
using Taskify.Web.Services.ApiClients;

namespace Taskify.Web.Tests.Support;

/// <summary>An <see cref="IAuditLogger"/> that keeps entries in memory.</summary>
public sealed class RecordingAuditLogger : IAuditLogger
{
    /// <summary>Gets the entries written so far.</summary>
    public List<AuditEntry> Entries { get; } = [];

    /// <inheritdoc />
    public void Log(AuditEntry entry) => Entries.Add(entry);
}

/// <summary>A fixed user directory that can also simulate the Projects API being down.</summary>
public sealed class FakeUserDirectory : IUserDirectory
{
    private readonly IReadOnlyList<UserInfo> users;

    /// <summary>Creates a directory with the five predefined users.</summary>
    public FakeUserDirectory() => users = SampleUsers.All;

    /// <summary>Gets or sets a value indicating whether lookups fail as if the Projects API were unreachable.</summary>
    public bool Unavailable { get; set; }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        GetAllAsync(cancellationToken).ContinueWith(t => t.Result.Any(u => u.Id == userId), TaskScheduler.Default);

    /// <inheritdoc />
    public Task<IReadOnlyList<UserInfo>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Unavailable
            ? throw new Taskify.Security.Errors.ServiceUnavailableException("simulated outage")
            : Task.FromResult(users);
}

/// <summary>The five predefined users, as the Projects API returns them.</summary>
public static class SampleUsers
{
    /// <summary>Maya Chen, Product Manager.</summary>
    public static readonly UserInfo Maya = new(SeedIds.Maya, "Maya Chen", UserRole.ProductManager);

    /// <summary>Jordan Lee, Engineer.</summary>
    public static readonly UserInfo Jordan = new(SeedIds.Jordan, "Jordan Lee", UserRole.Engineer);

    /// <summary>Liam Novak, Engineer.</summary>
    public static readonly UserInfo Liam = new(SeedIds.Liam, "Liam Novak", UserRole.Engineer);

    /// <summary>Priya Patel, Engineer.</summary>
    public static readonly UserInfo Priya = new(SeedIds.Priya, "Priya Patel", UserRole.Engineer);

    /// <summary>Tomasz Wiśniewski, Engineer.</summary>
    public static readonly UserInfo Tomasz = new(SeedIds.Tomasz, "Tomasz Wiśniewski", UserRole.Engineer);

    /// <summary>All five, Product Manager first.</summary>
    public static readonly IReadOnlyList<UserInfo> All = [Maya, Jordan, Liam, Priya, Tomasz];
}

/// <summary>An authentication state with the claims <see cref="SelectedUserMiddleware"/> would have set.</summary>
/// <param name="userId">The selected user, or <see langword="null"/>.</param>
/// <param name="clientIp">The browser IP, or <see langword="null"/>.</param>
public sealed class FakeAuthenticationStateProvider(Guid? userId, string? clientIp) : AuthenticationStateProvider
{
    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var claims = new List<Claim>();
        if (userId is { } id)
        {
            claims.Add(new Claim(TaskifyClaims.UserId, id.ToString("D")));
        }

        if (clientIp is not null)
        {
            claims.Add(new Claim(TaskifyClaims.ClientIp, clientIp));
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, TaskifyClaims.AuthenticationType))));
    }
}

/// <summary>Builders for the pieces components need.</summary>
public static class TestIdentity
{
    /// <summary>Creates the circuit identity of a browser.</summary>
    /// <param name="userId">The selected user, or <see langword="null"/>.</param>
    /// <param name="clientIp">The browser IP.</param>
    /// <returns>The identity.</returns>
    public static CircuitIdentity For(Guid? userId, string? clientIp = "203.0.113.9") =>
        new(new FakeAuthenticationStateProvider(userId, clientIp));
}

/// <summary>Plays back canned JSON for paths and records what was requested.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Json)> responses = new(StringComparer.Ordinal);

    /// <summary>Gets the requests received, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Gets what was sent, in order: method, path with query, and body text.</summary>
    public List<(string Method, string PathAndQuery, string Body)> Sent { get; } = [];

    /// <summary>Registers a response for a path and query.</summary>
    /// <param name="pathAndQuery">For example <c>/api/users</c>.</param>
    /// <param name="json">The JSON body.</param>
    /// <param name="status">The status code.</param>
    /// <returns>The handler, for chaining.</returns>
    public StubHandler On(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        responses[pathAndQuery] = (status, json);
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Sent.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
        var key = request.RequestUri!.PathAndQuery;
        if (!responses.TryGetValue(key, out var response))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/problem+json"),
            };
        }

        return new HttpResponseMessage(response.Status)
        {
            Content = new StringContent(response.Json, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Creates typed clients that talk to a <see cref="StubHandler"/>.</summary>
public static class TestClients
{
    /// <summary>The address the stub clients use.</summary>
    public static readonly Uri BaseAddress = new("https://stub.invalid");

    /// <summary>Creates a Projects client.</summary>
    /// <param name="handler">The canned responses.</param>
    /// <param name="identity">The circuit identity.</param>
    /// <returns>The client.</returns>
    public static ProjectsClient Projects(StubHandler handler, CircuitIdentity identity) =>
        new(new HttpClient(handler, disposeHandler: false) { BaseAddress = BaseAddress }, identity);

    /// <summary>Creates a Tasks client.</summary>
    /// <param name="handler">The canned responses.</param>
    /// <param name="identity">The circuit identity.</param>
    /// <returns>The client.</returns>
    public static TasksClient Tasks(StubHandler handler, CircuitIdentity identity) =>
        new(new HttpClient(handler, disposeHandler: false) { BaseAddress = BaseAddress }, identity);
}

/// <summary>Creates the Notifications client that talks to a <see cref="StubHandler"/>.</summary>
public static class NotificationTestClients
{
    /// <summary>Creates a Notifications client.</summary>
    /// <param name="handler">The canned responses.</param>
    /// <param name="identity">The circuit identity.</param>
    /// <returns>The client.</returns>
    public static NotificationsClient Notifications(StubHandler handler, CircuitIdentity identity) =>
        new(new HttpClient(handler, disposeHandler: false) { BaseAddress = TestClients.BaseAddress }, identity);
}

/// <summary>A realtime service that records subscriptions and lets a test raise signals by hand.</summary>
public sealed class FakeRealtimeBoard : IRealtimeBoard
{
    private readonly List<Entry> entries = [];

    /// <summary>Gets the number of subscriptions that are still active.</summary>
    public int ActiveCount => entries.Count(e => !e.Disposed);

    /// <summary>Gets the groups subscribed to so far, in order, for example <c>project:{id}</c>.</summary>
    public List<string> Subscribed { get; } = [];

    /// <inheritdoc />
    public IDisposable SubscribeProject(Guid projectId, Action onSignal) => Add($"project:{projectId}", onSignal);

    /// <inheritdoc />
    public IDisposable SubscribeTask(Guid taskId, Action onSignal) => Add($"task:{taskId}", onSignal);

    /// <inheritdoc />
    public IDisposable SubscribeUser(Guid userId, Action onSignal) => Add($"user:{userId}", onSignal);

    /// <inheritdoc />
    public IDisposable SubscribeProjectList(Action onSignal) => Add("projects", onSignal);

    /// <summary>Raises a change signal for one group.</summary>
    /// <param name="group">The group name.</param>
    public void Raise(string group)
    {
        foreach (var entry in entries.Where(e => !e.Disposed && e.Group == group).ToList())
        {
            entry.OnSignal();
        }
    }

    /// <summary>Raises a resync: every active subscriber is told to re-fetch.</summary>
    public void Resync()
    {
        foreach (var entry in entries.Where(e => !e.Disposed).ToList())
        {
            entry.OnSignal();
        }
    }

    private Entry Add(string group, Action onSignal)
    {
        var entry = new Entry(group, onSignal);
        entries.Add(entry);
        Subscribed.Add(group);
        return entry;
    }

    private sealed class Entry(string group, Action onSignal) : IDisposable
    {
        public string Group { get; } = group;

        public Action OnSignal { get; } = onSignal;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}

/// <summary>Registers the realtime pieces the pages need.</summary>
public static class RealtimeTestServices
{
    /// <summary>Registers a <see cref="FakeRealtimeBoard"/> (also as <see cref="IRealtimeBoard"/>) and a fake clock.</summary>
    /// <param name="services">The bUnit service collection.</param>
    /// <returns>The fake board.</returns>
    public static FakeRealtimeBoard AddFakeRealtime(this IServiceCollection services)
    {
        var fake = new FakeRealtimeBoard();
        services.AddSingleton(fake);
        services.AddSingleton<IRealtimeBoard>(fake);
        services.TryAddSingleton<TimeProvider>(new FakeTimeProvider());
        return fake;
    }
}
