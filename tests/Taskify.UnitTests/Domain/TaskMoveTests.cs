using Taskify.Contracts;
using Taskify.Tasks.Api.Domain;

namespace Taskify.UnitTests.Domain;

/// <summary>Moving a task (spec FR-012, FR-023; User Story 2 scenarios 3 and 4).</summary>
public class TaskMoveTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 30, 0, TimeSpan.Zero);

    private static TaskItem NewTask(TaskStatus status = TaskStatus.ToDo) =>
        new(Guid.NewGuid(), SeedIds.MobileAppLaunch, "Login page", "Details", status, SeedIds.Jordan, SeedIds.Maya, Created);

    [Fact]
    public void Moving_to_a_different_column_changes_the_status_and_records_who_when_and_where_from()
    {
        var task = NewTask();

        var change = task.MoveTo(TaskStatus.InProgress, SeedIds.Priya, Now);

        Assert.NotNull(change);
        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.Equal(task.Id, change.TaskId);
        Assert.Equal(TaskStatus.ToDo, change.FromStatus);
        Assert.Equal(TaskStatus.InProgress, change.ToStatus);
        Assert.Equal(SeedIds.Priya, change.MovedByUserId);
        Assert.Equal(Now, change.MovedAt);
        Assert.NotEqual(Guid.Empty, change.Id);
    }

    [Fact]
    public void A_move_updates_the_last_changed_time_but_not_the_creation_time()
    {
        var task = NewTask();

        task.MoveTo(TaskStatus.Done, SeedIds.Priya, Now);

        Assert.Equal(Now, task.UpdatedAt);
        Assert.Equal(Created, task.CreatedAt);
    }

    [Fact]
    public void A_move_leaves_everything_else_about_the_task_alone()
    {
        var task = NewTask();

        task.MoveTo(TaskStatus.InReview, SeedIds.Priya, Now);

        Assert.Equal("Login page", task.Title);
        Assert.Equal("Details", task.Description);
        Assert.Equal(SeedIds.Jordan, task.AssigneeUserId);
        Assert.Equal(SeedIds.Maya, task.CreatedByUserId);
    }

    [Theory]
    [InlineData(TaskStatus.ToDo)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.InReview)]
    [InlineData(TaskStatus.Done)]
    public void Moving_to_the_column_it_is_already_in_changes_nothing_and_records_nothing(TaskStatus status)
    {
        var task = NewTask(status);

        var change = task.MoveTo(status, SeedIds.Priya, Now);

        Assert.Null(change);
        Assert.Equal(status, task.Status);
        Assert.Equal(Created, task.UpdatedAt);
    }

    [Theory]
    [InlineData(TaskStatus.ToDo, TaskStatus.Done)]
    [InlineData(TaskStatus.Done, TaskStatus.ToDo)]
    [InlineData(TaskStatus.Done, TaskStatus.InProgress)]
    [InlineData(TaskStatus.InReview, TaskStatus.ToDo)]
    [InlineData(TaskStatus.InProgress, TaskStatus.InReview)]
    public void Any_column_can_move_to_any_other_including_backwards(TaskStatus from, TaskStatus to)
    {
        var task = NewTask(from);

        var change = task.MoveTo(to, SeedIds.Liam, Now);

        Assert.NotNull(change);
        Assert.Equal(to, task.Status);
        Assert.Equal(from, change.FromStatus);
    }

    [Fact]
    public void Whoever_moves_a_task_is_recorded_regardless_of_who_created_or_is_assigned_to_it()
    {
        var task = NewTask();

        var change = task.MoveTo(TaskStatus.Done, SeedIds.Tomasz, Now);

        Assert.Equal(SeedIds.Tomasz, change!.MovedByUserId);
        Assert.NotEqual(task.CreatedByUserId, change.MovedByUserId);
        Assert.NotEqual(task.AssigneeUserId, change.MovedByUserId);
    }

    [Fact]
    public void A_column_that_does_not_exist_is_refused()
    {
        var task = NewTask();

        Assert.Throws<ArgumentOutOfRangeException>(() => task.MoveTo((TaskStatus)42, SeedIds.Priya, Now));
        Assert.Equal(TaskStatus.ToDo, task.Status);
    }

    [Fact]
    public void A_status_change_must_differ_from_where_it_started()
    {
        Assert.Throws<ArgumentException>(() =>
            new StatusChange(Guid.NewGuid(), Guid.NewGuid(), TaskStatus.Done, TaskStatus.Done, SeedIds.Priya, Now));
    }

    [Fact]
    public void Consecutive_moves_form_a_chain_where_each_starts_where_the_last_ended()
    {
        var task = NewTask();

        var first = task.MoveTo(TaskStatus.InProgress, SeedIds.Priya, Now);
        var second = task.MoveTo(TaskStatus.Done, SeedIds.Liam, Now.AddMinutes(5));
        var third = task.MoveTo(TaskStatus.InProgress, SeedIds.Jordan, Now.AddMinutes(9));

        Assert.Equal(first!.ToStatus, second!.FromStatus);
        Assert.Equal(second.ToStatus, third!.FromStatus);
        Assert.Equal(TaskStatus.InProgress, task.Status);
    }
}
