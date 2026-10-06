using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.Contracts.Hub;
using Taskify.IntegrationTests.Infrastructure;

namespace Taskify.IntegrationTests.Contracts;

/// <summary>
/// Checks that every event and hub message record in <c>Taskify.Contracts</c> matches its schema in
/// <c>contracts/events.asyncapi.yaml</c> (research R15; constitution Principle III). Each record is serialized as a fully
/// populated sample and as a minimal one (optional fields left out); both must validate, and an unknown field must not.
/// These tests read files only and do not need the running application.
/// </summary>
public class AsyncApiContractTests
{
    private static readonly Guid Event = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid CommentId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Actor = SeedIds.Priya;
    private static readonly Guid Project = SeedIds.MobileAppLaunch;
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Full = ContractJson.Options;

    // The minimal sample omits null optional fields instead of writing "field": null.
    private static readonly JsonSerializerOptions Minimal = CreateMinimal();

    private static JsonSerializerOptions CreateMinimal()
    {
        var options = ContractJson.Create();
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }

    /// <summary>One sample: the message name, then the serialized full and minimal forms.</summary>
    public sealed record Sample(string Message, string FullJson, string MinimalJson)
    {
        /// <inheritdoc />
        public override string ToString() => Message;
    }

    private static Sample Event_<T>(string type, T full, T minimal) =>
        new(type, Envelope(type, full, Full), Envelope(type, minimal, Minimal));

    private static string Envelope<T>(string type, T payload, JsonSerializerOptions options)
    {
        var envelope = new EventEnvelope(Event, type, EventEnvelope.CurrentVersion, Now, Actor, JsonSerializer.SerializeToElement(payload, options));
        return JsonSerializer.Serialize(envelope, options);
    }

    private static Sample Message<T>(string name, T full, T minimal) =>
        new(name, JsonSerializer.Serialize(full, Full), JsonSerializer.Serialize(minimal, Minimal));

    private static readonly Sample[] EventSamples =
    [
        Event_(EventTypes.ProjectCreated,
            new ProjectCreatedPayload(Project, "Launch"),
            new ProjectCreatedPayload(Project, "L")),
        Event_(EventTypes.TaskCreated,
            new TaskCreatedPayload(TaskId, Project, "Fix it", TaskStatus.ToDo, SeedIds.Liam),
            new TaskCreatedPayload(TaskId, Project, "F", TaskStatus.ToDo, null)),
        Event_(EventTypes.TaskUpdated,
            new TaskUpdatedPayload(TaskId, Project, "Fix it"),
            new TaskUpdatedPayload(TaskId, Project, "F")),
        Event_(EventTypes.TaskAssigned,
            new TaskAssignedPayload(TaskId, Project, "Fix it", SeedIds.Maya, SeedIds.Liam),
            new TaskAssignedPayload(TaskId, Project, "F", null, null)),
        Event_(EventTypes.TaskMoved,
            new TaskMovedPayload(TaskId, Project, "Fix it", TaskStatus.ToDo, TaskStatus.Done, SeedIds.Liam),
            new TaskMovedPayload(TaskId, Project, "F", TaskStatus.InProgress, TaskStatus.InReview, null)),
        Event_(EventTypes.CommentAdded,
            new CommentAddedPayload(TaskId, Project, "Fix it", CommentId, SeedIds.Liam),
            new CommentAddedPayload(TaskId, Project, "F", CommentId, null)),
        Event_(EventTypes.CommentEdited,
            new CommentEditedPayload(TaskId, Project, CommentId),
            new CommentEditedPayload(TaskId, Project, CommentId)),
        Event_(EventTypes.CommentDeleted,
            new CommentDeletedPayload(TaskId, Project, CommentId),
            new CommentDeletedPayload(TaskId, Project, CommentId)),
    ];

    private static readonly Sample[] HubSamples =
    [
        Message(nameof(BoardChanged),
            new BoardChanged(Event, EventTypes.TaskMoved, Project, TaskId, Actor, Now),
            new BoardChanged(Event, EventTypes.TaskCreated, Project, null, Actor, Now)),
        Message(nameof(TaskChanged),
            new TaskChanged(Event, EventTypes.CommentEdited, TaskId, Actor, Now),
            new TaskChanged(Event, EventTypes.TaskUpdated, TaskId, Actor, Now)),
        Message(nameof(ProjectListChanged),
            new ProjectListChanged(Event, Project),
            new ProjectListChanged(Event, Project)),
        Message("NotificationCreated",
            new NotificationDto(Event, NotificationType.TaskCommented, TaskId, Project, Actor, "Liam commented on Fix it", Now, true),
            new NotificationDto(Event, NotificationType.TaskAssigned, TaskId, Project, Actor, "S", Now, false)),
    ];

