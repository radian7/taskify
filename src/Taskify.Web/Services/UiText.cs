using Taskify.Contracts;

namespace Taskify.Web.Services;

/// <summary>The words the UI uses for enum values and the board's fixed column order (spec FR-008).</summary>
public static class UiText
{
    /// <summary>The four columns of every board, in display order.</summary>
    public static readonly IReadOnlyList<TaskStatus> Columns =
        [TaskStatus.ToDo, TaskStatus.InProgress, TaskStatus.InReview, TaskStatus.Done];

    /// <summary>Gets the label of a column.</summary>
    /// <param name="status">The column.</param>
    /// <returns>For example <c>In Progress</c>.</returns>
    public static string StatusLabel(TaskStatus status) => status switch
    {
        TaskStatus.ToDo => "To Do",
        TaskStatus.InProgress => "In Progress",
        TaskStatus.InReview => "In Review",
        TaskStatus.Done => "Done",
        _ => status.ToString(),
    };

    /// <summary>Gets the label of a role.</summary>
    /// <param name="role">The role.</param>
    /// <returns>For example <c>Product Manager</c>.</returns>
    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.ProductManager => "Product Manager",
        UserRole.Engineer => "Engineer",
        _ => role.ToString(),
    };

    /// <summary>Formats a time for display. It is always shown in UTC and says so, so it reads the same for everyone.</summary>
    /// <param name="time">The time.</param>
    /// <returns>For example <c>4 Oct 2026 14:30 UTC</c>.</returns>
    public static string Date(DateTimeOffset time) =>
        time.UtcDateTime.ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
}
