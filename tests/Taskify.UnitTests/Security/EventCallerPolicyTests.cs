using Taskify.Contracts.Events;
using Taskify.Notifications.Api.Security;
using Taskify.Security.ApiKeys;

namespace Taskify.UnitTests.Security;

/// <summary>
/// Which calling service may send which event type to <c>/internal/events</c> (research R8 key matrix; spec FR-022).
/// The Projects API may send only <c>ProjectCreated</c>, the Tasks API the other seven types, and the Web app and the
/// Notifications API nothing; a refusal is a <c>403</c>.
/// </summary>
public class EventCallerPolicyTests
{
    public static TheoryData<string> TaskEventTypes() =>
        [.. EventTypes.All.Where(type => type != EventTypes.ProjectCreated)];

    [Fact]
    public void Projects_may_send_ProjectCreated()
    {
        Assert.True(EventCallerPolicy.IsAllowed(Callers.Projects, EventTypes.ProjectCreated));
    }

    [Theory]
    [MemberData(nameof(TaskEventTypes))]
    public void Projects_may_not_send_any_other_type(string type)
    {
        Assert.False(EventCallerPolicy.IsAllowed(Callers.Projects, type));
    }

    [Theory]
    [MemberData(nameof(TaskEventTypes))]
    public void Tasks_may_send_the_other_seven_types(string type)
    {
        Assert.True(EventCallerPolicy.IsAllowed(Callers.Tasks, type));
    }

    [Fact]
    public void Tasks_may_not_send_ProjectCreated()
    {
        Assert.False(EventCallerPolicy.IsAllowed(Callers.Tasks, EventTypes.ProjectCreated));
    }

    [Fact]
    public void There_are_seven_task_types_and_eight_types_in_all()
    {
        Assert.Equal(7, EventTypes.PublishedByTasks.Count);
        Assert.Equal(8, EventTypes.All.Count);
    }

    [Theory]
    [InlineData(Callers.Web)]
    [InlineData(Callers.Notifications)]
    [InlineData("someone-else")]
    [InlineData("")]
    [InlineData(null)]
    public void Everyone_else_may_send_nothing(string? caller)
    {
        foreach (var type in EventTypes.All)
        {
            Assert.False(EventCallerPolicy.IsAllowed(caller, type), $"{caller} / {type}");
        }
    }

    [Theory]
    [InlineData("projectcreated")]
    [InlineData("TASKMOVED")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void An_unknown_or_differently_cased_type_is_refused_for_every_caller(string type)
    {
        // Allow-list: "projectcreated" is not "ProjectCreated".
        foreach (var caller in new[] { Callers.Projects, Callers.Tasks, Callers.Web, Callers.Notifications })
        {
            Assert.False(EventCallerPolicy.IsAllowed(caller, type), $"{caller} / {type}");
        }
    }
}
