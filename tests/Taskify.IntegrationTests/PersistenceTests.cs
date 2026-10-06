using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Taskify.Contracts;
using Taskify.TestSupport;

namespace Taskify.IntegrationTests;

/// <summary>
/// Persistence (spec SC-004): data survives a restart of the whole application when it keeps the same data volume.
/// </summary>
/// <remarks>
/// The test starts its own, second copy of the application with persistence on and a throwaway volume (never the
/// developer's <c>taskify-postgres-data</c>), writes data, shuts that copy down, starts a third with the same volume
/// and reads everything back. It then removes the volume. The shared application of the other tests is not touched.
/// </remarks>
public class PersistenceTests
{
    /// <summary>An application that keeps its database in the named volume.</summary>
    private sealed class PersistentApp(string volume) : TaskifyAppFixture
    {
        protected override IReadOnlyList<string> ExtraArguments => ["--Taskify:PersistData=true", $"--Taskify:DataVolumeName={volume}"];
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    [Fact]
    public async Task A_project_task_move_and_comment_survive_a_restart_with_the_same_data_volume()
    {
        var volume = "taskify-test-persist-" + Guid.NewGuid().ToString("N")[..12];
        var name = "Persisted " + Guid.NewGuid().ToString("N")[..8];
        const string CommentText = "A comment that must survive";
        Guid projectId;
        Guid taskId;
        Guid commentId;

        try
        {
            var first = new PersistentApp(volume);
            await first.InitializeAsync();
            try
            {
                var user = SeedIds.Priya;
                using var projects = first.CreateClient("projects-api", TaskifyAppFixture.WebKey, user);
                using var createdProject = await projects.PostAsync("/api/projects", Json($$"""{"name":"{{name}}"}"""), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Created, createdProject.StatusCode);
                projectId = (await ReadAsync(createdProject)).GetProperty("id").GetGuid();

                using var tasks = first.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user);
                using var createdTask = await tasks.PostAsync("/api/tasks", Json($$"""{"projectId":"{{projectId}}","title":"Persisted task","assigneeUserId":"{{SeedIds.Jordan}}"}"""), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Created, createdTask.StatusCode);
                taskId = (await ReadAsync(createdTask)).GetProperty("id").GetGuid();

                using var move = await tasks.PostAsync($"/api/tasks/{taskId}/moves", Json("""{"toStatus":"InProgress"}"""), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, move.StatusCode);

                using var comment = await tasks.PostAsync($"/api/tasks/{taskId}/comments", Json($$"""{"text":"{{CommentText}}"}"""), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
                commentId = (await ReadAsync(comment)).GetProperty("id").GetGuid();
            }
            finally
            {
                // Stops every container and project; the volume stays.
                await first.DisposeAsync();
            }

            var second = new PersistentApp(volume);
            await second.InitializeAsync();
            try
            {
                var user = SeedIds.Liam;
                using var projects = second.CreateClient("projects-api", TaskifyAppFixture.WebKey, user);
                using var tasks = second.CreateClient("tasks-api", TaskifyAppFixture.WebKey, user);

                var project = await ReadAsync(await projects.GetAsync($"/api/projects/{projectId}", TestContext.Current.CancellationToken));
                Assert.Equal(name, project.GetProperty("name").GetString());

                var task = await ReadAsync(await tasks.GetAsync($"/api/tasks/{taskId}", TestContext.Current.CancellationToken));
                Assert.Equal("Persisted task", task.GetProperty("title").GetString());
                Assert.Equal("InProgress", task.GetProperty("status").GetString());

                var history = await ReadAsync(await tasks.GetAsync($"/api/tasks/{taskId}/history", TestContext.Current.CancellationToken));
                var entry = history.EnumerateArray().Single();
                Assert.Equal("ToDo", entry.GetProperty("fromStatus").GetString());
                Assert.Equal("InProgress", entry.GetProperty("toStatus").GetString());

                var comments = await ReadAsync(await tasks.GetAsync($"/api/tasks/{taskId}/comments", TestContext.Current.CancellationToken));
                var stored = comments.EnumerateArray().Single();
                Assert.Equal(commentId, stored.GetProperty("id").GetGuid());
                Assert.Equal(CommentText, stored.GetProperty("text").GetString());

                // The sample data was not created a second time on top of the persisted data.
                var sample = await ReadAsync(await projects.GetAsync("/api/projects", TestContext.Current.CancellationToken));
                Assert.Single(sample.EnumerateArray(), p => p.GetProperty("id").GetGuid() == SeedIds.MobileAppLaunch);
            }
            finally
            {
                await second.DisposeAsync();
            }
        }
        finally
        {
            RemoveVolume(volume);
        }
    }

    /// <summary>Removes the throwaway volume. Best effort: a leftover volume only costs disk space.</summary>
    private static void RemoveVolume(string volume)
    {
        var runtime = Environment.GetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME") is { Length: > 0 } configured ? configured : "docker";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(runtime, ["volume", "rm", "--force", volume])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The runtime executable is not on the PATH.
        }
    }
}
