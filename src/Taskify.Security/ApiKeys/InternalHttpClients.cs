using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Taskify.Security.ApiKeys;

/// <summary>
/// Creates the HTTP clients one Taskify service uses to call another. Every such client uses HTTPS only
/// (constitution Principle I; research R16) and presents this service's own API key (research R8, deviation D2).
/// </summary>
public static class InternalHttpClients
{
    /// <summary>The default time a call to another service may take before it fails and is retried.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Registers a named client for another Taskify service.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The client name.</param>
    /// <param name="baseAddress">The service address, for example <c>https://projects-api</c>. Must be HTTPS.</param>
    /// <returns>The client builder, so callers can add message handlers.</returns>
    /// <exception cref="ArgumentException">The address is not an HTTPS URI.</exception>
    public static IHttpClientBuilder AddTaskifyHttpClient(this IServiceCollection services, string name, string baseAddress)
    {
        var address = RequireHttps(baseAddress);
        return services.AddHttpClient(name, (provider, client) => Configure(provider, client, address));
    }

    /// <summary>Registers a typed client for another Taskify service.</summary>
    /// <typeparam name="TClient">The typed client class; it receives the configured <see cref="HttpClient"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">The service address, for example <c>https://tasks-api</c>. Must be HTTPS.</param>
    /// <returns>The client builder, so callers can add message handlers.</returns>
    /// <exception cref="ArgumentException">The address is not an HTTPS URI.</exception>
    public static IHttpClientBuilder AddTaskifyHttpClient<TClient>(this IServiceCollection services, string baseAddress)
        where TClient : class
    {
        var address = RequireHttps(baseAddress);
        return services.AddHttpClient<TClient>((provider, client) => Configure(provider, client, address));
    }

    private static Uri RequireHttps(string baseAddress)
    {
        var address = new Uri(baseAddress);
        if (address.Scheme != Uri.UriSchemeHttps)
        {
            // Aspire's "https+http://" form would silently fall back to plain HTTP; it is never used.
            throw new ArgumentException("Service-to-service traffic must use HTTPS (research R16).", nameof(baseAddress));
        }

        return address;
    }

    private static void Configure(IServiceProvider provider, HttpClient client, Uri address)
    {
        client.BaseAddress = address;
        client.Timeout = DefaultTimeout;

        var key = provider.GetRequiredService<IOptions<ApiKeyOptions>>().Value.OwnKey;
        if (!string.IsNullOrEmpty(key))
        {
            client.DefaultRequestHeaders.Add(TaskifyHeaders.ApiKey, key);
        }
    }
}
