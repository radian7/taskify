using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using Taskify.Web.Components.Shared;
using Taskify.Web.Services.ApiClients;
using Taskify.Web.Tests.Support;

namespace Taskify.Web.Tests.Comments;

/// <summary>The comment thread on the task details page (spec FR-015, FR-016, FR-024; User Story 4).</summary>
public sealed class CommentThreadTests : BunitContext
{
    private static readonly Guid TaskId = Guid.Parse("33333333-3333-3333-0001-000000000001");

    private readonly StubHandler handler = new();

    private static DateTimeOffset At(int day, int hour = 9) => new(2026, 9, day, hour, 0, 0, TimeSpan.Zero);

    private static string Json(object value) => JsonSerializer.Serialize(value, ContractJson.Options);

    private static CommentDto Comment(Guid author, string text, DateTimeOffset created, DateTimeOffset? edited = null) =>
        new(Guid.NewGuid(), TaskId, author, text, created, edited, null, false);

    private static CommentDto Deleted(Guid author, DateTimeOffset created, DateTimeOffset deletedAt) =>
        new(Guid.NewGuid(), TaskId, author, null, created, null, deletedAt, true);

    private IRenderedComponent<CommentThread> RenderThread(Guid currentUser, params CommentDto[] comments)
    {
        handler.On($"/api/tasks/{TaskId}/comments", Json(comments));
        var identity = TestIdentity.For(currentUser);
        Services.AddSingleton(identity);
        Services.AddSingleton(TestClients.Tasks(handler, identity));
        var users = SampleUsers.All.ToDictionary(u => u.Id);
        return Render<CommentThread>(p => p
            .Add(c => c.TaskId, TaskId)
            .Add(c => c.CurrentUserId, currentUser)
            .Add(c => c.Users, users));
    }

    [Fact]
    public void Comments_show_author_and_time_oldest_first()
    {
        var cut = RenderThread(
            SeedIds.Priya,
            Comment(SeedIds.Jordan, "second", At(3)),
            Comment(SeedIds.Maya, "first", At(2, 14)));

        var items = cut.FindAll("li.comment");
        Assert.Equal(2, items.Count);
        Assert.Contains("Maya Chen", items[0].TextContent);
        Assert.Contains("2 Sep 2026 14:00 UTC", items[0].TextContent);
        Assert.Contains("first", items[0].TextContent);
        Assert.Contains("Jordan Lee", items[1].TextContent);
        Assert.Contains("second", items[1].TextContent);
    }

    [Fact]
    public void An_edited_comment_has_an_edited_indicator_and_others_do_not()
    {
        var cut = RenderThread(
            SeedIds.Priya,
            Comment(SeedIds.Maya, "changed", At(2), edited: At(3)),
            Comment(SeedIds.Jordan, "plain", At(4)));

        var items = cut.FindAll("li.comment");
        Assert.Single(items[0].QuerySelectorAll(".comment-edited"));
        Assert.Empty(items[1].QuerySelectorAll(".comment-edited"));
    }

    [Fact]
    public void A_deleted_comment_shows_a_placeholder_with_the_deletion_time()
    {
        var cut = RenderThread(SeedIds.Maya, Deleted(SeedIds.Priya, At(2), At(5, 16)));

        var item = Assert.Single(cut.FindAll("li.comment"));
        Assert.Contains("Comment deleted by Priya Patel", item.TextContent);
        Assert.Contains("5 Sep 2026 16:00 UTC", item.TextContent);
    }

    [Fact]
    public void Edit_and_delete_buttons_appear_only_on_the_current_users_live_comments()
    {
        var cut = RenderThread(
            SeedIds.Priya,
            Comment(SeedIds.Maya, "not mine", At(2)),
            Comment(SeedIds.Priya, "mine", At(3)),
            Deleted(SeedIds.Priya, At(4), At(5)));

        var items = cut.FindAll("li.comment");
        Assert.Empty(items[0].QuerySelectorAll("button"));
        Assert.Single(items[1].QuerySelectorAll(".comment-edit-button"));
        Assert.Single(items[1].QuerySelectorAll(".comment-delete-button"));
        Assert.Empty(items[2].QuerySelectorAll("button"));
    }

    [Fact]
    public void Script_text_is_shown_as_plain_text()
    {
        const string Hostile = "<script>alert('x')</script>";
        var cut = RenderThread(SeedIds.Priya, Comment(SeedIds.Maya, Hostile, At(2)));

        Assert.Empty(cut.FindAll("script"));
        Assert.Equal(Hostile, cut.Find(".comment-text").TextContent);
    }

    [Fact]
    public void Adding_a_comment_posts_it_and_shows_it()
    {
        var created = Comment(SeedIds.Priya, "hello", At(6));
        var cut = RenderThread(SeedIds.Priya);
        handler.On($"/api/tasks/{TaskId}/comments", Json(created), System.Net.HttpStatusCode.Created);

        cut.Find("#new-comment").Input("hello");
        Assert.Equal("5 / 2000", cut.Find(".comment-add .char-counter").TextContent);
        cut.Find("form.comment-add").Submit();

        cut.WaitForAssertion(() => Assert.Contains("hello", cut.Find(".comment-text").TextContent));
        var post = Assert.Single(handler.Sent, s => s.Method == "POST");
        Assert.Equal("""{"text":"hello"}""", post.Body);
    }

    [Fact]
    public void A_rejected_comment_shows_the_server_error()
    {
        var cut = RenderThread(SeedIds.Priya);
        handler.On(
            $"/api/tasks/{TaskId}/comments",
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"text":["Text must be 1 to 2000 characters."]}}""",
            System.Net.HttpStatusCode.BadRequest);

        cut.Find("form.comment-add").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Text must be 1 to 2000 characters.", cut.Find(".error-fields").TextContent));
    }
}
