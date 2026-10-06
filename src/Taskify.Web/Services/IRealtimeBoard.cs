namespace Taskify.Web.Services;

/// <summary>
/// Change signals from the Notifications API hub (contracts/realtime-hub.md). A signal says that something changed;
/// it never carries data to render. The screen re-fetches from the REST API.
/// </summary>
public interface IRealtimeBoard
{
    /// <summary>Joins group <c>project:{id}</c>. The callback runs on a board change and after a resync.</summary>
    /// <param name="projectId">The project.</param>
    /// <param name="onSignal">Called without data; the caller re-fetches.</param>
    /// <returns>Dispose to leave the group (the last subscriber leaves it on the hub).</returns>
    IDisposable SubscribeProject(Guid projectId, Action onSignal);

    /// <summary>Joins group <c>task:{id}</c>. The callback runs on a task change and after a resync.</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="onSignal">Called without data; the caller re-fetches.</param>
    /// <returns>Dispose to leave the group.</returns>
    IDisposable SubscribeTask(Guid taskId, Action onSignal);

    /// <summary>Joins group <c>user:{id}</c>. The callback runs on a resync.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="onSignal">Called without data; the caller re-fetches.</param>
    /// <returns>Dispose to leave the group.</returns>
    IDisposable SubscribeUser(Guid userId, Action onSignal);

    /// <summary>Listens for new projects. Every connection is in the <c>projects</c> group already.</summary>
    /// <param name="onSignal">Called without data; the caller re-fetches the list.</param>
    /// <returns>Dispose to stop listening.</returns>
    IDisposable SubscribeProjectList(Action onSignal);
}
