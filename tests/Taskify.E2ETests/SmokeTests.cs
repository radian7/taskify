using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Taskify.TestSupport;

namespace Taskify.E2ETests;

/// <summary>
/// End-to-end checks in a real browser against the real application (research R11): choose a user, view a board, move
/// cards by dragging and with the keyboard, and see the history. They move sample tasks, so they run in the sequential
/// sample-data collection and put every card back.
/// </summary>
/// <param name="app">The running application.</param>
/// <param name="browser">The shared browser.</param>
[Collection(SeedData.Collection)]
public class SmokeTests(TaskifyAppFixture app, BrowserFixture browser)
{
    private const string Task = "Plan beta tester outreach"; // an unassigned task in To Do, Mobile App Launch

    private static readonly LocatorAssertionsToBeVisibleOptions Quick = new() { Timeout = 1000 };

    private static ILocator Column(IPage page, string status) => page.Locator($"section.board-column[data-status='{status}']");

    private static ILocator Card(IPage page, string status, string title) =>
        Column(page, status).Locator("article.task-card", new LocatorLocatorOptions { Has = page.Locator($".task-title a:text-is('{title}')") });

    private static async Task ChooseUserAsync(IPage page, string name)
    {
        await page.GotoAsync("/");
        await page.Locator("button.user-button", new PageLocatorOptions { HasText = name }).ClickAsync();
        await page.WaitForURLAsync("**/projects");
    }

    private static async Task OpenBoardAsync(IPage page, string project)
    {
        await page.Locator("a.project-name", new PageLocatorOptions { HasText = project }).ClickAsync();
        await Assertions.Expect(Column(page, "ToDo")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_user_picks_themselves_sees_the_projects_and_a_board_with_their_own_cards_marked()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        var console = new List<string>();
        page.Console += (_, message) => console.Add(message.Text);

        // The start screen: five users, no password.
        await page.GotoAsync("/");
        await Assertions.Expect(page.Locator("button.user-button")).ToHaveCountAsync(5);
        await Assertions.Expect(page.Locator("input[type=password]")).ToHaveCountAsync(0);

        await ChooseUserAsync(page, "Priya Patel");
        await Assertions.Expect(page.GetByText("Acting as")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".current-user")).ToContainTextAsync("Priya Patel");

        // The three sample projects.
        foreach (var sample in new[] { "Mobile App Launch", "Website Redesign", "Internal Tools" })
        {
            await Assertions.Expect(page.Locator("a.project-name", new PageLocatorOptions { HasText = sample })).ToHaveCountAsync(1);
        }

        // A board: four columns in order, and Priya's own cards are marked in text as well as colour.
        await OpenBoardAsync(page, "Mobile App Launch");
        var headings = await page.Locator("section.board-column h2").AllInnerTextsAsync();
        Assert.Equal(["To Do", "In Progress", "In Review", "Done"], headings.Select(h => Regex.Replace(h, @"\s*\d+$", "").Trim()));
        await Assertions.Expect(page.Locator("article.card--mine")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".mine-label")).ToHaveTextAsync("Assigned to you");
        await Assertions.Expect(Card(page, "InProgress", "Implement push notifications")).ToHaveClassAsync(new Regex("card--mine"));

        // The page's own script and styles work under the Content Security Policy.
        Assert.DoesNotContain(console, line => line.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Switching_user_returns_to_the_selection_screen_and_the_new_choice_takes_effect()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await ChooseUserAsync(page, "Priya Patel");

        await page.Locator("a.switch-user").ClickAsync();
        await Assertions.Expect(page.Locator("button.user-button")).ToHaveCountAsync(5);

        await page.Locator("button.user-button", new PageLocatorOptions { HasText = "Jordan Lee" }).ClickAsync();
        await page.WaitForURLAsync("**/projects");
        await Assertions.Expect(page.Locator(".current-user")).ToContainTextAsync("Jordan Lee");
    }

    [Fact]
    public async Task Without_a_selected_user_a_project_address_sends_the_browser_to_the_selection_screen()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();

        await page.GotoAsync("/projects");

