using System.Globalization;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.Notifications.Api.Domain;
using Taskify.Security.Validation;

namespace Taskify.UnitTests.Notifications;

/// <summary>
/// The notification trigger table (data-model.md, spec FR-027, SC-009; User Story 6): which events notify whom, that
/// the actor is never notified, and that summaries stay within 300 user-perceived characters.
/// </summary>
public class TriggerRuleTests
{
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    private static readonly Guid TaskId = Guid.Parse("33333333-3333-3333-0001-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-0001-000000000001");
    private static readonly Guid Ana = SeedIds.Priya;
    private static readonly Guid Bo = SeedIds.Jordan;

    private static EventEnvelope Envelope(string type, object payload, Guid actor) =>
        new(Guid.NewGuid(), type, EventEnvelope.CurrentVersion, DateTimeOffset.UtcNow, actor,
            JsonSerializer.SerializeToElement(payload, ContractJson.Options));

    private static EventEnvelope Created(Guid? assignee, Guid actor, string title = "Login page") =>
        Envelope(EventTypes.TaskCreated, new TaskCreatedPayload(TaskId, ProjectId, title, TaskStatus.ToDo, assignee), actor);

    private static EventEnvelope Assigned(Guid? assignee, Guid actor, string title = "Login page") =>
        Envelope(EventTypes.TaskAssigned, new TaskAssignedPayload(TaskId, ProjectId, title, null, assignee), actor);

    private static EventEnvelope Moved(Guid? assignee, Guid actor, string title = "Login page", TaskStatus to = TaskStatus.InReview) =>
        Envelope(EventTypes.TaskMoved, new TaskMovedPayload(TaskId, ProjectId, title, TaskStatus.InProgress, to, assignee), actor);

    private static EventEnvelope Commented(Guid? assignee, Guid actor, string title = "Login page") =>
        Envelope(EventTypes.CommentAdded, new CommentAddedPayload(TaskId, ProjectId, title, Guid.NewGuid(), assignee), actor);

    // ---- recipients ------------------------------------------------------------------------------------------

