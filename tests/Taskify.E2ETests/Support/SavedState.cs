using System.Text.Json;
using Taskify.TestSupport;

namespace Taskify.E2ETests;

/// <summary>
/// Waits until a change made in the browser has really been saved. The board moves a card at once and saves in the
/// background (optimistic UI, research R6), so a test that reloads straight after a drag would abort the save.
/// </summary>
/// <param name="app">The running application.</param>
public sealed class SavedState(TaskifyAppFixture app)
{
    /// <summary>Waits until a task is in the given column, according to the Tasks API.</summary>
    /// <param name="taskHref">The task's link (<c>/projects/{projectId}/tasks/{taskId}</c>).</param>
    /// <param name="status">The expected column, for example <c>InProgress</c>.</param>
    /// <returns>A task that completes when the API reports the column.</returns>
    public async Task TaskIsInAsync(string taskHref, string status)
    {
        var taskId = Guid.Parse(taskHref[(taskHref.LastIndexOf('/') + 1)..]);
        using var client = app.CreateClient("tasks-api", TaskifyAppFixture.WebKey, app.NextUser());

        var deadline = DateTime.UtcNow.AddSeconds(10);
        string? current = null;
        while (DateTime.UtcNow < deadline)
        {
            using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/tasks/{taskId}", TestContext.Current.CancellationToken));
            current = document.RootElement.GetProperty("status").GetString();
            if (current == status)
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"The task is still in {current} after 10 seconds; expected {status}.");
    }
}
