using System.Text.Json;
using Taskify.Contracts;
using Taskify.Contracts.Events;
using Taskify.Notifications.Api.Validation;
using Taskify.UnitTests.Security;

namespace Taskify.UnitTests.Validation;

/// <summary>
/// Validation of domain events received on <c>/internal/events</c> (spec FR-019, FR-022; constitution Principle II:
/// internal origin does not imply trust). Every field is allow-listed: the eight known types, version 1, exact
/// field names, UUID IDs, existing users, known statuses and text within the length rules.
/// </summary>
public class EventValidatorTests
{
    private const string Stranger = "99999999-9999-9999-9999-999999999999";

    private static readonly Guid EventId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TaskId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid ProjectId = SeedIds.MobileAppLaunch;
    private static readonly Guid CommentId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly EventEnvelopeValidator Validator = new(new FakeUserDirectory([.. SeedIds.AllUsers]));

    public static TheoryData<string> AllTypes() => [.. EventTypes.All];

    private static string Payload(string type) => type switch
    {
        EventTypes.ProjectCreated => $$"""{"projectId":"{{ProjectId}}","name":"Launch"}""",
        EventTypes.TaskCreated => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","title":"Fix it","status":"ToDo","assigneeUserId":"{{SeedIds.Liam}}"}""",
        EventTypes.TaskUpdated => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","title":"Fix it"}""",
        EventTypes.TaskAssigned => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","title":"Fix it","previousAssigneeUserId":null,"assigneeUserId":"{{SeedIds.Liam}}"}""",
        EventTypes.TaskMoved => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","title":"Fix it","fromStatus":"ToDo","toStatus":"InProgress","assigneeUserId":null}""",
        EventTypes.CommentAdded => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","title":"Fix it","commentId":"{{CommentId}}","assigneeUserId":"{{SeedIds.Liam}}"}""",
        EventTypes.CommentEdited => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","commentId":"{{CommentId}}"}""",
        EventTypes.CommentDeleted => $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","commentId":"{{CommentId}}"}""",
        _ => "{}",
    };

    private static string Envelope(
        string type,
        string? payload = null,
        string version = "1",
        string? eventId = null,
        string? actor = null,
        string extra = "") =>
        $$"""{"eventId":{{eventId ?? $"\"{EventId}\""}},"type":"{{type}}","version":{{version}},"occurredAt":"2026-01-02T03:04:05Z","actorUserId":{{actor ?? $"\"{SeedIds.Priya}\""}},"payload":{{payload ?? Payload(type)}}{{extra}}}""";

    private static string TitlePayload(string title) =>
        $$"""{"taskId":"{{TaskId}}","projectId":"{{ProjectId}}","title":{{JsonSerializer.Serialize(title)}}}""";

    private static string NamePayload(string name) =>
        $$"""{"projectId":"{{ProjectId}}","name":{{JsonSerializer.Serialize(name)}}}""";

    private static async Task<bool> IsValidAsync(string json) => (await Validator.ValidateAsync(json)).IsValid;

    private static string Without(string json, string field)
    {
        using var document = JsonDocument.Parse(json);
        var rest = document.RootElement.EnumerateObject()
            .Where(property => property.Name != field)
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        return JsonSerializer.Serialize(rest);
    }

    private static string With(string json, string field, string rawValue)
    {
        using var document = JsonDocument.Parse(json);
        var all = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        all[field] = JsonDocument.Parse(rawValue).RootElement.Clone();
        return JsonSerializer.Serialize(all);
    }