    public static TheoryData<Sample> Events() => [.. EventSamples];

    public static TheoryData<Sample> HubMessages() => [.. HubSamples];

    public static TheoryData<string> HubMethods() => ["JoinProject", "LeaveProject", "JoinTask", "LeaveTask", "JoinUser", "LeaveUser"];

    private static void AssertValid(string message, string json)
    {
        var result = AsyncApiSchemas.Validate(message, json);
        Assert.True(result.IsValid, $"{message} should be valid but is not: {json}\n{Describe(result)}");
    }

    private static void AssertInvalid(string message, string json) =>
        Assert.False(AsyncApiSchemas.Validate(message, json).IsValid, $"{message} should be rejected but is valid: {json}");

    private static string Describe(Json.Schema.EvaluationResults result) =>
        string.Join("; ", (result.Details ?? []).Where(d => !d.IsValid && d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} {e.Key}: {e.Value}")));

    private static string WithExtraField(string json, bool insidePayload)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var target = insidePayload ? node["payload"]!.AsObject() : node;
        target["unexpected"] = 1;
        return node.ToJsonString();
    }

    [Fact]
    public void The_document_describes_the_eight_events_the_four_hub_messages_and_the_six_hub_commands()
    {
        var expected = EventTypes.All
            .Concat(["BoardChanged", "TaskChanged", "ProjectListChanged", "NotificationCreated"])
            .Concat(["JoinProject", "LeaveProject", "JoinTask", "LeaveTask", "JoinUser", "LeaveUser"]);

        Assert.Equal(expected.Order(StringComparer.Ordinal), AsyncApiSchemas.MessageNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void There_is_a_sample_for_each_event_type_and_hub_message()
    {
        Assert.Equal(EventTypes.All.Order(StringComparer.Ordinal), EventSamples.Select(s => s.Message).Order(StringComparer.Ordinal));
        Assert.Equal(4, HubSamples.Length);
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void A_fully_populated_event_matches_its_schema(Sample sample) => AssertValid(sample.Message, sample.FullJson);

    [Theory]
    [MemberData(nameof(Events))]
    public void A_minimal_event_matches_its_schema(Sample sample) => AssertValid(sample.Message, sample.MinimalJson);

    [Theory]
    [MemberData(nameof(Events))]
    public void An_event_with_an_unknown_envelope_field_is_rejected(Sample sample) =>
        AssertInvalid(sample.Message, WithExtraField(sample.FullJson, insidePayload: false));

    [Theory]
    [MemberData(nameof(Events))]
    public void An_event_with_an_unknown_payload_field_is_rejected(Sample sample) =>
        AssertInvalid(sample.Message, WithExtraField(sample.FullJson, insidePayload: true));

    [Fact]
    public void An_event_whose_type_does_not_match_its_message_is_rejected()
    {
        var moved = EventSamples.Single(s => s.Message == EventTypes.TaskMoved);

        AssertInvalid(EventTypes.TaskUpdated, moved.FullJson);
    }

    [Theory]
    [MemberData(nameof(HubMessages))]
    public void A_fully_populated_hub_message_matches_its_schema(Sample sample) => AssertValid(sample.Message, sample.FullJson);

    [Theory]
    [MemberData(nameof(HubMessages))]
    public void A_minimal_hub_message_matches_its_schema(Sample sample) => AssertValid(sample.Message, sample.MinimalJson);

    [Theory]
    [MemberData(nameof(HubMessages))]
    public void A_hub_message_with_an_unknown_field_is_rejected(Sample sample) =>
        AssertInvalid(sample.Message, WithExtraField(sample.FullJson, insidePayload: false));

    [Theory]
    [MemberData(nameof(HubMethods))]
    public void A_hub_method_argument_with_a_non_empty_GUID_is_valid(string method) =>
        AssertValid(method, $"\"{TaskId}\"");

    [Theory]
    [MemberData(nameof(HubMethods))]
    public void A_hub_method_argument_with_the_empty_GUID_is_rejected(string method) =>
        AssertInvalid(method, $"\"{Guid.Empty}\"");

    [Theory]
    [MemberData(nameof(HubMethods))]
    public void A_hub_method_argument_that_is_not_a_UUID_is_rejected(string method)
    {
        AssertInvalid(method, "\"not-a-uuid\"");
        AssertInvalid(method, "\"\"");
        AssertInvalid(method, "42");
        AssertInvalid(method, "null");
    }
}
