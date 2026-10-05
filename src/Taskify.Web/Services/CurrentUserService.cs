using Taskify.Security.Audit;
using Taskify.Security.Users;

namespace Taskify.Web.Services;

/// <summary>
/// The predefined user this browser acts as (spec FR-002). The choice is stored in the protected cookie, written by
/// <c>POST /session/select</c> (see <see cref="SessionEndpoints"/>); this service reads it for the current circuit.
/// </summary>
/// <param name="identity">Gives the validated selection from the circuit's principal.</param>
/// <param name="users">The directory of predefined users.</param>
/// <param name="audit">Records restored selections.</param>
public sealed class CurrentUserService(CircuitIdentity identity, IUserDirectory users, IAuditLogger audit)
{
    private Task? initialization;

    /// <summary>Gets the selected user, or <see langword="null"/> when nobody is selected. Valid after <see cref="InitializeAsync"/>.</summary>
    public UserInfo? Current { get; private set; }

    /// <summary>
    /// Loads the selection for this circuit. Safe to call from every page; it runs once. When a selection is
    /// restored from the cookie it is audited as <c>UserSelectionRestored</c> (spec FR-032).
    /// </summary>
    /// <returns>A task that completes when <see cref="Current"/> is set.</returns>
    public Task InitializeAsync() => initialization ??= LoadAsync();

    private async Task LoadAsync()
    {
        var snapshot = await identity.GetAsync();
        if (snapshot.UserId is not { } userId)
        {
            return;
        }

        // The middleware already checked this ID; looking the user up gives the display name and role.
        Current = (await users.GetAllAsync()).FirstOrDefault(u => u.Id == userId);
        if (Current is not null)
        {
            audit.Log(new AuditEntry(AuditActions.UserSelectionRestored, AuditOutcome.Succeeded)
            {
                ActingUserId = Current.Id,
                SourceIp = snapshot.ClientIp,
            });
        }
    }
}
