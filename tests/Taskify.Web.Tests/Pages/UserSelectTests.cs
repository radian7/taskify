using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Taskify.Contracts;
using Taskify.Web.Components.Pages;
using Taskify.Web.Tests.Support;

namespace Taskify.Web.Tests.Pages;

/// <summary>The user selection screen (spec FR-001, FR-002; User Story 1).</summary>
public sealed class UserSelectTests : BunitContext
{
    private IRenderedComponent<UserSelect> RenderPage(StubHandler? handler = null)
    {
        handler ??= new StubHandler().On("/api/users", JsonSerializer.Serialize(SampleUsers.All, ContractJson.Options));
        Services.AddSingleton(TestClients.Projects(handler, TestIdentity.For(null)));
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        return Render<UserSelect>();
    }

    [Fact]
    public void The_five_predefined_users_are_listed_with_their_roles()
    {
        var cut = RenderPage();

        var buttons = cut.FindAll("form button").Select(b => b.TextContent.Trim()).ToList();

        Assert.Equal(5, buttons.Count);
        Assert.Contains(buttons, b => b.Contains("Maya Chen") && b.Contains("Product Manager"));
        Assert.Equal(4, buttons.Count(b => b.Contains("Engineer")));
        Assert.Contains(buttons, b => b.Contains("Tomasz Wiśniewski"));
    }

    [Fact]
    public void No_password_is_requested()
    {
        var cut = RenderPage();

        Assert.Empty(cut.FindAll("input[type=password]"));
        Assert.DoesNotContain("password", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Each_user_is_a_form_that_posts_the_user_id_with_an_antiforgery_token_to_the_session_endpoint()
    {
        var cut = RenderPage();

        var forms = cut.FindAll("form");

        Assert.Equal(5, forms.Count);
        Assert.All(forms, form =>
        {
            Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
            Assert.Equal("/session/select", form.GetAttribute("action"));
            Assert.NotNull(form.QuerySelector("input[type=hidden][name=userId]"));
            Assert.NotNull(form.QuerySelector("input[name=__RequestVerificationToken]"));
        });
        Assert.Equal(
            SeedIds.AllUsers.Select(id => id.ToString()).Order(),
            forms.Select(f => f.QuerySelector("input[name=userId]")!.GetAttribute("value")!).Order());
    }

    [Fact]
    public void An_unreachable_projects_api_shows_an_error_instead_of_an_empty_list()
    {
        var handler = new StubHandler().On("/api/users", "{}", System.Net.HttpStatusCode.ServiceUnavailable);

        var cut = RenderPage(handler);

        Assert.Empty(cut.FindAll("form"));
        Assert.Contains("temporarily unavailable", cut.Markup);
    }

    private sealed class FakeAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
