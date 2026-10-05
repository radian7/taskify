using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Taskify.Security.Hosting;

/// <summary>Request size limits (research R7: a coarse guard in addition to the per-field limits).</summary>
public static class RequestLimitsSetup
{
    /// <summary>The largest request body any API accepts: 1 MB. Larger bodies get 413 and are audited.</summary>
    public const long MaxRequestBodyBytes = 1_048_576;

    /// <summary>Applies <see cref="MaxRequestBodyBytes"/> to the web server.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static WebApplicationBuilder ConfigureRequestLimits(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
        return builder;
    }
}
