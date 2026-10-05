using Taskify.Security.Users;

namespace Taskify.Web.Services.ApiClients;

/// <summary>Calls the Projects API (users and projects). More methods are added by the user stories that need them.</summary>
/// <param name="http">The HTTP client for the Projects API.</param>
/// <param name="identity">The identity of the circuit making the call.</param>
public sealed class ProjectsClient(HttpClient http, CircuitIdentity identity) : ApiClientBase(http, identity)
{
    /// <summary>Gets the five predefined users. This route needs no selected user.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The users, or the error.</returns>
    public Task<ApiResult<List<UserInfo>>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<UserInfo>>("/api/users", actingUser: false, cancellationToken);

    /// <summary>Gets all projects, newest first (spec FR-007).</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The projects, or the error.</returns>
    public Task<ApiResult<List<ProjectDto>>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<ProjectDto>>("/api/projects", cancellationToken: cancellationToken);

    /// <summary>Gets one project.</summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The project, or the error (404 when it does not exist).</returns>
    public Task<ApiResult<ProjectDto>> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        GetAsync<ProjectDto>($"/api/projects/{projectId:D}", cancellationToken: cancellationToken);

    /// <summary>Creates a project (spec FR-006). The API trims and validates the text; the form's limits are only a hint.</summary>
    /// <param name="name">The name (1–100 characters).</param>
    /// <param name="description">The description (up to 1,000 characters), or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new project, or the error (400 with field messages when the input is not accepted).</returns>
    public Task<ApiResult<ProjectDto>> CreateProjectAsync(string name, string? description, CancellationToken cancellationToken = default) =>
        SendAsync<ProjectDto>(HttpMethod.Post, "/api/projects", new CreateProjectBody(name, description), cancellationToken: cancellationToken);
}
