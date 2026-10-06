using Microsoft.AspNetCore.SignalR;
using Taskify.Notifications.Api.Validation;

namespace Taskify.Notifications.Api.Hubs;

// STUB (T110-T115 test-first unit): compile-only placeholder. T120 implements it and maps it at /hubs/board.

/// <summary>
/// The real-time board hub. The Web server joins and leaves groups (<c>project:{id}</c>, <c>task:{id}</c>,
/// <c>user:{id}</c>); every argument is validated first (Principle II).
/// </summary>
/// <param name="validator">Validates hub arguments.</param>
public sealed class BoardHub(HubArgumentValidator validator) : Hub
{
    /// <summary>Adds the connection to <c>project:{projectId}</c>.</summary>
    /// <param name="projectId">The project.</param>
    /// <returns>A task that completes when the connection joined.</returns>
    public Task JoinProject(Guid projectId) => throw new NotImplementedException($"T120 {validator}");

    /// <summary>Removes the connection from <c>project:{projectId}</c>.</summary>
    /// <param name="projectId">The project.</param>
    /// <returns>A task that completes when the connection left.</returns>
    public Task LeaveProject(Guid projectId) => throw new NotImplementedException("T120");

    /// <summary>Adds the connection to <c>task:{taskId}</c>.</summary>
    /// <param name="taskId">The task.</param>
    /// <returns>A task that completes when the connection joined.</returns>
    public Task JoinTask(Guid taskId) => throw new NotImplementedException("T120");

    /// <summary>Removes the connection from <c>task:{taskId}</c>.</summary>
    /// <param name="taskId">The task.</param>
    /// <returns>A task that completes when the connection left.</returns>
    public Task LeaveTask(Guid taskId) => throw new NotImplementedException("T120");

    /// <summary>Adds the connection to <c>user:{userId}</c>; the ID must be a predefined user.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>A task that completes when the connection joined.</returns>
    public Task JoinUser(Guid userId) => throw new NotImplementedException("T120");

    /// <summary>Removes the connection from <c>user:{userId}</c>.</summary>
    /// <param name="userId">The user.</param>
    /// <returns>A task that completes when the connection left.</returns>
    public Task LeaveUser(Guid userId) => throw new NotImplementedException("T120");
}
