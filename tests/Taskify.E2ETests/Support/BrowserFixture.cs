using Microsoft.Playwright;

namespace Taskify.E2ETests;

/// <summary>
/// Starts one headless browser for the whole run. By default it drives the Google Chrome that is installed on the
/// machine, so nothing needs downloading. Set <c>TASKIFY_E2E_BROWSER</c> to <c>msedge</c> for Edge, or to
/// <c>chromium</c> for Playwright's own build (install it first: <c>pwsh bin/Debug/net10.0/playwright.ps1 install chromium</c>).
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? playwright;

    /// <summary>Gets the browser.</summary>
    public IBrowser Browser { get; private set; } = default!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();

        var channel = Environment.GetEnvironmentVariable("TASKIFY_E2E_BROWSER") ?? "chrome";
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Channel = channel == "chromium" ? null : channel,
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        playwright?.Dispose();
    }

    /// <summary>
    /// Opens a fresh browser session (its own cookies) on the Web app. The localhost development certificate is
    /// accepted, because the app runs on localhost with it.
    /// </summary>
    /// <param name="webAddress">The Web app's HTTPS address.</param>
    /// <returns>The new context. The caller disposes it.</returns>
    public Task<IBrowserContext> NewSessionAsync(Uri webAddress) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = webAddress.ToString(),
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });
}
