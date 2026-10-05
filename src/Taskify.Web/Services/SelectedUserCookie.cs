using Microsoft.AspNetCore.DataProtection;

namespace Taskify.Web.Services;

/// <summary>
/// The cookie that remembers which predefined user a browser acts as, so the choice survives a page refresh
/// (research R8). Phase 1 has no login: this is a convenience, not proof of identity, and the plan records that as
/// accepted risk D1. The value is still protected, because a tamperable cookie would let anyone forge a user ID.
/// </summary>
/// <remarks>
/// The cookie is <c>HttpOnly</c> (scripts cannot read it), <c>Secure</c> (HTTPS only) and <c>SameSite=Strict</c>
/// (never sent on cross-site requests). Its value is encrypted and signed with ASP.NET Core Data Protection, whose key ring is
/// itself encrypted at rest with the <c>dataprotection-cert</c> secret, and it expires on the server after
/// <see cref="Lifetime"/> no matter what the browser does.
/// </remarks>
public sealed class SelectedUserCookie
{
    /// <summary>The cookie name.</summary>
    public const string CookieName = "taskify.user";

    /// <summary>How long a selection stays valid.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly ITimeLimitedDataProtector protector;

    /// <summary>Creates the helper.</summary>
    /// <param name="provider">Provides the Data Protection key ring.</param>
    public SelectedUserCookie(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        // The purpose string isolates this cookie from every other use of the key ring; bump the suffix to
        // invalidate all existing cookies.
        protector = provider.CreateProtector("Taskify.Web.SelectedUser.v1").ToTimeLimitedDataProtector();
    }

    /// <summary>Gets the options every response that sets or deletes the cookie must use.</summary>
    public static CookieOptions Options => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/",
        MaxAge = Lifetime,
    };

    /// <summary>Creates the protected cookie value for a user.</summary>
    /// <param name="userId">The chosen user.</param>
    /// <returns>An opaque, tamper-proof value.</returns>
    public string Protect(Guid userId) => protector.Protect(userId.ToString("D"), Lifetime);

    /// <summary>
    /// Reads a cookie value. Anything that is not a value this application issued and that is still valid is rejected:
    /// tampered, truncated, expired, from another key ring, or not a GUID.
    /// </summary>
    /// <param name="value">The raw cookie value.</param>
    /// <returns>The user ID, or <see langword="null"/> when the value is not acceptable.</returns>
    public Guid? TryUnprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var text = protector.Unprotect(value);
            return Guid.TryParseExact(text, "D", out var userId) && userId != Guid.Empty ? userId : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Tampered, expired or issued under another key. The caller audits and deletes the cookie.
            return null;
        }
    }
}
