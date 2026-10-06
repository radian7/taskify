using Microsoft.AspNetCore.SignalR;
using Taskify.Security.Audit;
using Taskify.Security.Users;

namespace Taskify.Notifications.Api.Validation;

/// <summary>
/// Validates the arguments of hub method calls (constitution Principle II). A failure throws a
/// <c>HubException</c> with the generic message "Invalid request" (never the value) and audits the rejection.
/// </summary>
/// <param name="users">The directory of predefined users.</param>
/// <param name="audit">The audit log.</param>
public sealed class HubArgumentValidator(IUserDirectory users, IAuditLogger audit)
{
    private const string Message = "Invalid request";

    /// <summary>Checks a project or task ID: anything but <see cref="Guid.Empty"/> is accepted.</summary>
    /// <param name="id">The argument.</param>
    public void ValidateEntityId(Guid id)
    {
        if (id == Guid.Empty)
        {
            Reject();
        }
    }

    /// <summary>Checks a user ID: it must be one of the predefined users.</summary>
    /// <param name="userId">The argument.</param>
    /// <returns>A task that completes when the ID is valid.</returns>
    public async Task ValidateUserIdAsync(Guid userId)
    {
        if (userId == Guid.Empty || !await users.ExistsAsync(userId))
        {
            Reject();
        }
    }

    private void Reject()
    {
        // The audit entry has no field for the argument and the exception message is generic, so a rejected value
        // is never echoed to the client or written to the logs.
        audit.Log(new AuditEntry(AuditActions.HubRejected, AuditOutcome.Validation));
        throw new HubException(Message);
    }
}
