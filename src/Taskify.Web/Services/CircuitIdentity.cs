using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Taskify.Web.Services;

/// <summary>What the Web app knows about the browser behind a circuit.</summary>
/// <param name="UserId">The validated selected user, or <see langword="null"/> when nobody is selected.</param>
/// <param name="ClientIp">The browser's source IP, or <see langword="null"/> when unknown.</param>
public sealed record CircuitIdentitySnapshot(Guid? UserId, string? ClientIp);

/// <summary>
/// Reads the selected user and the source IP that <see cref="SelectedUserMiddleware"/> placed on the principal of
/// the request that opened this circuit. It is read once per circuit; changing the selection is a full page
/// navigation, which starts a new circuit.
/// </summary>
/// <param name="authenticationState">Gives the principal captured when the circuit connected.</param>
public sealed class CircuitIdentity(AuthenticationStateProvider authenticationState)
{
    private Task<CircuitIdentitySnapshot>? snapshot;

    /// <summary>Gets the identity, loading it on first use.</summary>
    /// <returns>The snapshot for this circuit.</returns>
    public Task<CircuitIdentitySnapshot> GetAsync() => snapshot ??= LoadAsync();

    private async Task<CircuitIdentitySnapshot> LoadAsync()
    {
        var state = await authenticationState.GetAuthenticationStateAsync();
        var principal = state.User;

        Guid? userId = Guid.TryParse(principal.FindFirstValue(TaskifyClaims.UserId), out var id) && id != Guid.Empty ? id : null;
        return new CircuitIdentitySnapshot(userId, principal.FindFirstValue(TaskifyClaims.ClientIp));
    }
}
