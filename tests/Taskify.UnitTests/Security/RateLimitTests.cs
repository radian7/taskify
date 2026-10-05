using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.RateLimiting;

namespace Taskify.UnitTests.Security;

/// <summary>
/// Tests for the limits in spec FR-031 and SC-010.
/// <para>
/// The framework's <see cref="SlidingWindowRateLimiter"/> has no injectable clock, so the two time-based tests
/// run a scaled-down copy of the production limiter (1.2-second window, same six segments, same algorithm).
/// The real one-minute behaviour at one write per second is covered by the integration tests (T148).
/// </para>
/// </summary>
public class RateLimitTests
{
    private static readonly TimeSpan ScaledWindow = TimeSpan.FromMilliseconds(1200);

    private static SlidingWindowRateLimiter ProductionLimiter(int permitLimit) =>
        new(RateLimitPolicies.CreateOptions(permitLimit, autoReplenishment: false));

    private static SlidingWindowRateLimiter ScaledLimiter(int permitLimit)
    {
        var options = RateLimitPolicies.CreateOptions(permitLimit);
        options.Window = ScaledWindow;
        return new SlidingWindowRateLimiter(options);
    }

    [Fact]
    public void The_defaults_match_the_spec()
    {
        Assert.Equal(60, RateLimitPolicies.WritesPerMinute);
        Assert.Equal(300, RateLimitPolicies.ReadsPerMinute);
        Assert.Equal(3000, RateLimitPolicies.InternalEventsPerMinute);
        Assert.Equal(1200, RateLimitPolicies.WebIpPerMinute);
    }

    [Fact]
    public void The_options_use_a_one_minute_window_in_six_segments_without_queueing()
    {
        var options = RateLimitPolicies.CreateOptions(60);

        Assert.Equal(TimeSpan.FromMinutes(1), options.Window);
        Assert.Equal(6, options.SegmentsPerWindow);
        Assert.Equal(0, options.QueueLimit);
        Assert.Equal(60, options.PermitLimit);
    }

    [Fact]
    public void The_61st_write_in_a_minute_is_rejected()
    {
        using var limiter = ProductionLimiter(RateLimitPolicies.WritesPerMinute);

        for (var i = 1; i <= 60; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired, $"write {i} should pass");
        }

        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void The_301st_read_in_a_minute_is_rejected()
    {
        using var limiter = ProductionLimiter(RateLimitPolicies.ReadsPerMinute);

        for (var i = 1; i <= 300; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired, $"read {i} should pass");
        }

        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void Each_partition_has_its_own_budget()
    {
        using var first = ProductionLimiter(60);
        using var second = ProductionLimiter(60);
        for (var i = 0; i < 60; i++)
        {
            first.AttemptAcquire();
        }

        Assert.False(first.AttemptAcquire().IsAcquired);
        Assert.True(second.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public async Task A_steady_rate_below_the_limit_is_never_rejected()
    {
        // Scaled from "one write per second against 60 per minute": 12 permits per 1.2 s, one request every
        // 120 ms (8.3 per second, about 10 per window). Sleeping can only make the pace slower, never faster.
        using var limiter = ScaledLimiter(permitLimit: 12);

        for (var request = 0; request < 30; request++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired, $"request {request} should pass");
            await Task.Delay(120, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Permits_come_back_once_the_window_has_moved_on()
    {
        using var limiter = ScaledLimiter(permitLimit: 12);
        for (var i = 0; i < 12; i++)
        {
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        }

        Assert.False(limiter.AttemptAcquire().IsAcquired);

        await Task.Delay(ScaledWindow + TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);

        Assert.True(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void The_partition_key_prefers_the_acting_user_then_the_caller_then_the_ip()
    {
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.7");
        Assert.Equal("10.0.0.7", RateLimitPolicies.PartitionKey(context));

        context.Items["Taskify.ClientIp"] = "203.0.113.9";
        Assert.Equal("203.0.113.9", RateLimitPolicies.PartitionKey(context));

        context.Items["Taskify.Caller"] = Callers.Tasks;
        Assert.Equal(Callers.Tasks, RateLimitPolicies.PartitionKey(context));

        context.Items["Taskify.ActingUser"] = userId;
        Assert.Equal(userId.ToString(), RateLimitPolicies.PartitionKey(context));
    }

    [Fact]
    public void The_partition_key_falls_back_to_anonymous_when_nothing_is_known() =>
        Assert.Equal("anonymous", RateLimitPolicies.PartitionKey(new DefaultHttpContext()));
}
