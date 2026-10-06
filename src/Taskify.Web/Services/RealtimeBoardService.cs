using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using Taskify.Contracts.Hub;
using Taskify.Security;
using Taskify.Security.ApiKeys;

namespace Taskify.Web.Services;

/// <summary>
/// One SignalR connection from the Web server to the Notifications API hub, shared by all circuits (research R5).
/// Group membership is reference-counted: the hub group is joined by the first subscriber and left by the last.
/// After a reconnect the service rejoins every group and tells every subscriber to re-fetch (a resync),
/// because signals sent while the connection was down are lost (FR-026).
/// </summary>
public sealed class RealtimeBoardService : IRealtimeBoard, IAsyncDisposable
{
    /// <summary>The hub address when service discovery has not provided one.</summary>
    public const string DefaultHubUrl = "https://notifications-api/hubs/board";

    private const int RecentEventCapacity = 1000;
    private const string ProjectsGroup = "projects";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private readonly Dictionary<string, int> groupCounts = new(StringComparer.Ordinal);
    private readonly List<Subscription> subscriptions = [];
    private readonly BoundedRecentSet<(string Kind, Guid EventId)> recent = new(RecentEventCapacity);
    private readonly HubConnection connection;
    private readonly ILogger<RealtimeBoardService> logger;
    private readonly CancellationTokenSource stop = new();
    private bool started;

    /// <summary>Creates the service. The connection is opened by the first subscription.</summary>
    /// <param name="keys">Gives the Web app's own API key, which the hub requires.</param>
    /// <param name="configuration">Gives the hub address resolved by Aspire, when there is one.</param>
    /// <param name="logger">Logs connection state only, never message content.</param>
    public RealtimeBoardService(IOptions<ApiKeyOptions> keys, IConfiguration configuration, ILogger<RealtimeBoardService> logger)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;

        // WebSockets do not go through HTTP service discovery, so use the address Aspire put in the configuration.
        var baseAddress = configuration["services:notifications-api:https:0"];
        var url = string.IsNullOrEmpty(baseAddress) ? DefaultHubUrl : baseAddress.TrimEnd('/') + "/hubs/board";
        if (!url.StartsWith("https://", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The hub address must use HTTPS (research R16).");
        }

        var ownKey = keys.Value.OwnKey;
        connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                if (!string.IsNullOrEmpty(ownKey))
                {
                    options.Headers.Add(TaskifyHeaders.ApiKey, ownKey);
                }
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<BoardChanged>("BoardChanged", signal => Route("BoardChanged", signal.EventId, ProjectGroup(signal.ProjectId)));
        connection.On<TaskChanged>("TaskChanged", signal => Route("TaskChanged", signal.EventId, TaskGroup(signal.TaskId)));
        connection.On<ProjectListChanged>("ProjectListChanged", signal => Route("ProjectListChanged", signal.EventId, ProjectsGroup));
        connection.Reconnected += _ => OnConnectedAsync();
    }

    private static string ProjectGroup(Guid id) => $"project:{id}";

    private static string TaskGroup(Guid id) => $"task:{id}";

    private static string UserGroup(Guid id) => $"user:{id}";

    /// <inheritdoc />
    public IDisposable SubscribeProject(Guid projectId, Action onSignal) =>
        Subscribe(ProjectGroup(projectId), "JoinProject", "LeaveProject", projectId, onSignal);

    /// <inheritdoc />
    public IDisposable SubscribeTask(Guid taskId, Action onSignal) =>
        Subscribe(TaskGroup(taskId), "JoinTask", "LeaveTask", taskId, onSignal);

    /// <inheritdoc />
    public IDisposable SubscribeUser(Guid userId, Action onSignal) =>
        Subscribe(UserGroup(userId), "JoinUser", "LeaveUser", userId, onSignal);

    /// <inheritdoc />
    public IDisposable SubscribeProjectList(Action onSignal) => Subscribe(ProjectsGroup, null, null, null, onSignal);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await connection.DisposeAsync();
        stop.Dispose();
    }

    private Releaser Subscribe(string group, string? joinMethod, string? leaveMethod, Guid? id, Action onSignal)
    {
        ArgumentNullException.ThrowIfNull(onSignal);
        var subscription = new Subscription(group, joinMethod, leaveMethod, id, onSignal);
        bool first;
        bool start = false;
        lock (gate)
        {
            subscriptions.Add(subscription);
            groupCounts[group] = groupCounts.GetValueOrDefault(group) + 1;
            first = groupCounts[group] == 1;
            if (!started)
            {
                started = true;
                start = true;
            }
        }

        if (start)
        {
            _ = ConnectAsync();   // the groups are joined once the connection is up
        }
        else if (first)
        {
            _ = InvokeIfConnectedAsync(joinMethod, id);
        }

        return new Releaser(() => Unsubscribe(subscription));
    }

    private void Unsubscribe(Subscription subscription)
    {
        bool last;
        lock (gate)
        {
            if (!subscriptions.Remove(subscription))
            {
                return;
            }

            var count = groupCounts[subscription.Group] - 1;
            last = count == 0;
            if (last)
            {
                groupCounts.Remove(subscription.Group);
            }
            else
            {
                groupCounts[subscription.Group] = count;
            }
        }

        if (last)
        {
            _ = InvokeIfConnectedAsync(subscription.LeaveMethod, subscription.Id);
        }
    }

    // Routes one signal to the subscribers of its group, unless this event was handled already.
    private void Route(string kind, Guid eventId, string group)
    {
        if (recent.TryAdd((kind, eventId)))
        {
            Notify(s => s.Group == group);
        }
    }

    private void Notify(Func<Subscription, bool> filter)
    {
        Subscription[] targets;
        lock (gate)
        {
            targets = subscriptions.Where(filter).ToArray();
        }

        foreach (var target in targets)
        {
            try
            {
                target.OnSignal();
            }
            catch (Exception exception)
            {
                // One broken screen must not stop the others from being told.
                logger.LogWarning(exception, "A realtime subscriber failed.");
            }
        }
    }

    private async Task ConnectAsync()
    {
        // WithAutomaticReconnect only covers a connection that was up once, so retry the first start here.
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(stop.Token);
                await OnConnectedAsync();
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The realtime hub is not reachable; retrying.");
                try
                {
                    await Task.Delay(RetryDelay, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    // After the first start and after every reconnect: groups are gone on the hub, so join them all again,
    // then make every screen re-fetch, because signals sent in the meantime were lost (FR-026).
    private async Task OnConnectedAsync()
    {
        Subscription[] joins;
        lock (gate)
        {
            joins = subscriptions.Where(s => s.JoinMethod is not null).DistinctBy(s => s.Group).ToArray();
        }

        foreach (var join in joins)
        {
            await InvokeIfConnectedAsync(join.JoinMethod, join.Id);
        }

        Notify(_ => true);
    }

    private async Task InvokeIfConnectedAsync(string? method, Guid? id)
    {
        if (method is null || id is null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await connection.InvokeAsync(method, id.Value, stop.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The next reconnect rejoins all groups; until then the screen keeps its last data.
            logger.LogWarning(exception, "A realtime group call failed.");
        }
    }

    private sealed record Subscription(string Group, string? JoinMethod, string? LeaveMethod, Guid? Id, Action OnSignal);

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? release = release;

        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
