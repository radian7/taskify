using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Taskify.Contracts;
using Taskify.Notifications.Api.Hubs;
using Taskify.Notifications.Api.Validation;
using Taskify.Security.Audit;
using Taskify.UnitTests.Security;

namespace Taskify.UnitTests.Hubs;

/// <summary>
/// Argument validation of the board hub methods (constitution Principle II; contracts/realtime-hub.md). Entity IDs
/// must not be the empty GUID; user IDs must be one of the five predefined users. A refusal is a
/// <see cref="HubException"/> with the generic message "Invalid request" that never repeats the argument.
/// </summary>
public class BoardHubValidationTests
{
    private const string Generic = "Invalid request";

    private static readonly Guid SomeId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid Stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly RecordingAuditLogger audit = new();
    private readonly RecordingGroups groups = new();

    private BoardHub CreateHub()
    {
        var validator = new HubArgumentValidator(new FakeUserDirectory([.. SeedIds.AllUsers]), audit);
        return new BoardHub(validator) { Context = new FakeHubCallerContext(), Groups = groups };
    }

    public static TheoryData<string> EntityMethods() => ["JoinProject", "LeaveProject", "JoinTask", "LeaveTask"];

    public static TheoryData<string> UserMethods() => ["JoinUser", "LeaveUser"];

    private static Task Invoke(BoardHub hub, string method, Guid argument) => method switch
    {
        "JoinProject" => hub.JoinProject(argument),
        "LeaveProject" => hub.LeaveProject(argument),
        "JoinTask" => hub.JoinTask(argument),
        "LeaveTask" => hub.LeaveTask(argument),
        "JoinUser" => hub.JoinUser(argument),
        "LeaveUser" => hub.LeaveUser(argument),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    private static string GroupPrefix(string method) => method switch
    {
        "JoinProject" or "LeaveProject" => "project",
        "JoinTask" or "LeaveTask" => "task",
        _ => "user",
    };

    [Theory]
    [MemberData(nameof(EntityMethods))]
    public async Task Project_and_task_methods_accept_a_non_empty_GUID(string method)
    {
        await Invoke(CreateHub(), method, SomeId);

        var expected = $"{GroupPrefix(method)}:{SomeId}";
        Assert.Contains(groups.Calls, call => call.Group == expected && call.Added == method.StartsWith("Join", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EntityMethods))]
    public async Task Project_and_task_methods_reject_the_empty_GUID_with_a_generic_message(string method)
    {
        var exception = await Assert.ThrowsAsync<HubException>(() => Invoke(CreateHub(), method, Guid.Empty));

        Assert.Equal(Generic, exception.Message);
        Assert.Empty(groups.Calls);
    }

    [Theory]
    [MemberData(nameof(UserMethods))]
    public async Task User_methods_accept_each_of_the_five_seeded_users(string method)
    {
        foreach (var user in SeedIds.AllUsers)
        {
            await Invoke(CreateHub(), method, user);

            Assert.Contains(groups.Calls, call => call.Group == $"user:{user}");
        }
    }

    [Theory]
    [MemberData(nameof(UserMethods))]
    public async Task User_methods_reject_an_unknown_GUID_and_the_empty_GUID(string method)
    {
        foreach (var argument in new[] { Stranger, Guid.Empty })
        {
            var exception = await Assert.ThrowsAsync<HubException>(() => Invoke(CreateHub(), method, argument));

            Assert.Equal(Generic, exception.Message);
        }

        Assert.Empty(groups.Calls);
    }

    [Theory]
    [InlineData("JoinProject")]
    [InlineData("LeaveProject")]
    [InlineData("JoinTask")]
    [InlineData("LeaveTask")]
    [InlineData("JoinUser")]
    [InlineData("LeaveUser")]
    public async Task The_exception_message_never_contains_the_argument_value(string method)
    {
        // Guid.Empty is rejected by every method; the stranger by the user methods only.
        var arguments = method.EndsWith("User", StringComparison.Ordinal) ? new[] { Guid.Empty, Stranger } : [Guid.Empty];

        foreach (var argument in arguments)
        {
            var exception = await Assert.ThrowsAsync<HubException>(() => Invoke(CreateHub(), method, argument));

            Assert.DoesNotContain(argument.ToString(), exception.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(argument.ToString("N"), exception.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Null(exception.InnerException);
        }
    }

    [Fact]
    public async Task A_rejection_is_audited_without_the_argument()
    {
        await Assert.ThrowsAsync<HubException>(() => CreateHub().JoinUser(Stranger));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.HubRejected, entry.Action);
        Assert.Equal(AuditOutcome.Validation, entry.Outcome);
        Assert.DoesNotContain(Stranger.ToString(), entry.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public List<(string Group, bool Added)> Calls { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Calls.Add((groupName, true));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Calls.Add((groupName, false));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "test-connection";

        public override string? UserIdentifier => null;

        public override System.Security.Claims.ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
