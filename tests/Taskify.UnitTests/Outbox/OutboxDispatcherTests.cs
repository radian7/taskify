using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.Security.Audit;
using Taskify.Security.Outbox;
using Taskify.UnitTests.Security;

namespace Taskify.UnitTests.Outbox;

/// <summary>Tests for outbox delivery (research R4, FR-026): retries, dead-lettering and ordering.</summary>
public sealed class OutboxDispatcherTests : IDisposable
{
    private readonly RecordingAuditLogger audit = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeHandler handler = new();
    private readonly ServiceProvider provider;

    public OutboxDispatcherTests()
    {
        // The name must be fixed per test: the options lambda runs for every scope, and each name is a separate database.
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TestDb>(o => o.UseInMemoryDatabase(databaseName));
        services.AddHttpClient(OutboxDefaults.HttpClientName, c => c.BaseAddress = new Uri("https://notifications-api"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        provider.Dispose();
        handler.Dispose();
    }

    private OutboxDispatcher<TestDb> Dispatcher() => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IHttpClientFactory>(),
        audit,
        time,
        NullLogger<OutboxDispatcher<TestDb>>.Instance);

    private async Task<Guid> AddAsync(string type = EventTypes.TaskMoved, int minutesAgo = 0)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        var message = OutboxWriter.Add(
            db,
            type,
            SeedIds.Priya,
            new TaskMovedPayload(Guid.NewGuid(), SeedIds.MobileAppLaunch, "Login page", TaskStatus.ToDo, TaskStatus.InReview, SeedIds.Jordan),
            time.GetUtcNow().AddMinutes(-minutesAgo));
        await db.SaveChangesAsync();
        return message.Id;
    }

    private async Task<OutboxMessage> GetAsync(Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestDb>().Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task An_empty_outbox_waits_the_idle_delay() =>
        Assert.Equal(OutboxDefaults.IdleDelay, await Dispatcher().DispatchBatchAsync(CancellationToken.None));

    [Fact]
    public async Task A_202_marks_the_event_dispatched_and_sends_a_valid_envelope()
    {
        var id = await AddAsync();
        handler.Respond(HttpStatusCode.Accepted);

        await Dispatcher().DispatchBatchAsync(CancellationToken.None);

        var stored = await GetAsync(id);
        Assert.Equal(time.GetUtcNow(), stored.DispatchedAt);
        Assert.Null(stored.DeadLetteredAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/internal/events", request.Path);
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(request.Body, ContractJson.Options)!;
        Assert.Equal(id, envelope.EventId);
        Assert.Equal(EventTypes.TaskMoved, envelope.Type);
        Assert.Equal(1, envelope.Version);
        Assert.Equal(SeedIds.Priya, envelope.ActorUserId);
        var payload = envelope.Payload.Deserialize<TaskMovedPayload>(ContractJson.Options)!;
        Assert.Equal(TaskStatus.InReview, payload.ToStatus);
        Assert.Equal("Login page", payload.Title);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_permanent_rejection_dead_letters_audits_counts_and_is_never_resent(HttpStatusCode status)
    {
        // A unique event type keeps this test independent of other tests that touch the global counter.
        var type = "DeadLetterTest" + Guid.NewGuid().ToString("N");
        var id = await AddAsync(type);
        handler.Respond(status);

        var counted = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "taskify.outbox.deadlettered")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "type" && (string?)tag.Value == type)
                {
                    Interlocked.Add(ref counted, value);
                }
            }
        });
        listener.Start();

        var dispatcher = Dispatcher();
        await dispatcher.DispatchBatchAsync(CancellationToken.None);
        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        var stored = await GetAsync(id);
        Assert.Equal(time.GetUtcNow(), stored.DeadLetteredAt);
        Assert.Null(stored.DispatchedAt);
        Assert.Single(handler.Requests);
        Assert.Equal(1, Interlocked.Read(ref counted));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.EventDeadLettered, entry.Action);
        Assert.Equal(AuditOutcome.Error, entry.Outcome);
        Assert.Equal(id, entry.EntityId);
        Assert.Equal(type, entry.EntityType);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_transient_failure_counts_an_attempt_and_leaves_the_event_pending(HttpStatusCode status)
    {
        var id = await AddAsync();
        handler.Respond(status);

        var delay = await Dispatcher().DispatchBatchAsync(CancellationToken.None);

        var stored = await GetAsync(id);
        Assert.Equal(1, stored.Attempts);
        Assert.Null(stored.DispatchedAt);
        Assert.Null(stored.DeadLetteredAt);
        Assert.Equal(TimeSpan.FromSeconds(1), delay);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task A_connection_failure_is_retried()
    {
        var id = await AddAsync();
        handler.Throw(new HttpRequestException("connection refused"));

        await Dispatcher().DispatchBatchAsync(CancellationToken.None);

        var stored = await GetAsync(id);
        Assert.Equal(1, stored.Attempts);
        Assert.Null(stored.DeadLetteredAt);
    }

    [Fact]
    public async Task A_failed_event_is_delivered_once_the_receiver_recovers()
    {
        var id = await AddAsync();
        handler.Respond(HttpStatusCode.ServiceUnavailable);
        handler.Respond(HttpStatusCode.Accepted);
        var dispatcher = Dispatcher();

        await dispatcher.DispatchBatchAsync(CancellationToken.None);
        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        var stored = await GetAsync(id);
        Assert.NotNull(stored.DispatchedAt);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Events_are_sent_oldest_first_and_a_failure_stops_the_batch()
    {
        var older = await AddAsync(minutesAgo: 10);
        var newer = await AddAsync(minutesAgo: 1);
        handler.Respond(HttpStatusCode.ServiceUnavailable);

        await Dispatcher().DispatchBatchAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Contains(older.ToString(), handler.Requests[0].Body);
        Assert.Equal(1, (await GetAsync(older)).Attempts);
        Assert.Equal(0, (await GetAsync(newer)).Attempts);
    }

    [Fact]
    public async Task Several_events_are_delivered_in_one_batch_in_order()
    {
        var first = await AddAsync(minutesAgo: 5);
        var second = await AddAsync(minutesAgo: 2);
        handler.Respond(HttpStatusCode.Accepted);
        handler.Respond(HttpStatusCode.Accepted);

        var delay = await Dispatcher().DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, delay);
        Assert.Contains(first.ToString(), handler.Requests[0].Body);
        Assert.Contains(second.ToString(), handler.Requests[1].Body);
        Assert.NotNull((await GetAsync(first)).DispatchedAt);
        Assert.NotNull((await GetAsync(second)).DispatchedAt);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(20, 60)]
    public void Backoff_doubles_and_is_capped_at_one_minute(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OutboxDefaults.Backoff(attempts));

    /// <summary>A database context that holds only the outbox, enough to exercise the dispatcher.</summary>
    public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ConfigureOutbox();
    }

    private sealed record SentRequest(string Path, string Body);

    /// <summary>Plays back scripted responses and records what was sent.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> script = new();

        public List<SentRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode status) => script.Enqueue(() => new HttpResponseMessage(status));

        public void Throw(Exception exception) => script.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return script.Count > 0 ? script.Dequeue()() : new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
