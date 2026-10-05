using System.Diagnostics.Metrics;
using Taskify.ServiceDefaults;

namespace Taskify.Security.Audit;

/// <summary>
/// The Taskify OpenTelemetry counters (research R13). Alert rules in <c>deploy/alerts</c> are built on them.
/// </summary>
public static class TaskifyMetrics
{
    private static readonly Meter Meter = new(Extensions.MeterName);

    /// <summary>
    /// Counts refused requests, tagged <c>reason</c> (an <see cref="AuditOutcome"/>), <c>caller</c> and
    /// <c>service</c>. A high rate from one source signals abuse (constitution Security Requirements).
    /// </summary>
    public static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        "taskify.rejections", unit: "{request}", description: "Requests refused by validation, authentication, authorization or rate limiting.");

    /// <summary>Counts outbox events the receiver rejected permanently. Each one is a missed update or notification.</summary>
    public static readonly Counter<long> OutboxDeadLettered = Meter.CreateCounter<long>(
        "taskify.outbox.deadlettered", unit: "{event}", description: "Outbox events dropped after a permanent rejection.");
}
