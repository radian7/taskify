using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Security.Audit;
using Taskify.Security.Errors;

namespace Taskify.Security.RateLimiting;

/// <summary>
/// The rate limits (spec FR-031, research R9). Limits are enforced by the API that owns the data, so they
/// hold even if the Web app is bypassed. They are per instance, which is correct because phase 1 runs one
/// instance per service; a scale-out needs a shared store.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Policy for every POST, PUT and DELETE under <c>/api</c>: 60 per minute per acting user.</summary>
    public const string Writes = "writes";

    /// <summary>Policy for every GET under <c>/api</c>: 300 per minute per acting user (or per calling service when there is none).</summary>
    public const string Reads = "reads";

    /// <summary>Policy for <c>/internal/events</c>: 3,000 per minute per calling service.</summary>
    public const string InternalEvents = "internal-events";

    /// <summary>Outer guard on the Web app: 1,200 requests per minute per client IP.</summary>
    public const string WebIp = "web-ip";

    /// <summary>Changes allowed per user per minute (FR-031).</summary>
    public const int WritesPerMinute = 60;

    /// <summary>Reads allowed per user per minute (FR-031).</summary>
    public const int ReadsPerMinute = 300;

    /// <summary>Events accepted per calling service per minute.</summary>
    public const int InternalEventsPerMinute = 3000;

    /// <summary>Web requests allowed per client IP per minute.</summary>
    public const int WebIpPerMinute = 1200;

    private const int SegmentsPerWindow = 6;

    /// <summary>Creates the sliding-window settings: a one-minute window in six segments, no queueing.</summary>
    /// <param name="permitLimit">The number of requests allowed in the window.</param>
    /// <param name="autoReplenishment"><see langword="false"/> lets tests advance the window by hand.</param>
    /// <returns>The limiter options.</returns>
    public static SlidingWindowRateLimiterOptions CreateOptions(int permitLimit, bool autoReplenishment = true) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = SegmentsPerWindow,
        QueueLimit = 0,
        AutoReplenishment = autoReplenishment,
    };

    /// <summary>Registers the API policies (<see cref="Writes"/>, <see cref="Reads"/>, <see cref="InternalEvents"/>).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTaskifyRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            ConfigureRejection(options);
            options.AddPolicy(Writes, context => Partition(context, WritesPerMinute));
            options.AddPolicy(Reads, context => Partition(context, ReadsPerMinute));
            options.AddPolicy(InternalEvents, context => Partition(context, InternalEventsPerMinute));
        });

    /// <summary>Registers the Web app's outer per-IP limit as the global limiter.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTaskifyWebRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            ConfigureRejection(options);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => CreateOptions(WebIpPerMinute)));
        });

    /// <summary>Applies the <see cref="Writes"/> policy to the endpoint.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder RequireWrites<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireRateLimiting(Writes);

    /// <summary>Applies the <see cref="Reads"/> policy to the endpoint.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder RequireReads<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireRateLimiting(Reads);

    /// <summary>Applies the <see cref="InternalEvents"/> policy to the endpoint.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder RequireInternalEventLimit<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireRateLimiting(InternalEvents);

    /// <summary>
    /// Works out who a request counts against: the acting user, else the calling service, else the client IP.
    /// Limits are therefore per person, not per shared Web-app key.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The partition key.</returns>
    public static string PartitionKey(HttpContext context) =>
        context.GetActingUserId()?.ToString()
        ?? context.GetCaller()
        ?? context.GetClientIp()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";

    private static RateLimitPartition<string> Partition(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context), _ => CreateOptions(permitLimit));

    private static void ConfigureRejection(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, _) =>
        {
            var http = context.HttpContext;

            var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                : 60;
            http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            http.RequestServices.GetRequiredService<IAuditLogger>().Rejected(AuditOutcome.RateLimited);

            // Nothing was changed: the limiter runs before the endpoint handler.
            await ProblemResponses.WriteAsync(
                http,
                StatusCodes.Status429TooManyRequests,
                "Too many requests, please wait a moment");
        };
    }
}