        await Assertions.Expect(page.Locator("button.user-button")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task Dragging_a_card_to_another_column_moves_it_within_a_second_and_it_stays_after_a_reload()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await ChooseUserAsync(page, "Priya Patel");
        await OpenBoardAsync(page, "Mobile App Launch");
        await Assertions.Expect(Card(page, "ToDo", Task)).ToBeVisibleAsync();

        try
        {
            await Card(page, "ToDo", Task).DragToAsync(Column(page, "InProgress"));

            // SC-002: the new column is shown within 1 second.
            await Assertions.Expect(Card(page, "InProgress", Task)).ToBeVisibleAsync(Quick);
            await Assertions.Expect(Card(page, "ToDo", Task)).ToHaveCountAsync(0);

            // The save happens in the background: wait for it before reloading, or the reload would abort it.
            var href = await Card(page, "InProgress", Task).Locator(".task-title a").GetAttributeAsync("href");
            await new SavedState(app).TaskIsInAsync(href!, "InProgress");

            await page.ReloadAsync();
            await Assertions.Expect(Card(page, "InProgress", Task)).ToBeVisibleAsync();
        }
        finally
        {
            await RestoreAsync(page, from: "InProgress", to: "To Do");
        }
    }

    [Fact]
    public async Task Dropping_a_card_in_its_own_column_changes_nothing_and_adds_no_history()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await ChooseUserAsync(page, "Priya Patel");
        await OpenBoardAsync(page, "Mobile App Launch");

        // The history is append-only, so other tests' moves of this card are still there: compare before and after.
        await Card(page, "ToDo", Task).Locator(".task-title a").ClickAsync();
        await Assertions.Expect(page.Locator(".status-history")).ToBeVisibleAsync();
        var entriesBefore = await page.Locator(".status-history li").CountAsync();
        await page.GoBackAsync();
        await Assertions.Expect(Column(page, "ToDo")).ToBeVisibleAsync();

        await Card(page, "ToDo", Task).DragToAsync(Column(page, "ToDo"));

        await Assertions.Expect(Card(page, "ToDo", Task)).ToBeVisibleAsync();
        await Card(page, "ToDo", Task).Locator(".task-title a").ClickAsync();
        await Assertions.Expect(page.Locator(".status-history")).ToBeVisibleAsync();
        Assert.Equal(entriesBefore, await page.Locator(".status-history li").CountAsync());
    }

    [Fact]
    public async Task The_keyboard_menu_moves_a_card_the_same_way_dragging_does_and_the_history_records_it()
    {
        await using var context = await browser.NewSessionAsync(app.GetUri("web"));
        var page = await context.NewPageAsync();
        await ChooseUserAsync(page, "Jordan Lee");
        await OpenBoardAsync(page, "Mobile App Launch");
        var toggle = Card(page, "ToDo", Task).Locator("button.move-toggle");

        try
        {
            // Keyboard only: focus the button, open the menu with Enter, step to "In Review", choose it with Enter.
            await toggle.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator("[role=menu]")).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Tab");
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(page.Locator("[role=menuitem]:focus")).ToHaveTextAsync("In Review");
            await page.Keyboard.PressAsync("Enter");

            await Assertions.Expect(Card(page, "InReview", Task)).ToBeVisibleAsync(Quick);
            var href = await Card(page, "InReview", Task).Locator(".task-title a").GetAttributeAsync("href");
            await new SavedState(app).TaskIsInAsync(href!, "InReview");

            // The history names who moved it, from where to where.
            await Card(page, "InReview", Task).Locator(".task-title a").ClickAsync();
            var entry = page.Locator(".status-history li").First;
            await Assertions.Expect(entry).ToContainTextAsync("Jordan Lee");
            await Assertions.Expect(entry).ToContainTextAsync("To Do");
            await Assertions.Expect(entry).ToContainTextAsync("In Review");
            await Assertions.Expect(entry.Locator("time")).ToContainTextAsync("UTC");
        }
        finally
        {
            await page.GotoAsync("/projects");
            await OpenBoardAsync(page, "Mobile App Launch");
            await RestoreAsync(page, from: "InReview", to: "To Do");
        }
    }

    /// <summary>Puts the sample card back in To Do using the menu, whatever column it is in.</summary>
    private static async Task RestoreAsync(IPage page, string from, string to)
    {
        var card = Card(page, from, Task);
        if (await card.CountAsync() == 0)
        {
            return;
        }

        await card.Locator("button.move-toggle").ClickAsync();
        await page.Locator("[role=menuitem]", new PageLocatorOptions { HasTextString = to }).ClickAsync();
        await Assertions.Expect(Card(page, "ToDo", Task)).ToBeVisibleAsync();
    }
}
