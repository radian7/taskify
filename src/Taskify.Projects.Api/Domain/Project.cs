namespace Taskify.Projects.Api.Domain;

/// <summary>
/// A container for related tasks (spec FR-006). Projects cannot be changed or deleted in phase 1 (spec Assumptions).
/// </summary>
/// <remarks>
/// "Chars" below means user-perceived characters (spec FR-019): each visible character, including an emoji, counts as one.
/// </remarks>
public sealed class Project
{
    /// <summary>Creates a project.</summary>
    /// <param name="id">The ID. "Generated".</param>
    /// <param name="name">The name. "Required; 1–100 chars after trim (FR-006); duplicates allowed".</param>
    /// <param name="description">The description. "0–1,000 chars after trim; empty string stored as null".</param>
    /// <param name="createdByUserId">The creator. "Must be a seeded user (FR-004)".</param>
    /// <param name="createdAt">When it was created. "Set by the server".</param>
    public Project(Guid id, string name, string? description, Guid createdByUserId, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the project ID.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the name (1–100 chars after trim). Duplicate names are allowed; the list tells them apart by creation date.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the description (0–1,000 chars after trim), or <see langword="null"/> when there is none.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the user who created the project.</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>Gets the creation time (UTC), set by the server.</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
