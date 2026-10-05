namespace Taskify.Tasks.Api.Domain;

/// <summary>
/// A unit of work in one project (spec FR-009 to FR-012). There is no way to delete a task in phase 1; finished
/// work moves to <see cref="TaskStatus.Done"/>. There is no concurrency token: when two edits or moves arrive at
/// nearly the same time, the last save wins (spec FR-011, research R14).
/// </summary>
/// <remarks>
/// "Chars" below means user-perceived characters (spec FR-019): each visible character, including an emoji, counts as one.
/// </remarks>
public sealed class TaskItem
{
    /// <summary>Creates a task. New tasks start in <see cref="TaskStatus.ToDo"/>; seed data may use any column.</summary>
    /// <param name="id">The ID. "Generated".</param>
    /// <param name="projectId">The project. "Must exist in the Projects API (checked on create)".</param>
    /// <param name="title">The title. "Required; 1–200 chars after trim (FR-009)".</param>
    /// <param name="description">The description. "0–5,000 chars after trim".</param>
    /// <param name="status">The column. "New tasks start as <c>ToDo</c>".</param>
    /// <param name="assigneeUserId">The assignee. "Null = unassigned; otherwise a seeded user (FR-010)".</param>
    /// <param name="createdByUserId">The creator. "Acting user".</param>
    /// <param name="createdAt">When it was created. "Set by the server".</param>
    public TaskItem(
        Guid id,
        Guid projectId,
        string title,
        string? description,
        TaskStatus status,
        Guid? assigneeUserId,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        Description = description;
        Status = status;
        AssigneeUserId = assigneeUserId;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Gets the task ID.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the project this task belongs to. It lives in the Projects service, so there is no foreign key.</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>Gets the title (1–200 chars after trim).</summary>
    public string Title { get; private set; }

    /// <summary>Gets the description (0–5,000 chars after trim), or <see langword="null"/>.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the column the task is in.</summary>
    public TaskStatus Status { get; private set; }

    /// <summary>Gets the assignee, or <see langword="null"/> when the task is unassigned.</summary>
    public Guid? AssigneeUserId { get; private set; }

    /// <summary>Gets the user who created the task.</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>Gets the creation time (UTC), set by the server.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the time of the last change, including moves (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Moves the task to another column (spec FR-012: any user, any column to any other, including backwards).
    /// A move to the column the task is already in changes nothing and records nothing (User Story 2, scenario 4).
    /// </summary>
    /// <param name="to">The column to move to. It must be one of the four defined columns.</param>
    /// <param name="actingUserId">The user who moves the task, recorded in the history.</param>
    /// <param name="now">The time of the move (UTC).</param>
    /// <returns>The history entry for the move, or <see langword="null"/> when the task was already in that column.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> is not a defined column.</exception>
    public StatusChange? MoveTo(TaskStatus to, Guid actingUserId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(to))
        {
            throw new ArgumentOutOfRangeException(nameof(to), "Not a column.");
        }

        if (to == Status)
        {
            return null;
        }

        var change = new StatusChange(Guid.CreateVersion7(), Id, Status, to, actingUserId, now);
        Status = to;
        UpdatedAt = now;
        return change;
    }

    /// <summary>
    /// Replaces the title, description and assignee together (spec FR-011: any user may edit any task, and may assign,
    /// reassign or unassign it). Tasks in the Done column are edited exactly like any other (clarification Q5).
    /// Nothing is recorded, and <see cref="UpdatedAt"/> is left alone, when the new values equal the current ones.
    /// </summary>
    /// <param name="title">The new title. Already trimmed and validated (1–200 chars).</param>
    /// <param name="description">The new description, already trimmed (0–5,000 chars), or <see langword="null"/>.</param>
    /// <param name="assigneeUserId">The new assignee, or <see langword="null"/> to unassign.</param>
    /// <param name="now">The time of the edit (UTC).</param>
    /// <returns>What changed, so the caller can raise the right events.</returns>
    public TaskUpdate Update(string title, string? description, Guid? assigneeUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var textChanged = !string.Equals(Title, title, StringComparison.Ordinal) || !string.Equals(Description, description, StringComparison.Ordinal);
        var assigneeChanged = AssigneeUserId != assigneeUserId;
        if (!textChanged && !assigneeChanged)
        {
            return new TaskUpdate(TextChanged: false, AssigneeChanged: false, AssigneeUserId);
        }

        var previousAssignee = AssigneeUserId;
        Title = title;
        Description = description;
        AssigneeUserId = assigneeUserId;
        UpdatedAt = now;
        return new TaskUpdate(textChanged, assigneeChanged, previousAssignee);
    }
}

/// <summary>What <see cref="TaskItem.Update"/> changed.</summary>
/// <param name="TextChanged">The title or the description changed.</param>
/// <param name="AssigneeChanged">The assignee changed (including assigning or unassigning).</param>
/// <param name="PreviousAssigneeUserId">The assignee before the edit.</param>
public readonly record struct TaskUpdate(bool TextChanged, bool AssigneeChanged, Guid? PreviousAssigneeUserId)
{
    /// <summary>Gets a value indicating whether anything changed.</summary>
    public bool Any => TextChanged || AssigneeChanged;
}
