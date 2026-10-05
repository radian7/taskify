using System.Text.Json;
using Taskify.Contracts;
using Taskify.Tasks.Api.Endpoints;
using Taskify.Tasks.Api.Validation;

namespace Taskify.UnitTests.Validation;

/// <summary>
/// Validation of new and edited tasks (spec FR-009 to FR-011, FR-019, FR-021; constitution Principle II). Lengths count
/// user-perceived characters after trimming. Whether a project or user exists is checked by the endpoint (422).
/// </summary>
public class TaskValidatorTests
{
    // 👨‍👩‍👧‍👦: one user-perceived character made of 11 UTF-16 code units.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    private static readonly CreateTaskValidator Create = new();
    private static readonly UpdateTaskValidator Update = new();
    private static readonly Guid Project = SeedIds.MobileAppLaunch;

    private static T Bind<T>(string json) => JsonSerializer.Deserialize<T>(json, ContractJson.Options)!;

    private static CreateTaskRequest NewTask(string title = "Design login", string? description = null, Guid? assignee = null, Guid? project = null) =>
        new(project ?? Project, title, description, assignee);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void A_title_of_one_to_two_hundred_characters_is_accepted(int length) =>
        Assert.True(Create.Validate(NewTask(new string('t', length))).IsValid);

    [Theory]
    [InlineData(201)]
    [InlineData(1000)]
    public void A_title_over_two_hundred_characters_is_rejected(int length)
    {
        var result = Create.Validate(NewTask(new string('t', length)));

        Assert.Equal("Title", Assert.Single(result.Errors).PropertyName);
        Assert.Equal("Title must be between 1 and 200 characters.", result.Errors[0].ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void An_empty_or_whitespace_only_title_is_rejected(string title) =>
        Assert.False(Create.Validate(NewTask(title)).IsValid);

    [Fact]
    public void Exactly_two_hundred_emoji_are_accepted_and_two_hundred_and_one_are_not()
    {
        Assert.True(Create.Validate(NewTask(string.Concat(Enumerable.Repeat(FamilyEmoji, 200)))).IsValid);
        Assert.False(Create.Validate(NewTask(string.Concat(Enumerable.Repeat(FamilyEmoji, 201)))).IsValid);
    }

    [Fact]
    public void A_title_stuffed_with_combining_marks_is_rejected_by_the_abuse_guard()
    {
        var zalgo = "e" + new string('́', 3300);

        Assert.False(Create.Validate(NewTask(zalgo)).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(4999, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void A_description_is_optional_and_limited_to_five_thousand_characters(object? description, bool accepted)
    {
        var text = description switch
        {
            int length => new string('d', length),
            string value => value,
            _ => null,
        };

        Assert.Equal(accepted, Create.Validate(NewTask(description: text)).IsValid);
    }

    [Fact]
    public void A_task_needs_a_real_project_id()
    {
        var result = Create.Validate(NewTask(project: Guid.Empty));

        Assert.Equal("ProjectId", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void A_task_may_be_unassigned_or_assigned_to_a_user_id()
    {
        Assert.True(Create.Validate(NewTask(assignee: null)).IsValid);
        Assert.True(Create.Validate(NewTask(assignee: SeedIds.Jordan)).IsValid);
    }

    [Fact]
    public void The_empty_guid_is_never_a_valid_assignee()
    {
        var result = Create.Validate(NewTask(assignee: Guid.Empty));

        Assert.Equal("AssigneeUserId", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var result = Create.Validate(new CreateTaskRequest(Guid.Empty, string.Empty, new string('d', 5001), Guid.Empty));

        Assert.Equal(["AssigneeUserId", "Description", "ProjectId", "Title"], result.Errors.Select(e => e.PropertyName).Order());
    }

    [Fact]
    public void Error_messages_never_repeat_the_rejected_text()
    {
        var messages = Create.Validate(NewTask("SECRET-" + new string('x', 300))).Errors.Select(e => e.ErrorMessage);

        Assert.DoesNotContain("SECRET", string.Concat(messages), StringComparison.Ordinal);
    }

    [Fact]
    public void A_create_body_needs_only_a_project_and_a_title()
    {
        var request = Bind<CreateTaskRequest>($$"""{"projectId":"{{Project}}","title":"Design login"}""");

        Assert.Null(request.Description);
        Assert.Null(request.AssigneeUserId);
        Assert.True(Create.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("""{"title":"no project"}""")]
    [InlineData("""{"projectId":"11111111-1111-1111-1111-000000000001"}""")]
    [InlineData("""{"projectId":"not-a-guid","title":"x"}""")]
    [InlineData("""{"projectId":"11111111-1111-1111-1111-000000000001","title":null}""")]
    [InlineData("""{"projectId":"11111111-1111-1111-1111-000000000001","title":"x","status":"Done"}""")]
    [InlineData("""{"projectId":"11111111-1111-1111-1111-000000000001","title":"x","assigneeUserId":"nope"}""")]
    [InlineData("""{"projectId":"11111111-1111-1111-1111-000000000001","title":"x","createdByUserId":"11111111-1111-1111-1111-000000000002"}""")]
    public void A_create_body_that_does_not_match_the_contract_is_rejected_before_validation(string json) =>
        Assert.ThrowsAny<JsonException>(() => Bind<CreateTaskRequest>(json));

    [Fact]
    public void An_edit_replaces_title_and_assignee_and_the_description_is_optional()
    {
        var request = Bind<UpdateTaskRequest>($$"""{"title":"New title","assigneeUserId":"{{SeedIds.Liam}}"}""");

        Assert.Equal("New title", request.Title);
        Assert.Equal(SeedIds.Liam, request.AssigneeUserId);
        Assert.Null(request.Description);
        Assert.True(Update.Validate(request).IsValid);
    }

    [Fact]
    public void An_edit_can_unassign_a_task_with_an_explicit_null()
    {
        var request = Bind<UpdateTaskRequest>("""{"title":"New title","assigneeUserId":null}""");

        Assert.Null(request.AssigneeUserId);
        Assert.True(Update.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("""{"title":"New title"}""")]
    [InlineData("""{"assigneeUserId":null}""")]
    [InlineData("""{"title":null,"assigneeUserId":null}""")]
    [InlineData("""{"title":"x","assigneeUserId":null,"projectId":"11111111-1111-1111-1111-000000000001"}""")]
    [InlineData("""{"title":"x","assigneeUserId":null,"status":"Done"}""")]
    public void An_edit_body_must_carry_title_and_assignee_and_nothing_extra(string json) =>
        Assert.ThrowsAny<JsonException>(() => Bind<UpdateTaskRequest>(json));

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a", true)]
    public void An_edit_follows_the_same_title_rules_as_creation(string title, bool accepted) =>
        Assert.Equal(accepted, Update.Validate(new UpdateTaskRequest(title, null)).IsValid);

    [Fact]
    public void An_edit_follows_the_same_length_limits_as_creation()
    {
        Assert.False(Update.Validate(new UpdateTaskRequest(new string('t', 201), null)).IsValid);
        Assert.False(Update.Validate(new UpdateTaskRequest("ok", null, new string('d', 5001))).IsValid);
        Assert.True(Update.Validate(new UpdateTaskRequest(new string('t', 200), null, new string('d', 5000))).IsValid);
    }

    [Fact]
    public void An_edit_cannot_name_the_empty_guid_as_assignee() =>
        Assert.False(Update.Validate(new UpdateTaskRequest("ok", Guid.Empty)).IsValid);
}
