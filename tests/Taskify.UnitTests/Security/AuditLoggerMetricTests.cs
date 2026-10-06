using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Taskify.Security.Audit;

namespace Taskify.UnitTests.Security;

/// <summary>
/// The <c>taskify.rejections</c> counter (research R13): every refusal increments it, a successful change does not.
/// The integration tests cannot read another process's metrics, so the counter is checked here (spec FR-022, T148).
/// </summary>
public sealed class AuditLoggerMetricTests
{
    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "tasks-api";

        public string ContentRootPath { get; set; } = "/";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static List<string> Capture(string caller, Action act)
    {
        // Other tests in the run may also reject requests, so count only measurements tagged with this test's caller.
        var reasons = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "taskify.rejections")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? reason = null;
            var mine = false;
            foreach (var tag in tags)
            {
                reason = tag.Key == "reason" ? (string?)tag.Value : reason;
                mine |= tag.Key == "caller" && (string?)tag.Value == caller;
            }

            if (mine)
            {
                lock (reasons)
                {
                    reasons.Add(reason!);
                }
            }
        });
        listener.Start();
        act();
        return reasons;
    }

    [Theory]
    [InlineData(AuditOutcome.Validation)]
    [InlineData(AuditOutcome.Unauthorized)]
    [InlineData(AuditOutcome.Forbidden)]
    [InlineData(AuditOutcome.Conflict)]
    [InlineData(AuditOutcome.TooLarge)]
    [InlineData(AuditOutcome.UnknownReference)]
    [InlineData(AuditOutcome.RateLimited)]
    public void A_rejection_increments_the_counter_with_its_reason(AuditOutcome outcome)
    {
        var audit = new AuditLogger(NullLogger<AuditLogger>.Instance, new HttpContextAccessor(), new Host());

        var reasons = Capture("metric-test-rejected", () => audit.Log(new AuditEntry(AuditActions.RequestRejected, outcome) { CallerService = "metric-test-rejected" }));

        Assert.Contains(outcome.ToString(), reasons);
    }

    [Fact]
    public void A_successful_change_does_not_increment_the_counter()
    {
        var audit = new AuditLogger(NullLogger<AuditLogger>.Instance, new HttpContextAccessor(), new Host());

        var reasons = Capture("metric-test-success", () => audit.Log(new AuditEntry(AuditActions.TaskCreated, AuditOutcome.Succeeded) { CallerService = "metric-test-success" }));

        Assert.Empty(reasons);
    }
}
