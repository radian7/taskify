namespace Taskify.Web.Services;

/// <summary>
/// Gives the browser's source IP to code that runs inside a circuit (spec FR-032, research R8). A circuit has no
/// HTTP request of its own, so the address is read from the principal captured when the circuit connected, not from
/// <c>HttpContext</c> (which is empty in interactive components).
/// </summary>
/// <param name="identity">Gives the circuit's captured identity.</param>
public sealed class ClientIpCapture(CircuitIdentity identity)
{
    /// <summary>Gets the browser's source IP.</summary>
    /// <returns>The IP address text, or <see langword="null"/> when unknown.</returns>
    public async Task<string?> GetAsync() => (await identity.GetAsync()).ClientIp;
}
