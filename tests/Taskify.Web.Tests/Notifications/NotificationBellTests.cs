using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using Taskify.Contracts.Hub;
using Taskify.Web.Components.Shared;
using Taskify.Web.Tests.Support;

namespace Taskify.Web.Tests.Notifications;

/// <summary>
/// The notification bell in the page header (spec FR-027, FR-028, FR-026; User Story 6). The REST API is the truth: a
/// live <c>user:{id}</c> signal or a resync makes the bell re-fetch, and the summary is always shown as plain text (R9).
/// </summary>
/// <remarks>
/// The stub answers these routes: <c>GET /api/notifications/unread-count</c>, <c>GET /api/notifications?limit=50</c>,
/// <c>POST /api/notifications/{id}/read</c> and <c>POST /api/notifications/read-all</c>.
/// </remarks>
public sealed class NotificationBellTests : BunitContext
{
    private const string CountPath = "/api/notifications/unread-count";
    private const string ListPath = "/api/notifications?limit=50";

    private static readonly Guid ProjectId = SeedIds.MobileAppLaunch;
    private static readonly Guid TaskId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private readonly StubHandler handler = new();
    private readonly FakeRealtimeBoard realtime;

    /// <summary>Registers the client, the identity and the fake realtime service.</summary>
    public NotificationBellTests()
    {
        var identity = TestIdentity.For(SeedIds.Priya);
        Services.AddSingleton(identity);
        Services.AddSingleton(NotificationTestClients.Notifications(handler, identity));
        realtime = Services.AddFakeRealtime();
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, ContractJson.Options);

    private static NotificationDto Note(string summary, bool isRead = false, int minute = 0) =>
        new(Guid.NewGuid(), NotificationType.TaskMoved, TaskId, ProjectId, SeedIds.Jordan, summary,
            new DateTimeOffset(2026, 9, 3, 10, minute, 0, TimeSpan.Zero), isRead);

    private int Fetches(string pathAndQuery) => handler.Sent.Count(s => s.Method == "GET" && s.PathAndQuery == pathAndQuery);

    private IRenderedComponent<NotificationBell> RenderBell(int unread, Guid? user = null, params NotificationDto[] list)
    {
        handler
            .On(CountPath, $$"""{"count":{{unread}}}""")
            .On(ListPath, Json(list));
        return Render<NotificationBell>(p => p.Add(c => c.UserId, user ?? SeedIds.Priya));
    }

    private static void Open(IRenderedComponent<NotificationBell> cut) => cut.Find("button.notification-toggle").Click();

