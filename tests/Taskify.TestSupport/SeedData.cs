namespace Taskify.TestSupport;

/// <summary>
/// Names the xUnit collection for tests that read or change the sample tasks. Test classes in one collection run one
/// after another, so a test that moves a sample task cannot disturb a test that counts tasks per column.
/// </summary>
public static class SeedData
{
    /// <summary>The collection name.</summary>
    public const string Collection = "Sample tasks";
}
