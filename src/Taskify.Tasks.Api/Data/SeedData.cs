using Taskify.Contracts;
using Taskify.Tasks.Api.Domain;

namespace Taskify.Tasks.Api.Data;

/// <summary>
/// The sample tasks (spec FR-005): 10 per sample project, at least 2 in every column, assigned to a mix of the five
/// users, with some unassigned. They exist the first time Taskify is opened so the board can be demonstrated at once.
/// </summary>
public static class SeedData
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the sample tasks. IDs and times are fixed, so the result is the same on every run.</summary>
    /// <returns>All 30 sample tasks.</returns>
    public static IReadOnlyList<TaskItem> Tasks()
    {
        var maya = SeedIds.Maya;
        var liam = SeedIds.Liam;
        var priya = SeedIds.Priya;
        var tomasz = SeedIds.Tomasz;
        var jordan = SeedIds.Jordan;
        Guid? nobody = null;

        // (title, column, assignee). Within a project the order below is the creation order, oldest first.
        var mobile = new (string Title, TaskStatus Status, Guid? Assignee)[]
        {
            ("Set up CI for iOS builds", TaskStatus.Done, liam),
            ("Design app icon", TaskStatus.Done, jordan),
            ("Create release checklist", TaskStatus.Done, nobody),
            ("Implement push notifications", TaskStatus.InProgress, priya),
            ("Build offline mode", TaskStatus.InProgress, tomasz),
            ("Login screen accessibility fixes", TaskStatus.InReview, jordan),
            ("Onboarding copy review", TaskStatus.InReview, maya),
            ("Write App Store description", TaskStatus.ToDo, maya),
            ("Plan beta tester outreach", TaskStatus.ToDo, nobody),
            ("Define crash reporting thresholds", TaskStatus.ToDo, liam),
        };

        var website = new (string Title, TaskStatus Status, Guid? Assignee)[]
        {
            ("Choose design system", TaskStatus.Done, maya),
            ("Set up staging site", TaskStatus.Done, tomasz),
            ("Redirect map for old URLs", TaskStatus.Done, priya),
            ("Implement responsive navigation", TaskStatus.InProgress, jordan),
            ("Migrate blog to new layout", TaskStatus.InProgress, priya),
            ("Homepage hero section", TaskStatus.InReview, liam),
            ("Cookie banner wording", TaskStatus.InReview, nobody),
            ("Audit current page speed", TaskStatus.ToDo, tomasz),
            ("Collect customer testimonials", TaskStatus.ToDo, nobody),
            ("Draft new pricing page", TaskStatus.ToDo, maya),
        };

        var tools = new (string Title, TaskStatus Status, Guid? Assignee)[]
        {
            ("Set up team chat channels", TaskStatus.Done, liam),
            ("Inventory of software licenses", TaskStatus.Done, jordan),
            ("Create IT help page", TaskStatus.Done, nobody),
            ("Build team calendar integration", TaskStatus.InProgress, tomasz),
            ("Clean up shared drive", TaskStatus.InProgress, maya),
            ("Expense report template", TaskStatus.InReview, priya),
            ("Access request process", TaskStatus.InReview, nobody),
            ("Evaluate time-tracking tools", TaskStatus.ToDo, nobody),
            ("Write onboarding guide for new hires", TaskStatus.ToDo, jordan),
            ("Automate weekly status report", TaskStatus.ToDo, liam),
        };

        return Build(SeedIds.MobileAppLaunch, 1, mobile)
            .Concat(Build(SeedIds.WebsiteRedesign, 2, website))
            .Concat(Build(SeedIds.InternalTools, 3, tools))
            .ToList();
    }

    private static IEnumerable<TaskItem> Build(Guid projectId, int projectNumber, (string Title, TaskStatus Status, Guid? Assignee)[] rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            // Fixed IDs of the form 33333333-3333-3333-000P-0000000000NN, so migrations are stable.
            var id = new Guid($"33333333-3333-3333-{projectNumber:D4}-{i + 1:D12}");

            // Three hours apart, so "newest first" within a column is well defined. Everything was created by the Product Manager.
            var createdAt = BaseTime.AddDays(projectNumber * 7).AddHours(i * 3);
            yield return new TaskItem(id, projectId, rows[i].Title, description: null, rows[i].Status, rows[i].Assignee, SeedIds.Maya, createdAt);
        }
    }
}
