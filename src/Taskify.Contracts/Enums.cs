namespace Taskify.Contracts;

/// <summary>
/// The four Kanban columns of a board, in display order (spec FR-008). Serialized as the member name
/// (<c>ToDo</c>, <c>InProgress</c>, <c>InReview</c>, <c>Done</c>); see <see cref="StrictEnumConverterFactory"/>.
/// </summary>
/// <remarks>
/// Consumers that import <c>System.Threading.Tasks</c> must alias this type
/// (<c>global using TaskStatus = Taskify.Contracts.TaskStatus;</c>) because the framework has a type with the same name.
/// </remarks>
public enum TaskStatus
{
    /// <summary>Work that has not started. New tasks start here (FR-009).</summary>
    ToDo = 0,

    /// <summary>Work in progress.</summary>
    InProgress = 1,

    /// <summary>Work waiting for review.</summary>
    InReview = 2,

    /// <summary>Finished work. Tasks in this column stay editable and open for comments (clarification Q5).</summary>
    Done = 3,
}

/// <summary>The role label of a predefined user. It grants no extra rights in phase 1 (spec Assumptions).</summary>
public enum UserRole
{
    /// <summary>Exactly one predefined user has this role.</summary>
    ProductManager = 0,

    /// <summary>Four predefined users have this role.</summary>
    Engineer = 1,
}

/// <summary>The kinds of in-app notification (spec FR-027).</summary>
public enum NotificationType
{
    /// <summary>Another user assigned a task to the recipient.</summary>
    TaskAssigned = 0,

    /// <summary>Another user moved a task assigned to the recipient.</summary>
    TaskMoved = 1,

    /// <summary>Another user commented on a task assigned to the recipient.</summary>
    TaskCommented = 2,
}
