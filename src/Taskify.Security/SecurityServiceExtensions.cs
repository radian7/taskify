using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.Hosting;
using Taskify.Security.Json;
using Taskify.Security.RateLimiting;
using Taskify.Security.Users;

namespace Taskify.Security;

/// <summary>
/// One call to add (and one call to use) the security plumbing every Taskify API shares, so that every service
/// enforces the same rules in the same way (plan: Structure Decision).
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers API key options, auditing, strict JSON, Problem Details, the exception handler, the rate limiter and the
    /// request size limit. The service must also register an <see cref="IUserDirectory"/>.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static WebApplicationBuilder AddTaskifySecurity(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddOptions<ApiKeyOptions>().Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditLogger, AuditLogger>();
        services.AddSingleton<IClientIpAccessor, HttpClientIpAccessor>();
        services.AddScoped<ICurrentActingUser, HttpCurrentActingUser>();
        services.AddStrictJson();
        services.AddTaskifyProblemDetails();
        services.AddTaskifyRateLimiter();
        builder.ConfigureRequestLimits();

        return builder;
    }

    /// <summary>
    /// Adds the security middleware in the required order: exception handler, API key (key matrix), client IP,
    /// acting user, rate limiter. Call it before mapping endpoints.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseTaskifySecurity(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseMiddleware<ClientIpMiddleware>();
        app.UseMiddleware<ActingUserMiddleware>();
        app.UseRateLimiter();

        return app;
    }
}
