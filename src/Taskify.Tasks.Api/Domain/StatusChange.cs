namespace Taskify.Tasks.Api.Domain;

/// <summary>
/// One recorded move of a task between columns (spec FR-023). Read-only once recorded: there is no way to edit or
/// delete a status change, and the service's database role has no UPDATE or DELETE permission on its table
/// (plan, threat "Tampering with status history").
/// </summary>
public sealed class StatusChange
{
    /// <summary>Creates a status change.</summary>
    /// <param name="id">The ID. "Generated".</param>
    /// <param name="taskId">The task that moved. "FK → Task".</param>
    /// <param name="fromStatus">The column it left. "Must differ from <paramref name="toStatus"/>".</param>
    /// <param name="toStatus">The column it entered.</param>
    /// <param name="movedByUserId">Who moved it. "Acting user".</param>
    /// <param name="movedAt">When it moved. "Set by the server".</param>
    /// <exception cref="ArgumentException"><paramref name="fromStatus"/> equals <paramref name="toStatus"/>: a move to the same column is not a change.</exception>
    public StatusChange(Guid id, Guid taskId, TaskStatus fromStatus, TaskStatus toStatus, Guid movedByUserId, DateTimeOffset movedAt)
    {
        if (fromStatus == toStatus)
        {
            throw new ArgumentException("A status change must move the task to a different column.", nameof(toStatus));
        }

        Id = id;
        TaskId = taskId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        MovedByUserId = movedByUserId;
        MovedAt = movedAt;
    }

    /// <summary>Gets the status change ID.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the task that moved.</summary>
    public Guid TaskId { get; private set; }

    /// <summary>Gets the column the task left.</summary>
    public TaskStatus FromStatus { get; private set; }

    /// <summary>Gets the column the task entered.</summary>
    public TaskStatus ToStatus { get; private set; }

    /// <summary>Gets the user who moved the task.</summary>
    public Guid MovedByUserId { get; private set; }

    /// <summary>Gets when the task moved (UTC), set by the server.</summary>
    public DateTimeOffset MovedAt { get; private set; }
}