    [Fact]
    public void The_badge_shows_the_unread_count()
    {
        var cut = RenderBell(3);

        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find(".notification-badge").TextContent.Trim()));
    }

    [Fact]
    public void With_nothing_unread_there_is_no_badge()
    {
        var cut = RenderBell(0);

        cut.WaitForAssertion(() => Assert.Equal(1, Fetches(CountPath)));
        Assert.Empty(cut.FindAll(".notification-badge"));
    }

    [Fact]
    public void Opening_the_dropdown_lists_the_notifications_newest_first_with_a_link_to_the_task()
    {
        var newer = Note("Jordan moved 'Login page' to Done", minute: 30);
        var older = Note("Jordan commented on 'Login page'", isRead: true, minute: 5);
        var cut = RenderBell(1, null, newer, older);
        cut.WaitForAssertion(() => Assert.Equal(1, Fetches(CountPath)));

        Open(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.notification").Count));
        var items = cut.FindAll("li.notification");
        Assert.Contains("Jordan moved 'Login page' to Done", items[0].TextContent);
        Assert.Contains("Jordan commented on 'Login page'", items[1].TextContent);
        Assert.Equal($"/projects/{ProjectId}/tasks/{TaskId}", items[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("notification--unread", items[0].ClassName);
        Assert.DoesNotContain("notification--unread", items[1].ClassName);
    }

    [Fact]
    public void A_summary_is_rendered_as_plain_text_never_as_markup()
    {
        const string Hostile = "<img src=x onerror=alert(1)> <b>bold</b> & \"quoted\"";
        var cut = RenderBell(1, null, Note(Hostile));
        cut.WaitForAssertion(() => Assert.Equal(1, Fetches(CountPath)));

        Open(cut);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("li.notification")));
        var item = cut.Find("li.notification");
        Assert.Contains(Hostile, item.TextContent);
        Assert.Empty(cut.FindAll("li.notification img"));
        Assert.Empty(cut.FindAll("li.notification b"));
    }

    [Fact]
    public void Clicking_a_notification_marks_it_read_and_lowers_the_count()
    {
        var note = Note("Jordan moved 'Login page' to Done");
        var cut = RenderBell(2, null, note, Note("Another"));
        handler.On($"/api/notifications/{note.Id}/read", string.Empty, HttpStatusCode.NoContent);
        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find(".notification-badge").TextContent.Trim()));
        Open(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.notification").Count));

        // After the click the server reports one unread notification left.
        handler.On(CountPath, """{"count":1}""");
        cut.FindAll("li.notification")[0].QuerySelector("a")!.Click();

        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".notification-badge").TextContent.Trim()));
        Assert.Contains(handler.Sent, s => s.Method == "POST" && s.PathAndQuery == $"/api/notifications/{note.Id}/read");
    }

    [Fact]
    public void Mark_all_read_posts_read_all_and_clears_the_badge()
    {
        var cut = RenderBell(2, null, Note("One"), Note("Two"));
        handler.On("/api/notifications/read-all", string.Empty, HttpStatusCode.NoContent);
        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find(".notification-badge").TextContent.Trim()));
        Open(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.notification").Count));

        handler.On(CountPath, """{"count":0}""");
        cut.Find("button.mark-all-read").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".notification-badge")));
        Assert.Contains(handler.Sent, s => s.Method == "POST" && s.PathAndQuery == "/api/notifications/read-all");
    }

    [Fact]
    public void A_live_signal_for_the_user_makes_the_bell_show_the_new_count()
    {
        var cut = RenderBell(1);
        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".notification-badge").TextContent.Trim()));
        Assert.Equal([$"user:{SeedIds.Priya}"], realtime.Subscribed);

        // NotificationCreated reaches the screen as a signal on user:{id}; the count comes from the REST API.
        handler.On(CountPath, """{"count":2}""");
        realtime.Raise($"user:{SeedIds.Priya}");

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find(".notification-badge").TextContent.Trim()));
    }

    [Fact]
    public void A_resync_re_fetches_the_count_and_the_open_list()
    {
        var cut = RenderBell(1, null, Note("Old one"));
        cut.WaitForAssertion(() => Assert.Equal(1, Fetches(CountPath)));
        Open(cut);
        cut.WaitForAssertion(() => Assert.Contains("Old one", cut.Markup));

        handler.On(CountPath, """{"count":4}""").On(ListPath, Json(new[] { Note("Missed while offline") }));
        realtime.Resync();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("4", cut.Find(".notification-badge").TextContent.Trim());
            Assert.Contains("Missed while offline", cut.Markup);
        });
    }

    [Fact]
    public void A_resync_with_the_dropdown_closed_re_fetches_only_the_count()
    {
        var cut = RenderBell(1);
        cut.WaitForAssertion(() => Assert.Equal(1, Fetches(CountPath)));

        realtime.Resync();

        cut.WaitForAssertion(() => Assert.Equal(2, Fetches(CountPath)));
        Assert.Equal(0, Fetches(ListPath));
    }

    [Fact]
    public void Switching_user_leaves_the_old_group_joins_the_new_one_and_re_fetches()
    {
        var cut = RenderBell(1);
        cut.WaitForAssertion(() => Assert.Equal(1, Fetches(CountPath)));
        Assert.Equal(1, realtime.ActiveCount);

        handler.On(CountPath, """{"count":7}""");
        cut.Render(p => p.Add(c => c.UserId, SeedIds.Jordan));

        Assert.Equal([$"user:{SeedIds.Priya}", $"user:{SeedIds.Jordan}"], realtime.Subscribed);
        Assert.Equal(1, realtime.ActiveCount);
        cut.WaitForAssertion(() => Assert.Equal("7", cut.Find(".notification-badge").TextContent.Trim()));
    }

    [Fact]
    public void Disposing_the_bell_leaves_the_group()
    {
        var cut = RenderBell(1);
        Assert.Equal(1, realtime.ActiveCount);

        cut.Instance.Dispose();   // IRenderedComponent.Dispose does not dispose the component itself

        Assert.Equal(0, realtime.ActiveCount);
    }
}
