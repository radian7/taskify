using System.Net;
using System.Text;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Notifications (spec FR-027 to FR-030, SC-009; User Story 6; contracts/notifications-api.yaml). Most tests deliver
/// forged events straight to <c>POST /internal/events</c> with the Tasks key, so they spend none of the 60 writes a
/// minute that the seeded users have (SC-010). Every test uses its own random task and project IDs and looks only at
/// those, because other test classes create notifications for the same users. Only the last test goes through the
/// real Projects and Tasks APIs.
/// </summary>
/// <param name="app">The running application.</param>
public class NotificationsContractTests(TaskifyAppFixture app)
{
    private static readonly OpenApiContract Contract = OpenApiContract.Load("notifications-api.yaml");

    // Priya acts, Tomasz is the usual recipient and Liam the "other user" (Maya is kept free for the rate-limit tests).
    private static readonly Guid Actor = SeedIds.Priya;
    private static readonly Guid Recipient = SeedIds.Tomasz;
    private static readonly Guid Other = SeedIds.Liam;

    private HttpClient Notifications(Guid user) => app.CreateClient("notifications-api", TaskifyAppFixture.WebKey, user);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    private static string Quote(Guid? id) => id is null ? "null" : $"\"{id}\"";

    private static string Envelope(string type, Guid eventId, Guid actor, string payload) =>
        $$"""{"eventId":"{{eventId}}","type":"{{type}}","version":1,"occurredAt":"{{DateTimeOffset.UtcNow:O}}","actorUserId":"{{actor}}","payload":{{payload}}}""";

    /// <summary>One task the tests deliver events about. Its IDs are random, so no other test sees its notifications.</summary>
    private sealed record Subject(Guid TaskId, Guid ProjectId, string Title)
    {
        public static Subject New() => new(Guid.NewGuid(), Guid.NewGuid(), $"Notify {Guid.NewGuid():N}");
    }

