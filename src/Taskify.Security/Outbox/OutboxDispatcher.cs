using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;

namespace Taskify.Security.Outbox;

/// <summary>Settings shared by every outbox dispatcher.</summary>
public static class OutboxDefaults
{
    /// <summary>The name of the <see cref="HttpClient"/> that talks to the Notifications API.</summary>
    public const string HttpClientName = "taskify-outbox";

    /// <summary>The Notifications API intake route (contracts/events.asyncapi.yaml).</summary>
    public const string EventsPath = "/internal/events";

    /// <summary>How long to wait when there is nothing to send. Short, so live updates meet the 2-second goal (FR-025).</summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>The longest wait between retries (data-model.md: backoff capped at 1 minute).</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    /// <summary>Calculates the wait after a failed attempt: 1 s, 2 s, 4 s … capped at one minute.</summary>
    /// <param name="attempts">The number of failed attempts so far (at least 1).</param>
    /// <returns>The delay.</returns>
    public static TimeSpan Backoff(int attempts)
    {
        var seconds = Math.Pow(2, Math.Clamp(attempts - 1, 0, 6));
        var delay = TimeSpan.FromSeconds(seconds);
        return delay > MaxBackoff ? MaxBackoff : delay;
    }
}

/// <summary>
/// Delivers outbox events to the Notifications API in the order they happened (research R4). A
/// <c>202</c> marks the event dispatched. A <c>400</c> or <c>403</c> means the receiver will never accept it, so the
/// event is dead-lettered, audited and counted. Everything else (5xx, 401, 404, 429, timeouts) is retried with
/// exponential backoff, so a Notifications API outage loses nothing (FR-026).
/// </summary>
/// <typeparam name="TDbContext">The service's database context, which must map <see cref="OutboxMessage"/>.</typeparam>
/// <param name="scopes">Creates a scope (and database context) for each batch.</param>
/// <param name="httpClients">Creates the HTTP client for the Notifications API.</param>
/// <param name="audit">Records dead-lettered events.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The log sink. It never receives event payloads.</param>
public sealed class OutboxDispatcher<TDbContext>(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IAuditLogger audit,
    TimeProvider time,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private const int BatchSize = 50;

    /// <summary>Sends one batch of pending events.</summary>
    /// <param name="cancellationToken">Cancels the batch.</param>
    /// <returns>How long to wait before the next batch.</returns>
    public async Task<TimeSpan> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var pending = await db.Set<OutboxMessage>()
            .Where(m => m.DispatchedAt == null && m.DeadLetteredAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return OutboxDefaults.IdleDelay;
        }

        var client = httpClients.CreateClient(OutboxDefaults.HttpClientName);

        foreach (var message in pending)
        {
            var result = await SendAsync(client, message, cancellationToken);
            switch (result)
            {
                case SendResult.Delivered:
                    message.DispatchedAt = time.GetUtcNowMicroseconds();
                    await db.SaveChangesAsync(cancellationToken);
                    break;

                case SendResult.Permanent:
                    message.DeadLetteredAt = time.GetUtcNowMicroseconds();
                    await db.SaveChangesAsync(cancellationToken);
                    DeadLetter(message);
                    break;

                default:
                    message.Attempts++;
                    await db.SaveChangesAsync(cancellationToken);

                    // The receiver is probably down; later events would fail too, so stop and back off.
                    return OutboxDefaults.Backoff(message.Attempts);
            }
        }

        return TimeSpan.Zero;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                delay = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // For example the database is briefly unavailable. Log the exception type only.
                logger.LogError("Outbox dispatch failed: {ExceptionType}", exception.GetType().Name);
                delay = TimeSpan.FromSeconds(5);
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static async Task<SendResult> SendAsync(HttpClient client, OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var payload = JsonDocument.Parse(message.Payload);
            var envelope = new EventEnvelope(
                message.Id,
                message.Type,
                EventEnvelope.CurrentVersion,
                message.OccurredAt,
                message.ActorUserId,
                payload.RootElement.Clone());

            using var response = await client.PostAsJsonAsync(OutboxDefaults.EventsPath, envelope, ContractJson.Options, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Accepted or HttpStatusCode.OK => SendResult.Delivered,
                HttpStatusCode.BadRequest or HttpStatusCode.Forbidden => SendResult.Permanent,
                _ => SendResult.Retry,
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Connection refused, DNS failure or timeout: transient.
            return SendResult.Retry;
        }
    }

    private void DeadLetter(OutboxMessage message)
    {
        // Event ID and type only (never the payload), at error level, plus the counter the alert rule uses.
        audit.Log(new AuditEntry(AuditActions.EventDeadLettered, AuditOutcome.Error)
        {
            EntityType = message.Type,
            EntityId = message.Id,
            ActingUserId = message.ActorUserId,
        });
        TaskifyMetrics.OutboxDeadLettered.Add(1, new KeyValuePair<string, object?>("type", message.Type));
        logger.LogError("Outbox event {EventId} of type {EventType} was rejected permanently and will not be retried", message.Id, message.Type);
    }

    private enum SendResult
    {
        Delivered,
        Permanent,
        Retry,
    }
}

/// <summary>Registration of the outbox dispatcher.</summary>
public static class OutboxServiceExtensions
{
    /// <summary>
    /// Registers the dispatcher and the HTTP client it uses to reach the Notifications API. The client always
    /// uses HTTPS (constitution Principle I; research R16) and presents this service's own API key.
    /// </summary>
    /// <typeparam name="TDbContext">The service's database context.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="notificationsBaseAddress">The Notifications API address. Must be <c>https</c>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">The address is not an HTTPS URI.</exception>
    public static IServiceCollection AddOutboxDispatcher<TDbContext>(
        this IServiceCollection services,
        string notificationsBaseAddress = "https://notifications-api")
        where TDbContext : DbContext
    {
        services.AddTaskifyHttpClient(OutboxDefaults.HttpClientName, notificationsBaseAddress);
        services.AddHostedService<OutboxDispatcher<TDbContext>>();
        return services;
    }
}