    [Fact]
    public void TaskAssigned_notifies_the_new_assignee_when_it_is_not_the_actor()
    {
        var draft = NotificationTriggerRules.Evaluate(Assigned(Bo, Ana), "Ana");

        Assert.NotNull(draft);
        Assert.Equal(Bo, draft.RecipientUserId);
        Assert.Equal(NotificationType.TaskAssigned, draft.Type);
        Assert.Equal(TaskId, draft.TaskId);
        Assert.Equal(ProjectId, draft.ProjectId);
        Assert.Contains("Ana", draft.Summary, StringComparison.Ordinal);
        Assert.Contains("Login page", draft.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskAssigned_to_the_actor_or_to_nobody_creates_nothing()
    {
        Assert.Null(NotificationTriggerRules.Evaluate(Assigned(Ana, Ana), "Ana"));
        Assert.Null(NotificationTriggerRules.Evaluate(Assigned(null, Ana), "Ana"));
    }

    [Fact]
    public void TaskCreated_with_an_assignee_other_than_the_actor_creates_a_TaskAssigned_notification()
    {
        var draft = NotificationTriggerRules.Evaluate(Created(Bo, Ana), "Ana");

        Assert.NotNull(draft);
        Assert.Equal(Bo, draft.RecipientUserId);
        Assert.Equal(NotificationType.TaskAssigned, draft.Type);
        Assert.Equal(TaskId, draft.TaskId);
        Assert.Equal(ProjectId, draft.ProjectId);
    }

    [Fact]
    public void TaskCreated_without_an_assignee_or_assigned_to_the_actor_creates_nothing()
    {
        Assert.Null(NotificationTriggerRules.Evaluate(Created(null, Ana), "Ana"));
        Assert.Null(NotificationTriggerRules.Evaluate(Created(Ana, Ana), "Ana"));
    }

    [Fact]
    public void TaskMoved_notifies_the_current_assignee_when_it_is_not_the_actor()
    {
        var draft = NotificationTriggerRules.Evaluate(Moved(Bo, Ana), "Ana");

        Assert.NotNull(draft);
        Assert.Equal(Bo, draft.RecipientUserId);
        Assert.Equal(NotificationType.TaskMoved, draft.Type);
        Assert.Equal("Ana moved 'Login page' to In Review", draft.Summary);
    }

    [Fact]
    public void TaskMoved_of_an_unassigned_task_or_by_the_assignee_creates_nothing()
    {
        Assert.Null(NotificationTriggerRules.Evaluate(Moved(null, Ana), "Ana"));
        Assert.Null(NotificationTriggerRules.Evaluate(Moved(Ana, Ana), "Ana"));
    }

    [Fact]
    public void CommentAdded_creates_a_TaskCommented_notification_for_the_assignee_when_it_is_not_the_actor()
    {
        var draft = NotificationTriggerRules.Evaluate(Commented(Bo, Ana), "Ana");

        Assert.NotNull(draft);
        Assert.Equal(Bo, draft.RecipientUserId);
        Assert.Equal(NotificationType.TaskCommented, draft.Type);
        Assert.Contains("Ana", draft.Summary, StringComparison.Ordinal);
        Assert.Contains("Login page", draft.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentAdded_on_an_unassigned_task_or_by_the_assignee_creates_nothing()
    {
        Assert.Null(NotificationTriggerRules.Evaluate(Commented(null, Ana), "Ana"));
        Assert.Null(NotificationTriggerRules.Evaluate(Commented(Ana, Ana), "Ana"));
    }

    [Fact]
    public void Other_events_create_nothing()
    {
        var updated = Envelope(EventTypes.TaskUpdated, new TaskUpdatedPayload(TaskId, ProjectId, "Login page"), Ana);
        var edited = Envelope(EventTypes.CommentEdited, new CommentEditedPayload(TaskId, ProjectId, Guid.NewGuid()), Ana);
        var deleted = Envelope(EventTypes.CommentDeleted, new CommentDeletedPayload(TaskId, ProjectId, Guid.NewGuid()), Ana);
        var project = Envelope(EventTypes.ProjectCreated, new ProjectCreatedPayload(ProjectId, "Mobile"), Ana);

        foreach (var envelope in new[] { updated, edited, deleted, project })
        {
            Assert.Null(NotificationTriggerRules.Evaluate(envelope, "Ana"));
        }
    }

    [Fact]
    public void A_notification_is_never_addressed_to_the_actor()
    {
        // Every notifying type, with the assignee being the actor or somebody else, and with the roles swapped.
        var envelopes = new[]
        {
            Created(Bo, Ana), Created(Ana, Bo), Created(Ana, Ana),
            Assigned(Bo, Ana), Assigned(Ana, Bo), Assigned(Ana, Ana),
            Moved(Bo, Ana), Moved(Ana, Bo), Moved(Ana, Ana),
            Commented(Bo, Ana), Commented(Ana, Bo), Commented(Ana, Ana),
        };

        foreach (var envelope in envelopes)
        {
            var draft = NotificationTriggerRules.Evaluate(envelope, "name");

            Assert.True(draft is null || draft.RecipientUserId != envelope.ActorUserId);
        }
    }

    // ---- summaries -------------------------------------------------------------------------------------------

    [Fact]
    public void A_long_title_is_truncated_to_at_most_300_user_perceived_characters()
    {
        var draft = NotificationTriggerRules.Evaluate(Moved(Bo, Ana, new string('x', 5000)), "Ana");

        Assert.NotNull(draft);
        Assert.InRange(TextLength.Count(draft.Summary), 1, NotificationTriggerRules.MaxSummaryCharacters);
        Assert.StartsWith("Ana moved 'xxx", draft.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_of_family_emoji_is_cut_between_characters_never_inside_one()
    {
        var title = string.Concat(Enumerable.Repeat(Family, 400));

        foreach (var envelope in new[] { Moved(Bo, Ana, title), Commented(Bo, Ana, title), Assigned(Bo, Ana, title) })
        {
            var draft = NotificationTriggerRules.Evaluate(envelope, "Ana");

            Assert.NotNull(draft);
            Assert.True(TextLength.Count(draft.Summary) <= NotificationTriggerRules.MaxSummaryCharacters);
            Assert.DoesNotContain('�', draft.Summary);
            // No half-cut sequence: after removing whole family emoji nothing emoji-like remains.
            var rest = draft.Summary.Replace(Family, string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain('‍', rest);
            Assert.DoesNotContain("\U0001F468", rest, StringComparison.Ordinal);
            Assert.DoesNotContain("\U0001F466", rest, StringComparison.Ordinal);
            Assert.All(Elements(draft.Summary), e => Assert.False(e.Length == 1 && char.IsSurrogate(e[0])));
        }
    }

    [Fact]
    public void A_short_summary_is_not_truncated()
    {
        var draft = NotificationTriggerRules.Evaluate(Moved(Bo, Ana, "Short", TaskStatus.Done), "Ana");

        Assert.NotNull(draft);
        Assert.Equal("Ana moved 'Short' to Done", draft.Summary);
    }

    private static IEnumerable<string> Elements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return (string)enumerator.Current;
        }
    }
}