    private async Task DeliverAsync(string type, string payload, Guid actor, Guid? eventId = null)
    {
        using var tasks = app.CreateClient("notifications-api", TaskifyAppFixture.TasksKey);
        using var response = await tasks.PostAsync("/internal/events", Json(Envelope(type, eventId ?? Guid.NewGuid(), actor, payload)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private Task MovedAsync(Subject s, Guid? assignee, Guid actor, Guid? eventId = null) =>
        DeliverAsync(
            EventTypes.TaskMoved,
            $$"""{"taskId":"{{s.TaskId}}","projectId":"{{s.ProjectId}}","title":"{{s.Title}}","fromStatus":"ToDo","toStatus":"InReview","assigneeUserId":{{Quote(assignee)}}}""",
            actor,
            eventId);

    private Task AssignedAsync(Subject s, Guid? assignee, Guid actor) =>
        DeliverAsync(
            EventTypes.TaskAssigned,
            $$"""{"taskId":"{{s.TaskId}}","projectId":"{{s.ProjectId}}","title":"{{s.Title}}","previousAssigneeUserId":null,"assigneeUserId":{{Quote(assignee)}}}""",
            actor);

    private Task CreatedAsync(Subject s, Guid? assignee, Guid actor) =>
        DeliverAsync(
            EventTypes.TaskCreated,
            $$"""{"taskId":"{{s.TaskId}}","projectId":"{{s.ProjectId}}","title":"{{s.Title}}","status":"ToDo","assigneeUserId":{{Quote(assignee)}}}""",
            actor);

    private Task CommentedAsync(Subject s, Guid? assignee, Guid actor) =>
        DeliverAsync(
            EventTypes.CommentAdded,
            $$"""{"taskId":"{{s.TaskId}}","projectId":"{{s.ProjectId}}","title":"{{s.Title}}","commentId":"{{Guid.NewGuid()}}","assigneeUserId":{{Quote(assignee)}}}""",
            actor);

    /// <summary>Lists a user's notifications (the newest 100) and keeps the ones about one task.</summary>
    private async Task<List<JsonElement>> ListForAsync(Guid user, Subject s, bool unreadOnly = false)
    {
        using var client = Notifications(user);
        var query = unreadOnly ? "?limit=100&unreadOnly=true" : "?limit=100";
        using var response = await client.GetAsync("/api/notifications" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(Contract.ValidateItems("Notification", body).IsValid, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray()
            .Where(n => n.GetProperty("taskId").GetGuid() == s.TaskId)
            .Select(n => n.Clone())
            .ToList();
    }

    private async Task<int> UnreadCountAsync(Guid user)
    {
        using var client = Notifications(user);
        using var response = await client.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["count"], JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        return JsonDocument.Parse(body).RootElement.GetProperty("count").GetInt32();
    }

    private async Task<HttpStatusCode> PostAsync(Guid user, string path)
    {
        using var client = Notifications(user);
        using var response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    // ---- list ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_has_only_the_acting_users_notifications_newest_first()
    {
        var first = Subject.New();
        var second = Subject.New();
        var forOther = Subject.New();
        await MovedAsync(first, Recipient, Actor);
        await MovedAsync(second, Recipient, Actor);
        await MovedAsync(forOther, Other, Actor);

        using var client = Notifications(Recipient);
        var body = await client.GetStringAsync("/api/notifications?limit=100", TestContext.Current.CancellationToken);
        var all = JsonDocument.Parse(body).RootElement.EnumerateArray().Select(n => n.GetProperty("taskId").GetGuid()).ToList();

        Assert.True(Contract.ValidateItems("Notification", body).IsValid, body);
        Assert.Contains(first.TaskId, all);
        Assert.Contains(second.TaskId, all);
        Assert.DoesNotContain(forOther.TaskId, all);
        Assert.True(all.IndexOf(second.TaskId) < all.IndexOf(first.TaskId), "The newest notification must come first.");
        var createdAt = JsonDocument.Parse(body).RootElement.EnumerateArray().Select(n => n.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(createdAt.OrderByDescending(t => t), createdAt);
        Assert.Single(await ListForAsync(Other, forOther));
        Assert.Empty(await ListForAsync(Other, first));
    }

    [Fact]
    public async Task The_summary_names_the_actor_the_task_and_the_column_and_the_notification_carries_the_ids()
    {
        var subject = Subject.New();
        await MovedAsync(subject, Recipient, Actor);

        var note = Assert.Single(await ListForAsync(Recipient, subject));

        Assert.Equal($"Priya Patel moved '{subject.Title}' to In Review", note.GetProperty("summary").GetString());
        Assert.Equal("TaskMoved", note.GetProperty("type").GetString());
        Assert.Equal(subject.ProjectId, note.GetProperty("projectId").GetGuid());
        Assert.Equal(Actor, note.GetProperty("actorUserId").GetGuid());
        Assert.False(note.GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task UnreadOnly_leaves_out_notifications_that_were_read()
    {
        var read = Subject.New();
        var unread = Subject.New();
        await MovedAsync(read, Recipient, Actor);
        await MovedAsync(unread, Recipient, Actor);
        var readId = Assert.Single(await ListForAsync(Recipient, read)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(Recipient, $"/api/notifications/{readId}/read"));

        var unreadOnly = await ListForAsync(Recipient, unread, unreadOnly: true);
        var readOnly = await ListForAsync(Recipient, read, unreadOnly: true);
        var everything = await ListForAsync(Recipient, read);

        Assert.Single(unreadOnly);
        Assert.Empty(readOnly);
        Assert.True(Assert.Single(everything).GetProperty("isRead").GetBoolean());
    }

    [Theory]
    [InlineData("1", HttpStatusCode.OK)]
    [InlineData("50", HttpStatusCode.OK)]
    [InlineData("100", HttpStatusCode.OK)]
    [InlineData("0", HttpStatusCode.BadRequest)]
    [InlineData("101", HttpStatusCode.BadRequest)]
    [InlineData("-1", HttpStatusCode.BadRequest)]
    [InlineData("abc", HttpStatusCode.BadRequest)]
    public async Task The_limit_accepts_1_to_100_and_rejects_everything_else_with_400(string limit, HttpStatusCode expected)
    {
        using var client = Notifications(Recipient);

        using var response = await client.GetAsync($"/api/notifications?limit={limit}", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task A_limit_of_1_returns_at_most_one_notification()
    {
        await MovedAsync(Subject.New(), Recipient, Actor);
        await MovedAsync(Subject.New(), Recipient, Actor);
        using var client = Notifications(Recipient);

        var body = await client.GetStringAsync("/api/notifications?limit=1", TestContext.Current.CancellationToken);

        Assert.Single(JsonDocument.Parse(body).RootElement.EnumerateArray());
    }

    [Fact]
    public async Task The_notification_routes_need_the_Web_key_and_an_acting_user()
    {
        using var tasksKey = app.CreateClient("notifications-api", TaskifyAppFixture.TasksKey, Recipient);
        using var noKey = app.CreateClient("notifications-api", null, Recipient);
        using var noUser = app.CreateClient("notifications-api", TaskifyAppFixture.WebKey);

        using var wrongCaller = await tasksKey.GetAsync("/api/notifications", TestContext.Current.CancellationToken);
        using var unauthenticated = await noKey.GetAsync("/api/notifications/unread-count", TestContext.Current.CancellationToken);
        using var anonymous = await noUser.GetAsync("/api/notifications", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, wrongCaller.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, anonymous.StatusCode);
    }

    // ---- unread count, mark read -------------------------------------------------------------------------------

    [Fact]
    public async Task The_unread_count_goes_up_with_a_notification_and_down_when_it_is_read()
    {
        var subject = Subject.New();
        var before = await UnreadCountAsync(Recipient);

        await MovedAsync(subject, Recipient, Actor);
        var after = await UnreadCountAsync(Recipient);
        var id = Assert.Single(await ListForAsync(Recipient, subject)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(Recipient, $"/api/notifications/{id}/read"));
        var afterRead = await UnreadCountAsync(Recipient);

        // Other test classes may add notifications for this user at the same time, so compare with a margin upwards only.
        Assert.True(after >= before + 1, $"before {before}, after {after}");
        Assert.True(afterRead < after, $"after {after}, after read {afterRead}");
        Assert.True(await UnreadCountAsync(Other) >= 0);
    }

    [Fact]
    public async Task Marking_a_notification_read_is_idempotent_204_and_changes_only_that_one()
    {
        var subject = Subject.New();
        var untouched = Subject.New();
        await MovedAsync(subject, Recipient, Actor);
        await MovedAsync(untouched, Recipient, Actor);
        var id = Assert.Single(await ListForAsync(Recipient, subject)).GetProperty("id").GetGuid();

        var first = await PostAsync(Recipient, $"/api/notifications/{id}/read");
        var second = await PostAsync(Recipient, $"/api/notifications/{id}/read");

        Assert.Equal(HttpStatusCode.NoContent, first);
        Assert.Equal(HttpStatusCode.NoContent, second);
        Assert.True(Assert.Single(await ListForAsync(Recipient, subject)).GetProperty("isRead").GetBoolean());
        Assert.False(Assert.Single(await ListForAsync(Recipient, untouched)).GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Another_users_notification_and_an_unknown_ID_both_give_the_same_404_and_change_nothing_FR_029()
    {
        var subject = Subject.New();
        await MovedAsync(subject, Recipient, Actor);
        var id = Assert.Single(await ListForAsync(Recipient, subject)).GetProperty("id").GetGuid();
        using var other = Notifications(Other);

        using var foreign = await other.PostAsync($"/api/notifications/{id}/read", content: null, TestContext.Current.CancellationToken);
        using var unknown = await other.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", foreign.Content.Headers.ContentType?.MediaType);
        // Nothing in the answer tells the two cases apart.
        Assert.Equal(
            (await ReadAsync(unknown)).GetProperty("title").GetString(),
            (await ReadAsync(foreign)).GetProperty("title").GetString());
        Assert.False(Assert.Single(await ListForAsync(Recipient, subject)).GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Read_all_gives_204_and_marks_only_the_acting_users_notifications()
    {
        var mine = Subject.New();
        var theirs = Subject.New();
        await MovedAsync(mine, Recipient, Actor);
        await MovedAsync(theirs, Other, Actor);

        var status = await PostAsync(Recipient, "/api/notifications/read-all");

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.True(Assert.Single(await ListForAsync(Recipient, mine)).GetProperty("isRead").GetBoolean());
        Assert.False(Assert.Single(await ListForAsync(Other, theirs)).GetProperty("isRead").GetBoolean());
        Assert.Equal(0, await UnreadCountAsync(Recipient));
    }

    // ---- creation rules (SC-009) -------------------------------------------------------------------------------

    [Fact]
    public async Task An_event_delivered_twice_creates_one_notification()
    {
        var subject = Subject.New();
        var eventId = Guid.NewGuid();

        await MovedAsync(subject, Recipient, Actor, eventId);
        await MovedAsync(subject, Recipient, Actor, eventId);

        Assert.Single(await ListForAsync(Recipient, subject));
    }

    [Fact]
    public async Task Each_trigger_rule_creates_exactly_one_notification_for_the_right_user_and_self_actions_create_none()
    {
        var created = Subject.New();
        var assigned = Subject.New();
        var moved = Subject.New();
        var commented = Subject.New();

        // The qualifying actions: the actor is somebody else than the assignee.
        await CreatedAsync(created, Recipient, Actor);
        await AssignedAsync(assigned, Recipient, Actor);
        await MovedAsync(moved, Recipient, Actor);
        await CommentedAsync(commented, Recipient, Actor);

        Assert.Equal("TaskAssigned", Assert.Single(await ListForAsync(Recipient, created)).GetProperty("type").GetString());
        Assert.Equal("TaskAssigned", Assert.Single(await ListForAsync(Recipient, assigned)).GetProperty("type").GetString());
        Assert.Equal("TaskMoved", Assert.Single(await ListForAsync(Recipient, moved)).GetProperty("type").GetString());
        Assert.Equal("TaskCommented", Assert.Single(await ListForAsync(Recipient, commented)).GetProperty("type").GetString());

        // The recipient acting on tasks assigned to themselves, unassigned tasks, and events that never notify.
        var own = Subject.New();
        await CreatedAsync(own, Recipient, Recipient);
        await AssignedAsync(own, Recipient, Recipient);
        await MovedAsync(own, Recipient, Recipient);
        await CommentedAsync(own, Recipient, Recipient);
        await MovedAsync(own, null, Actor);
        await CommentedAsync(own, null, Actor);
        await AssignedAsync(own, null, Actor);
        await CreatedAsync(own, null, Actor);
        await DeliverAsync(EventTypes.TaskUpdated, $$"""{"taskId":"{{own.TaskId}}","projectId":"{{own.ProjectId}}","title":"{{own.Title}}"}""", Actor);
        await DeliverAsync(EventTypes.CommentEdited, $$"""{"taskId":"{{own.TaskId}}","projectId":"{{own.ProjectId}}","commentId":"{{Guid.NewGuid()}}"}""", Actor);
        await DeliverAsync(EventTypes.CommentDeleted, $$"""{"taskId":"{{own.TaskId}}","projectId":"{{own.ProjectId}}","commentId":"{{Guid.NewGuid()}}"}""", Actor);

        Assert.Empty(await ListForAsync(Recipient, own));
        Assert.Empty(await ListForAsync(Actor, own));
        Assert.Empty(await ListForAsync(Actor, created));
    }

    [Fact]
    public async Task The_three_actions_of_the_US6_independent_test_through_the_real_APIs_produce_exactly_three_notifications()
    {
        // Priya assigns a task to Tomasz, moves it and comments on it; Tomasz then acts on his own task.
        using var projects = app.CreateClient("projects-api", TaskifyAppFixture.WebKey, Actor);
        using var tasks = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, Actor);
        using var projectResponse = await projects.PostAsync("/api/projects", Json($$"""{"name":"Notify {{Guid.NewGuid():N}}"}"""), TestContext.Current.CancellationToken);
        var projectId = (await ReadAsync(projectResponse)).GetProperty("id").GetGuid();
        using var taskResponse = await tasks.PostAsync("/api/tasks", Json($$"""{"projectId":"{{projectId}}","title":"Three actions"}"""), TestContext.Current.CancellationToken);
        var taskId = (await ReadAsync(taskResponse)).GetProperty("id").GetGuid();
        var subject = new Subject(taskId, projectId, "Three actions");

        using var assign = await tasks.PutAsync($"/api/tasks/{taskId}", Json($$"""{"title":"Three actions","assigneeUserId":"{{Recipient}}"}"""), TestContext.Current.CancellationToken);
        using var move = await tasks.PostAsync($"/api/tasks/{taskId}/moves", Json("""{"toStatus":"InProgress"}"""), TestContext.Current.CancellationToken);
        using var comment = await tasks.PostAsync($"/api/tasks/{taskId}/comments", Json("""{"text":"Please have a look."}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        // Events travel through the outbox, so wait for them.
        var notes = new List<JsonElement>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (notes.Count < 3 && DateTimeOffset.UtcNow < deadline)
        {
            notes = await ListForAsync(Recipient, subject);
            if (notes.Count < 3)
            {
                await Task.Delay(250, TestContext.Current.CancellationToken);
            }
        }

        Assert.Equal(["TaskAssigned", "TaskCommented", "TaskMoved"], notes.Select(n => n.GetProperty("type").GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.All(notes, n => Assert.Contains("Priya Patel", n.GetProperty("summary").GetString(), StringComparison.Ordinal));

        // The assignee acts on their own task: still exactly three, and nothing for the actor.
        using var own = Notifications(Recipient);
        using var recipientTasks = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, Recipient);
        using var ownMove = await recipientTasks.PostAsync($"/api/tasks/{taskId}/moves", Json("""{"toStatus":"InReview"}"""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ownMove.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(3, (await ListForAsync(Recipient, subject)).Count);
        Assert.Empty(await ListForAsync(Actor, subject));
    }
}
