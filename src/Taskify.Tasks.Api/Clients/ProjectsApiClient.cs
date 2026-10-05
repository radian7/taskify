using Taskify.Security;
using Taskify.Security.Errors;

namespace Taskify.Tasks.Api.Clients;

/// <summary>
/// Asks the Projects API whether a project exists, so a task is never created in a project that does not
/// (data-model.md "Cross-service references"). The Tasks service never reads the Projects database
/// (constitution Principle III). The user directory is a separate client: see <c>RemoteUserDirectory</c>.
/// </summary>
/// <param name="httpClients">Creates the HTTPS client that carries this service's API key.</param>
/// <param name="httpContextAccessor">Gives the current request, whose acting user is forwarded.</param>
public sealed class ProjectsApiClient(IHttpClientFactory httpClients, IHttpContextAccessor httpContextAccessor)
{
    /// <summary>The name of the HTTP client that talks to the Projects API.</summary>
    public const string HttpClientName = "taskify-projects";

    /// <summary>Checks that a project exists.</summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when it exists, <see langword="false"/> when the Projects API says it does not.</returns>
    /// <exception cref="ServiceUnavailableException">The Projects API cannot be reached or answered unexpectedly.</exception>
    public async Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{projectId:D}");

        // The Projects API requires an acting user on this route; pass on the one from the current request.
        if (httpContextAccessor.HttpContext?.GetActingUserId() is { } actingUserId)
        {
            request.Headers.Add(TaskifyHeaders.ActingUser, actingUserId.ToString());
        }

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => true,
                System.Net.HttpStatusCode.NotFound => false,
                _ => throw new ServiceUnavailableException("The Projects API answered with an unexpected status."),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException("The Projects API could not be reached.", exception);
        }
    }
}
