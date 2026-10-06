using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Tests for the Notifications API event intake <c>POST /internal/events</c> and the real-time hub <c>/hubs/board</c>
/// (spec FR-022, FR-025, FR-026, SC-008; User Story 5; contracts/events.asyncapi.yaml, contracts/realtime-hub.md,
/// research R5, R8). They move a sample task, so they run in the sequential <see cref="SeedData.Collection"/>.
/// </summary>
/// <param name="app">The running application.</param>
[Collection(SeedData.Collection)]
public class EventsContractTests(TaskifyAppFixture app)
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    private HttpClient Notifications(string? key) => app.CreateClient("notifications-api", key);

    private static string Envelope(string type, Guid eventId, string payload, string version = "1", string extra = "", Guid? actor = null) =>
        $$"""{"eventId":"{{eventId}}","type":"{{type}}","version":{{version}},"occurredAt":"{{DateTimeOffset.UtcNow:O}}","actorUserId":"{{actor ?? SeedIds.Priya}}","payload":{{payload}}{{extra}}}""";

    private static string TaskMovedPayload(Guid projectId, Guid taskId) =>
        $$"""{"taskId":"{{taskId}}","projectId":"{{projectId}}","title":"Event test","fromStatus":"ToDo","toStatus":"InProgress","assigneeUserId":null}""";

    private static string ProjectCreatedPayload(Guid projectId) =>
        $$"""{"projectId":"{{projectId}}","name":"Event test"}""";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string json) =>
        client.PostAsync("/internal/events", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private static string TaskMovedEvent(Guid? eventId = null, Guid? projectId = null) =>
        Envelope(EventTypes.TaskMoved, eventId ?? Guid.NewGuid(), TaskMovedPayload(projectId ?? Guid.NewGuid(), Guid.NewGuid()));

    // ---- intake ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Tasks_key_may_post_a_task_event_and_gets_202()
    {
        using var client = Notifications(TaskifyAppFixture.TasksKey);

        using var response = await PostAsync(client, TaskMovedEvent());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task The_Projects_key_may_post_ProjectCreated_and_gets_202()
    {
        using var client = Notifications(TaskifyAppFixture.ProjectsKey);

        using var response = await PostAsync(client, Envelope(EventTypes.ProjectCreated, Guid.NewGuid(), ProjectCreatedPayload(Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_repeated_eventId_gets_202_and_the_client_is_signalled_only_once()
    {
        var projectId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await using var hub = await HubClient.ConnectAsync(app, TaskifyAppFixture.WebKey);
        await hub.JoinProjectAsync(projectId);
        using var client = Notifications(TaskifyAppFixture.TasksKey);
        var body = TaskMovedEvent(eventId, projectId);

        using var first = await PostAsync(client, body);
        using var second = await PostAsync(client, body);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var signal = await hub.NextAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(eventId, signal.GetProperty("eventId").GetGuid());
        // No second effect: the duplicate produces no second BoardChanged.
        Assert.Null(await hub.NextOrNullAsync(Quiet));
    }

    [Fact]
    public async Task The_wrong_caller_for_the_type_gets_403()
    {
        using var projects = Notifications(TaskifyAppFixture.ProjectsKey);
        using var tasks = Notifications(TaskifyAppFixture.TasksKey);

        using var projectsSendingTaskEvent = await PostAsync(projects, TaskMovedEvent());
        using var tasksSendingProjectEvent = await PostAsync(
            tasks, Envelope(EventTypes.ProjectCreated, Guid.NewGuid(), ProjectCreatedPayload(Guid.NewGuid())));

        Assert.Equal(HttpStatusCode.Forbidden, projectsSendingTaskEvent.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, tasksSendingProjectEvent.StatusCode);
    }

    [Fact]
    public async Task The_Web_key_gets_403()
    {
        using var client = Notifications(TaskifyAppFixture.WebKey);

        using var response = await PostAsync(client, TaskMovedEvent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_or_unknown_key_gets_401()
    {
        using var none = Notifications(null);
        using var unknown = Notifications("not-a-key-1234567890");

        using var withoutKey = await PostAsync(none, TaskMovedEvent());
        using var withUnknownKey = await PostAsync(unknown, TaskMovedEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, withoutKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withUnknownKey.StatusCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"eventId":"not-a-uuid","type":"TaskMoved","version":1,"occurredAt":"2026-01-01T00:00:00Z","actorUserId":"11111111-1111-1111-1111-000000000003","payload":{}}""")]
    public async Task A_bad_envelope_gets_400(string body)
    {
        using var client = Notifications(TaskifyAppFixture.TasksKey);

        using var response = await PostAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_unknown_type_version_field_or_actor_gets_400_and_the_response_does_not_echo_the_input()
    {
        using var client = Notifications(TaskifyAppFixture.TasksKey);
        var payload = TaskMovedPayload(Guid.NewGuid(), Guid.NewGuid());
        var stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var bodies = new[]
        {
            Envelope("TaskDeleted", Guid.NewGuid(), payload),
            Envelope(EventTypes.TaskMoved, Guid.NewGuid(), payload, version: "2"),
            Envelope(EventTypes.TaskMoved, Guid.NewGuid(), payload, extra: ",\"extra\":\"smuggled-value\""),
            Envelope(EventTypes.TaskMoved, Guid.NewGuid(), payload[..^1] + ",\"description\":\"smuggled-value\"}"),
            Envelope(EventTypes.TaskMoved, Guid.NewGuid(), payload, actor: stranger),
            Envelope(EventTypes.TaskMoved, Guid.NewGuid(), payload.Replace("InProgress", "Blocked", StringComparison.Ordinal)),
        };

        foreach (var body in bodies)
        {
            using var response = await PostAsync(client, body);
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("smuggled-value", text);
            Assert.DoesNotContain(stranger.ToString(), text);
        }
    }

    // ---- hub -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(TaskifyAppFixture.TasksKey)]
    [InlineData(TaskifyAppFixture.ProjectsKey)]
    [InlineData(null)]
    [InlineData("not-a-key-1234567890")]
    public async Task A_hub_connection_with_a_key_other_than_the_Web_key_is_refused(string? key)
    {
        await using var connection = HubClient.Build(app, key);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(HubConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task A_hub_call_with_an_invalid_argument_gets_a_generic_error()
    {
        await using var hub = await HubClient.ConnectAsync(app, TaskifyAppFixture.WebKey);

        var empty = await Assert.ThrowsAsync<HubException>(() => hub.Connection.InvokeAsync("JoinProject", Guid.Empty, TestContext.Current.CancellationToken));
        var stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var unknownUser = await Assert.ThrowsAsync<HubException>(() => hub.Connection.InvokeAsync("JoinUser", stranger, TestContext.Current.CancellationToken));

        Assert.Contains("Invalid request", empty.Message);
        Assert.Contains("Invalid request", unknownUser.Message);
        Assert.DoesNotContain(stranger.ToString(), unknownUser.Message);
    }

    [Fact]
    public async Task A_task_move_in_the_Tasks_API_reaches_a_client_in_the_project_group_within_2_seconds()
    {
        await using var hub = await HubClient.ConnectAsync(app, TaskifyAppFixture.WebKey);
        await hub.JoinProjectAsync(SeedIds.MobileAppLaunch);
        using var tasks = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, SeedIds.Priya);
        var (taskId, from) = await FirstTaskAsync(tasks, SeedIds.MobileAppLaunch);
        var to = from == "InProgress" ? "InReview" : "InProgress";
        try
        {
            var clock = Stopwatch.StartNew();
            using var move = await MoveAsync(tasks, taskId, to);
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);

            var signal = await hub.NextMatchingAsync(
                s => s.GetProperty("type").GetString() == EventTypes.TaskMoved && s.GetProperty("taskId").GetGuid() == taskId,
                TimeSpan.FromSeconds(2));

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"BoardChanged took {clock.Elapsed}.");
            Assert.Equal(SeedIds.MobileAppLaunch, signal.GetProperty("projectId").GetGuid());
            Assert.Equal(SeedIds.Priya, signal.GetProperty("actorUserId").GetGuid());
            // A signal carries IDs only, never titles or text (R5).
            Assert.False(signal.TryGetProperty("title", out _));
        }
        finally
        {
            (await MoveAsync(tasks, taskId, from)).Dispose();
        }
    }

    [Fact]
    public async Task A_move_made_while_the_Notifications_API_is_stopped_is_delivered_after_it_restarts_FR_026()
    {
        var cancellation = TestContext.Current.CancellationToken;
        await using var hub = await HubClient.ConnectAsync(app, TaskifyAppFixture.WebKey, rejoin: SeedIds.MobileAppLaunch);
        await hub.JoinProjectAsync(SeedIds.MobileAppLaunch);
        using var tasks = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, SeedIds.Priya);
        var (taskId, from) = await FirstTaskAsync(tasks, SeedIds.MobileAppLaunch);
        var to = from == "InProgress" ? "InReview" : "InProgress";
        var restarted = false;
        try
        {
            await app.App.ResourceCommands.ExecuteCommandAsync("notifications-api", KnownResourceCommands.StopCommand, cancellation);
            await app.App.ResourceNotifications.WaitForResourceAsync("notifications-api", KnownResourceStates.Exited, cancellation)
                .WaitAsync(TimeSpan.FromSeconds(60), cancellation);

            // The write succeeds: the event waits in the Tasks API outbox (FR-026).
            using var move = await MoveAsync(tasks, taskId, to);
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);

            await app.App.ResourceCommands.ExecuteCommandAsync("notifications-api", KnownResourceCommands.StartCommand, cancellation);
            restarted = true;
            await app.App.ResourceNotifications.WaitForResourceHealthyAsync("notifications-api", cancellation)
                .WaitAsync(TimeSpan.FromSeconds(120), cancellation);

            var signal = await hub.NextMatchingAsync(
                s => s.GetProperty("type").GetString() == EventTypes.TaskMoved && s.GetProperty("taskId").GetGuid() == taskId,
                TimeSpan.FromSeconds(120));
            Assert.Equal(SeedIds.MobileAppLaunch, signal.GetProperty("projectId").GetGuid());
        }
        finally
        {
            if (!restarted)
            {
                await app.App.ResourceCommands.ExecuteCommandAsync("notifications-api", KnownResourceCommands.StartCommand, cancellation);
                await app.App.ResourceNotifications.WaitForResourceHealthyAsync("notifications-api", cancellation)
                    .WaitAsync(TimeSpan.FromSeconds(120), cancellation);
            }

            (await MoveAsync(tasks, taskId, from)).Dispose();
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static async Task<(Guid TaskId, string Status)> FirstTaskAsync(HttpClient tasks, Guid project)
    {
        var body = await tasks.GetStringAsync($"/api/tasks?projectId={project}", TestContext.Current.CancellationToken);
        var task = JsonDocument.Parse(body).RootElement.EnumerateArray().First(t => t.GetProperty("status").GetString() != "Done");
        return (task.GetProperty("id").GetGuid(), task.GetProperty("status").GetString()!);
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient tasks, Guid taskId, string toStatus) =>
        tasks.PostAsync(
            $"/api/tasks/{taskId}/moves",
            new StringContent($$"""{"toStatus":"{{toStatus}}"}""", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    /// <summary>A SignalR client that behaves like the Web server: Web key, group joins, <c>BoardChanged</c> signals.</summary>
    private sealed class HubClient : IAsyncDisposable
    {
        private readonly Channel<JsonElement> signals = Channel.CreateUnbounded<JsonElement>();

        private HubClient(HubConnection connection) => Connection = connection;

        public HubConnection Connection { get; }

        public static HubConnection Build(TaskifyAppFixture app, string? key, Guid? rejoinProject = null)
        {
            var builder = new HubConnectionBuilder()
                .WithUrl(new Uri(app.GetUri("notifications-api"), "/hubs/board"), options =>
                {
                    if (key is not null)
                    {
                        options.Headers["X-Api-Key"] = key;
                    }

                    // Accept only the ASP.NET Core development certificate that the services present on localhost.
                    options.HttpMessageHandlerFactory = _ => new SocketsHttpHandler
                    {
                        SslOptions = { RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.Subject == "CN=localhost" },
                    };
                    options.WebSocketConfiguration = socket =>
                        socket.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.Subject == "CN=localhost";
                });

            if (rejoinProject is not null)
            {
                builder.WithAutomaticReconnect(new RetryEverySecond());
            }

            return builder.Build();
        }

        public static async Task<HubClient> ConnectAsync(TaskifyAppFixture app, string key, Guid? rejoin = null)
        {
            var connection = Build(app, key, rejoin);
            var client = new HubClient(connection);
            connection.On<JsonElement>("BoardChanged", signal => client.signals.Writer.TryWrite(signal));
            if (rejoin is { } project)
            {
                // Groups do not survive a reconnect: the Web server rejoins them, then re-fetches (FR-026, R5).
                connection.Reconnected += _ => connection.InvokeAsync("JoinProject", project);
            }

            await connection.StartAsync(TestContext.Current.CancellationToken);
            return client;
        }

        public Task JoinProjectAsync(Guid projectId) => Connection.InvokeAsync("JoinProject", projectId, TestContext.Current.CancellationToken);

        public async Task<JsonElement> NextAsync(TimeSpan timeout) =>
            await NextOrNullAsync(timeout) ?? throw new TimeoutException($"No BoardChanged signal within {timeout}.");

        public async Task<JsonElement?> NextOrNullAsync(TimeSpan timeout)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                return await signals.Reader.ReadAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        public async Task<JsonElement> NextMatchingAsync(Func<JsonElement, bool> match, TimeSpan timeout)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < timeout)
            {
                var signal = await NextOrNullAsync(timeout - deadline.Elapsed);
                if (signal is { } found && match(found))
                {
                    return found;
                }
            }

            throw new TimeoutException($"No matching BoardChanged signal within {timeout}.");
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private sealed class RetryEverySecond : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.ElapsedTime < TimeSpan.FromMinutes(3) ? TimeSpan.FromSeconds(1) : null;
    }
}
