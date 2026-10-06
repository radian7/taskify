using Taskify.Contracts.Hub;

namespace Taskify.Web.Services.ApiClients;

/// <summary>Calls the Notifications API (spec FR-027 to FR-030).</summary>
/// <param name="http">The HTTP client for the Notifications API.</param>
/// <param name="identity">The identity of the circuit making the call.</param>
public sealed class NotificationsClient(HttpClient http, CircuitIdentity identity) : ApiClientBase(http, identity)
{
    /// <summary>Gets how many notifications the acting user has not read (<c>GET /api/notifications/unread-count</c>).</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The count, or the error.</returns>
    public Task<ApiResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("T137");

    /// <summary>Gets the acting user's notifications, newest first (<c>GET /api/notifications?limit={limit}</c>).</summary>
    /// <param name="limit">How many to fetch (1 to 100).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The notifications, or the error.</returns>
    public Task<ApiResult<List<NotificationDto>>> ListAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("T137");

    /// <summary>Marks one notification read (<c>POST /api/notifications/{id}/read</c>).</summary>
    /// <param name="notificationId">The notification.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the error.</returns>
    public Task<ApiResult> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("T137");

    /// <summary>Marks all of the acting user's notifications read (<c>POST /api/notifications/read-all</c>).</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the error.</returns>
    public Task<ApiResult> MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("T137");
}

/// <summary>The answer of <c>GET /api/notifications/unread-count</c>.</summary>
/// <param name="Count">How many notifications are unread.</param>
public sealed record UnreadCountDto(int Count);
