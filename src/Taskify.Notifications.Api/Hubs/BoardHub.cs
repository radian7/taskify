using Microsoft.AspNetCore.SignalR;
using Taskify.Notifications.Api.Validation;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;

namespace Taskify.Notifications.Api.Hubs;

/// <summary>
/// The real-time board hub. The Web server joins and leaves groups (<c>project:{id}</c>, <c>task:{id}</c>,
/// <c>user:{id}</c>); every argument is validated first (Principle II).
/// </summary>
/// <param name="validator">Validates hub arguments.</param>
public sealed class BoardHub(HubArgumentValidator validator) : Hub
{
    /// <summary>The group every connection joins to hear about new projects.</summary>
    public const string ProjectsGroup = "projects";

    /// <summary>Gets the group name for a project.</summary>
    /// <param name="projectId">The project.</param>
    /// <returns>The group name.</returns>
    public static string ProjectGroup(Guid projectId) => $"project:{projectId}";

    /// <summary>Gets the group name for a task.</summary>
    /// <param name="taskId">The task.</param>
    /// <returns>The group name.</returns>
    public static string TaskGroup(Guid taskId) => $"task:{taskId}";

    /// <summary>Gets the group name for a user.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>The group name.</returns>
    public static string UserGroup(Guid userId) => $"user:{userId}";

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        // Defence in depth: the route already requires the Web key; only the Web server may hold a hub connection (R8).
        var http = Context.GetHttpContext();
        if (http?.GetCaller() != Callers.Web)
        {
            http?.RequestServices.GetService<IAuditLogger>()?.Log(new AuditEntry(AuditActions.HubRejected, AuditOutcome.Forbidden));
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectsGroup);
        await base.OnConnectedAsync();
    }

    /// <summary>Adds the connection to <c>project:{projectId}</c>.</summary>
    /// <param name="projectId">The project.</param>
    /// <returns>A task that completes when the connection joined.</returns>
    public Task JoinProject(Guid projectId)
    {
        validator.ValidateEntityId(projectId);
        return Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
    }

    /// <summary>Removes the connection from <c>project:{projectId}</c>.</summary>
    /// <param name="projectId">The project.</param>
    /// <returns>A task that completes when the connection left.</returns>
    public Task LeaveProject(Guid projectId)
    {
        validator.ValidateEntityId(projectId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
    }

    /// <summary>Adds the connection to <c>task:{taskId}</c>.</summary>
    /// <param name="taskId">The task.</param>
    /// <returns>A task that completes when the connection joined.</returns>
    public Task JoinTask(Guid taskId)
    {
        validator.ValidateEntityId(taskId);
        return Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));
    }

    /// <summary>Removes the connection from <c>task:{taskId}</c>.</summary>
    /// <param name="taskId">The task.</param>
    /// <returns>A task that completes when the connection left.</returns>
    public Task LeaveTask(Guid taskId)
    {
        validator.ValidateEntityId(taskId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));
    }

    /// <summary>Adds the connection to <c>user:{userId}</c>; the ID must be a predefined user.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>A task that completes when the connection joined.</returns>
    public async Task JoinUser(Guid userId)
    {
        await validator.ValidateUserIdAsync(userId);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
    }

    /// <summary>Removes the connection from <c>user:{userId}</c>.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>A task that completes when the connection left.</returns>
    public async Task LeaveUser(Guid userId)
    {
        await validator.ValidateUserIdAsync(userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId));
    }
}
