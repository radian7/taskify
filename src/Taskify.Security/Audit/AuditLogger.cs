using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Taskify.Security.Audit;

/// <summary>Writes audit records (spec FR-022, FR-032; research R13).</summary>
public interface IAuditLogger
{
    /// <summary>
    /// Writes one audit record. Fields the entry leaves empty (caller, source IP, acting user) are filled
    /// from the current HTTP request when there is one.
    /// </summary>
    /// <param name="entry">The record to write.</param>
    void Log(AuditEntry entry);
}

/// <summary>
/// The structured-log implementation of <see cref="IAuditLogger"/>. Every refusal also increments the
/// <c>taskify.rejections</c> counter.
/// </summary>
/// <param name="logger">The log sink.</param>
/// <param name="httpContextAccessor">Gives the current request, if any, to fill in missing fields.</param>
/// <param name="environment">Provides the service name for the metric tags.</param>
public sealed class AuditLogger(
    ILogger<AuditLogger> logger,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment) : IAuditLogger
{
    /// <inheritdoc />
    public void Log(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var context = httpContextAccessor.HttpContext;
        var caller = entry.CallerService ?? context?.GetCaller();
        var sourceIp = entry.SourceIp ?? context?.GetClientIp() ?? context?.Connection.RemoteIpAddress?.ToString();
        var actingUser = entry.ActingUserId ?? context?.GetActingUserId();
        var correlationId = Activity.Current?.TraceId.ToString() ?? context?.TraceIdentifier;

        var level = entry.Outcome switch
        {
            AuditOutcome.Succeeded => LogLevel.Information,
            AuditOutcome.Error => LogLevel.Error,
            _ when entry.Action == AuditActions.EventDeadLettered => LogLevel.Error,
            _ => LogLevel.Warning,
        };

        // Security: only identifiers, never titles, names, descriptions, comment text, keys or cookies.
        logger.Log(
            level,
            "Audit {AuditAction} outcome={AuditOutcome} user={ActingUserId} previousUser={PreviousUserId} " +
            "entityType={EntityType} entityId={EntityId} caller={CallerService} sourceIp={SourceIp} correlationId={CorrelationId}",
            entry.Action,
            entry.Outcome,
            actingUser,
            entry.PreviousUserId,
            entry.EntityType,
            entry.EntityId,
            caller,
            sourceIp,
            correlationId);

        if (entry.Outcome != AuditOutcome.Succeeded)
        {
            TaskifyMetrics.Rejections.Add(
                1,
                new KeyValuePair<string, object?>("reason", entry.Outcome.ToString()),
                new KeyValuePair<string, object?>("caller", caller ?? "none"),
                new KeyValuePair<string, object?>("service", environment.ApplicationName));
        }
    }
}

/// <summary>Convenience overloads for the most common audit calls.</summary>
public static class AuditLoggerExtensions
{
    /// <summary>Records a refused request.</summary>
    /// <param name="audit">The audit logger.</param>
    /// <param name="outcome">Why the request was refused.</param>
    /// <param name="entityType">The kind of entity involved, if any.</param>
    /// <param name="entityId">The ID of the entity involved, if any.</param>
    public static void Rejected(this IAuditLogger audit, AuditOutcome outcome, string? entityType = null, Guid? entityId = null) =>
        audit.Log(new AuditEntry(AuditActions.RequestRejected, outcome) { EntityType = entityType, EntityId = entityId });

    /// <summary>Records a successful data change.</summary>
    /// <param name="audit">The audit logger.</param>
    /// <param name="action">One of <see cref="AuditActions"/>.</param>
    /// <param name="entityType">The kind of entity changed.</param>
    /// <param name="entityId">The ID of the entity changed.</param>
    public static void Changed(this IAuditLogger audit, string action, string entityType, Guid entityId) =>
        audit.Log(new AuditEntry(action, AuditOutcome.Succeeded) { EntityType = entityType, EntityId = entityId });
}
