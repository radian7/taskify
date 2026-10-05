using Taskify.Contracts;

namespace Taskify.Security.Users;

/// <summary>One of the five predefined users (spec FR-001).</summary>
/// <param name="Id">The fixed user ID (see <see cref="SeedIds"/>).</param>
/// <param name="DisplayName">The name shown in the UI (1–100 characters).</param>
/// <param name="Role">The role label; it grants no extra rights in phase 1.</param>
public sealed record UserInfo(Guid Id, string DisplayName, UserRole Role);

/// <summary>
/// The directory of predefined users, owned by the Projects API. Other services read it through its API
/// and cache it, because the five users never change in phase 1 (research R4).
/// </summary>
public interface IUserDirectory
{
    /// <summary>Checks whether a user ID belongs to one of the predefined users.</summary>
    /// <param name="userId">The ID to check.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns><see langword="true"/> when the user exists.</returns>
    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Gets all predefined users.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The five users.</returns>
    Task<IReadOnlyList<UserInfo>> GetAllAsync(CancellationToken cancellationToken = default);
}
