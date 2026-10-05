using Taskify.Contracts;

namespace Taskify.Projects.Api.Domain;

/// <summary>
/// One of the five predefined users (spec FR-001). The set is fixed and read-only in phase 1: there is no
/// way to create, rename or delete a user (FR-003), so this type has no public setters.
/// </summary>
public sealed class User
{
    /// <summary>Creates a user. Used only to seed the database.</summary>
    /// <param name="id">The fixed ID (see <see cref="SeedIds"/>).</param>
    /// <param name="displayName">The name shown in the UI: 1–100 characters, unique.</param>
    /// <param name="role">The role label; exactly one <see cref="UserRole.ProductManager"/> and four <see cref="UserRole.Engineer"/>.</param>
    public User(Guid id, string displayName, UserRole role)
    {
        Id = id;
        DisplayName = displayName;
        Role = role;
    }

    /// <summary>Gets the fixed seed ID.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the display name (1–100 characters, unique).</summary>
    public string DisplayName { get; private set; }

    /// <summary>Gets the role label. It grants no extra rights in phase 1.</summary>
    public UserRole Role { get; private set; }
}
