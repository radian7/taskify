namespace Taskify.Contracts;

/// <summary>
/// Fixed identifiers of the seeded data (spec FR-001, FR-005). Every service refers to the same users
/// and projects by these IDs, so no service needs another service's database (constitution Principle III).
/// </summary>
public static class SeedIds
{
    /// <summary>Maya Chen, the Product Manager.</summary>
    public static readonly Guid Maya = new("11111111-1111-1111-1111-000000000001");

    /// <summary>Liam Novak, Engineer.</summary>
    public static readonly Guid Liam = new("11111111-1111-1111-1111-000000000002");

    /// <summary>Priya Patel, Engineer.</summary>
    public static readonly Guid Priya = new("11111111-1111-1111-1111-000000000003");

    /// <summary>Tomasz Wiśniewski, Engineer.</summary>
    public static readonly Guid Tomasz = new("11111111-1111-1111-1111-000000000004");

    /// <summary>Jordan Lee, Engineer.</summary>
    public static readonly Guid Jordan = new("11111111-1111-1111-1111-000000000005");

    /// <summary>All five predefined users.</summary>
    public static readonly IReadOnlyList<Guid> AllUsers = [Maya, Liam, Priya, Tomasz, Jordan];

    /// <summary>Sample project "Mobile App Launch".</summary>
    public static readonly Guid MobileAppLaunch = new("22222222-2222-2222-2222-000000000001");

    /// <summary>Sample project "Website Redesign".</summary>
    public static readonly Guid WebsiteRedesign = new("22222222-2222-2222-2222-000000000002");

    /// <summary>Sample project "Internal Tools".</summary>
    public static readonly Guid InternalTools = new("22222222-2222-2222-2222-000000000003");

    /// <summary>All three sample projects.</summary>
    public static readonly IReadOnlyList<Guid> AllProjects = [MobileAppLaunch, WebsiteRedesign, InternalTools];
}