    [Fact]
    public void The_sample_payloads_match_the_contract_records()
    {
        // Guards the tests themselves: each sample must bind to the record the contract names.
        Assert.NotNull(JsonSerializer.Deserialize<ProjectCreatedPayload>(Payload(EventTypes.ProjectCreated), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<TaskCreatedPayload>(Payload(EventTypes.TaskCreated), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<TaskUpdatedPayload>(Payload(EventTypes.TaskUpdated), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<TaskAssignedPayload>(Payload(EventTypes.TaskAssigned), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<TaskMovedPayload>(Payload(EventTypes.TaskMoved), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<CommentAddedPayload>(Payload(EventTypes.CommentAdded), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<CommentEditedPayload>(Payload(EventTypes.CommentEdited), ContractJson.Options));
        Assert.NotNull(JsonSerializer.Deserialize<CommentDeletedPayload>(Payload(EventTypes.CommentDeleted), ContractJson.Options));
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public async Task A_valid_event_of_each_type_is_accepted(string type)
    {
        Assert.True(await IsValidAsync(Envelope(type)));
    }

    [Theory]
    [InlineData("TaskDeleted")]
    [InlineData("taskmoved")]
    [InlineData("")]
    public async Task An_unknown_type_is_rejected(string type)
    {
        Assert.False(await IsValidAsync(Envelope(type, Payload(EventTypes.TaskMoved))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("null")]
    public async Task A_version_other_than_1_is_rejected(string version)
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, version: version)));
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public async Task An_unknown_payload_field_is_rejected(string type)
    {
        var withExtra = With(Payload(type), "description", "\"secret text\"");

        Assert.False(await IsValidAsync(Envelope(type, withExtra)));
    }

    [Fact]
    public async Task An_unknown_envelope_field_is_rejected()
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, extra: ",\"extra\":1")));
    }

    [Theory]
    [InlineData(EventTypes.ProjectCreated, "projectId")]
    [InlineData(EventTypes.ProjectCreated, "name")]
    [InlineData(EventTypes.TaskCreated, "taskId")]
    [InlineData(EventTypes.TaskCreated, "title")]
    [InlineData(EventTypes.TaskCreated, "status")]
    [InlineData(EventTypes.TaskUpdated, "projectId")]
    [InlineData(EventTypes.TaskAssigned, "title")]
    [InlineData(EventTypes.TaskMoved, "fromStatus")]
    [InlineData(EventTypes.TaskMoved, "toStatus")]
    [InlineData(EventTypes.CommentAdded, "commentId")]
    [InlineData(EventTypes.CommentEdited, "commentId")]
    [InlineData(EventTypes.CommentDeleted, "taskId")]
    public async Task A_missing_required_payload_field_is_rejected(string type, string field)
    {
        Assert.False(await IsValidAsync(Envelope(type, Without(Payload(type), field))));
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("type")]
    [InlineData("version")]
    [InlineData("occurredAt")]
    [InlineData("actorUserId")]
    [InlineData("payload")]
    public async Task A_missing_envelope_field_is_rejected(string field)
    {
        Assert.False(await IsValidAsync(Without(Envelope(EventTypes.TaskMoved), field)));
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("")]
    [InlineData("12345")]
    public async Task A_non_UUID_event_or_actor_ID_is_rejected(string value)
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, eventId: JsonSerializer.Serialize(value))));
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, actor: JsonSerializer.Serialize(value))));
    }

    [Theory]
    [InlineData(EventTypes.ProjectCreated, "projectId")]
    [InlineData(EventTypes.TaskCreated, "taskId")]
    [InlineData(EventTypes.TaskCreated, "projectId")]
    [InlineData(EventTypes.TaskCreated, "assigneeUserId")]
    [InlineData(EventTypes.TaskAssigned, "assigneeUserId")]
    [InlineData(EventTypes.TaskMoved, "taskId")]
    [InlineData(EventTypes.CommentAdded, "commentId")]
    [InlineData(EventTypes.CommentEdited, "commentId")]
    [InlineData(EventTypes.CommentDeleted, "taskId")]
    public async Task A_non_UUID_payload_ID_is_rejected(string type, string field)
    {
        Assert.False(await IsValidAsync(Envelope(type, With(Payload(type), field, "\"not-a-uuid\""))));
    }

    [Fact]
    public async Task An_actor_or_assignee_who_is_not_a_known_user_is_rejected()
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, actor: $"\"{Stranger}\"")));

        Assert.False(await IsValidAsync(Envelope(
            EventTypes.TaskCreated, With(Payload(EventTypes.TaskCreated), "assigneeUserId", $"\"{Stranger}\""))));
    }

    [Fact]
    public async Task A_title_of_200_characters_is_accepted_and_201_is_rejected()
    {
        Assert.True(await IsValidAsync(Envelope(EventTypes.TaskUpdated, TitlePayload(new string('a', 200)))));
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskUpdated, TitlePayload(new string('a', 201)))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_title_is_rejected(string title)
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskUpdated, TitlePayload(title))));
    }

    [Fact]
    public async Task A_title_over_the_abuse_guard_is_rejected_even_when_it_is_one_grapheme()
    {
        // One letter followed by 3,200 combining marks counts as 1 grapheme but is 3,201 UTF-16 code units, over the
        // guard of 16 per allowed character (200 x 16 = 3,200).
        var title = "a" + new string('́', 3200);

        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskUpdated, TitlePayload(title))));
    }

    [Fact]
    public async Task A_project_name_of_100_characters_is_accepted_and_101_is_rejected()
    {
        Assert.True(await IsValidAsync(Envelope(EventTypes.ProjectCreated, NamePayload(new string('p', 100)))));
        Assert.False(await IsValidAsync(Envelope(EventTypes.ProjectCreated, NamePayload(new string('p', 101)))));
        Assert.False(await IsValidAsync(Envelope(EventTypes.ProjectCreated, NamePayload(string.Empty))));
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("todo")]
    [InlineData("1")]
    [InlineData("")]
    public async Task An_unknown_status_is_rejected(string status)
    {
        var quoted = JsonSerializer.Serialize(status);

        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskCreated, With(Payload(EventTypes.TaskCreated), "status", quoted))));
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, With(Payload(EventTypes.TaskMoved), "toStatus", quoted))));
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, With(Payload(EventTypes.TaskMoved), "fromStatus", quoted))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{")]
    public async Task A_body_that_is_not_a_JSON_object_is_rejected(string body)
    {
        Assert.False(await IsValidAsync(body));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    public async Task A_payload_that_is_not_a_complete_object_is_rejected(string payload)
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, payload)));
    }

    [Fact]
    public async Task A_payload_of_another_type_is_rejected()
    {
        Assert.False(await IsValidAsync(Envelope(EventTypes.TaskMoved, Payload(EventTypes.ProjectCreated))));
    }
}
