using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using Taskify.Security.ApiKeys;
using Taskify.Security.Errors;

namespace Taskify.Security.Users;

/// <summary>
/// The user directory for services that do not own the users: it reads <c>GET /api/users</c> from the Projects API
/// and keeps the list in memory, because the five users never change in phase 1 (research R4). A failed fetch is
/// never cached, so the next call tries again.
/// </summary>
/// <param name="httpClients">Creates the client for the Projects API.</param>
public sealed class RemoteUserDirectory(IHttpClientFactory httpClients) : IUserDirectory, IDisposable
{
    /// <summary>The name of the HTTP client that talks to the Projects API.</summary>
    public const string HttpClientName = "taskify-projects-users";

    private readonly SemaphoreSlim loadLock = new(1, 1);
    private IReadOnlyList<UserInfo>? cache;

    /// <inheritdoc />
    public void Dispose() => loadLock.Dispose();

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken)).Any(u => u.Id == userId);

    /// <inheritdoc />
    /// <exception cref="ServiceUnavailableException">The Projects API cannot be reached or answered unexpectedly.</exception>
    public async Task<IReadOnlyList<UserInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            return cache;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (cache is not null)
            {
                return cache;
            }

            try
            {
                var client = httpClients.CreateClient(HttpClientName);
                var users = await client.GetFromJsonAsync<List<UserInfo>>("/api/users", ContractJson.Options, cancellationToken)
                    ?? throw new ServiceUnavailableException("The user directory returned no data.");

                if (users.Count == 0)
                {
                    throw new ServiceUnavailableException("The user directory is empty.");
                }

                cache = users;
                return cache;
            }
            catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException
                                              && !cancellationToken.IsCancellationRequested)
            {
                // Do not echo the underlying message (it may contain addresses); the exception type is enough.
                throw new ServiceUnavailableException("The user directory could not be loaded.", exception);
            }
        }
        finally
        {
            loadLock.Release();
        }
    }
}

/// <summary>Registration of <see cref="RemoteUserDirectory"/>.</summary>
public static class RemoteUserDirectoryExtensions
{
    /// <summary>
    /// Registers a user directory backed by the Projects API, over HTTPS, using this service's own API key.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="projectsBaseAddress">The Projects API address. Must be HTTPS.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRemoteUserDirectory(this IServiceCollection services, string projectsBaseAddress = "https://projects-api")
    {
        services.AddTaskifyHttpClient(RemoteUserDirectory.HttpClientName, projectsBaseAddress);
        services.AddSingleton<IUserDirectory, RemoteUserDirectory>();
        return services;
    }
}
