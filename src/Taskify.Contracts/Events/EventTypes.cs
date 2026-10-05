namespace Taskify.Contracts.Events;

/// <summary>The event type names (contracts/events.asyncapi.yaml, schema <c>EnvelopeBase.type</c>).</summary>
public static class EventTypes
{
    /// <summary>A project was created. Published by the Projects API.</summary>
    public const string ProjectCreated = nameof(ProjectCreated);

    /// <summary>A task was created. Published by the Tasks API.</summary>
    public const string TaskCreated = nameof(TaskCreated);

    /// <summary>A task's title or description changed.</summary>
    public const string TaskUpdated = nameof(TaskUpdated);

    /// <summary>A task's assignee changed.</summary>
    public const string TaskAssigned = nameof(TaskAssigned);

    /// <summary>A task moved to another column.</summary>
    public const string TaskMoved = nameof(TaskMoved);

    /// <summary>A comment was added.</summary>
    public const string CommentAdded = nameof(CommentAdded);

    /// <summary>A comment was edited.</summary>
    public const string CommentEdited = nameof(CommentEdited);

    /// <summary>A comment was deleted.</summary>
    public const string CommentDeleted = nameof(CommentDeleted);

    /// <summary>Every event type, for allow-list validation.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ProjectCreated, TaskCreated, TaskUpdated, TaskAssigned, TaskMoved, CommentAdded, CommentEdited, CommentDeleted,
    };

    /// <summary>The only event type the Projects API may publish.</summary>
    public static readonly IReadOnlySet<string> PublishedByProjects =
        new HashSet<string>(StringComparer.Ordinal) { ProjectCreated };

    /// <summary>The event types the Tasks API may publish (everything except <see cref="ProjectCreated"/>).</summary>
    public static readonly IReadOnlySet<string> PublishedByTasks = new HashSet<string>(StringComparer.Ordinal)
    {
        TaskCreated, TaskUpdated, TaskAssigned, TaskMoved, CommentAdded, CommentEdited, CommentDeleted,
    };
}
