using System.Text.Json;
using Taskify.Contracts;
using Taskify.Projects.Api.Endpoints;
using Taskify.Projects.Api.Validation;

namespace Taskify.UnitTests.Validation;

/// <summary>
/// Validation of a new project (spec FR-006, FR-019; constitution Principle II: explicit limits, with tests for accepted
/// and rejected input). Lengths count user-perceived characters after trimming.
/// </summary>
public class CreateProjectValidatorTests
{
    // 👨‍👩‍👧‍👦: one user-perceived character made of 11 UTF-16 code units.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    private static readonly CreateProjectValidator Validator = new();

    private static CreateProjectRequest Bind(string json) =>
        JsonSerializer.Deserialize<CreateProjectRequest>(json, ContractJson.Options)!;

    private static CreateProjectRequest Request(string name, string? description = null) => new(name, description);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(100)]
    public void A_name_of_one_to_a_hundred_characters_is_accepted(int length) =>
        Assert.True(Validator.Validate(Request(new string('a', length))).IsValid);

    [Theory]
    [InlineData(101)]
    [InlineData(500)]
    [InlineData(5000)]
    public void A_name_over_a_hundred_characters_is_rejected(int length)
    {
        var result = Validator.Validate(Request(new string('a', length)));

        Assert.False(result.IsValid);
        Assert.Equal("Name", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("      ")]
    [InlineData("\t\r\n")]
    [InlineData("  ")]
    public void An_empty_or_whitespace_only_name_is_rejected(string name) =>
        Assert.False(Validator.Validate(Request(name)).IsValid);

    [Fact]
    public void Whitespace_around_a_name_does_not_count_towards_the_limit() =>
        Assert.True(Validator.Validate(Request("   " + new string('a', 100) + "   ")).IsValid);

    [Fact]
    public void Exactly_a_hundred_emoji_are_accepted_and_a_hundred_and_one_are_not()
    {
        Assert.True(Validator.Validate(Request(string.Concat(Enumerable.Repeat(FamilyEmoji, 100)))).IsValid);
        Assert.False(Validator.Validate(Request(string.Concat(Enumerable.Repeat(FamilyEmoji, 101)))).IsValid);
    }

    [Fact]
    public void A_name_stuffed_with_combining_marks_is_rejected_even_though_it_is_one_character()
    {
        var zalgo = "e" + new string('́', 1700);

        Assert.False(Validator.Validate(Request(zalgo)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A short description.")]
    public void An_optional_description_may_be_missing_empty_or_short(string? description) =>
        Assert.True(Validator.Validate(Request("Project", description)).IsValid);

    [Theory]
    [InlineData(999, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(5000, false)]
    public void A_description_is_limited_to_a_thousand_characters(int length, bool accepted) =>
        Assert.Equal(accepted, Validator.Validate(Request("Project", new string('d', length))).IsValid);

    [Fact]
    public void Only_the_description_is_flagged_when_only_the_description_is_too_long()
    {
        var result = Validator.Validate(Request("Project", new string('d', 1001)));

        Assert.Equal("Description", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var result = Validator.Validate(Request(string.Empty, new string('d', 1001)));

        Assert.Equal(["Description", "Name"], result.Errors.Select(e => e.PropertyName).Order());
    }

    [Fact]
    public void Error_messages_state_the_limit_but_never_repeat_the_rejected_text()
    {
        var secret = "SECRET-" + new string('x', 200);

        var messages = Validator.Validate(Request(secret)).Errors.Select(e => e.ErrorMessage).ToList();

        Assert.Equal("Name must be between 1 and 100 characters.", Assert.Single(messages));
        Assert.DoesNotContain("SECRET", string.Concat(messages), StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_with_only_a_name_is_accepted_and_the_description_is_optional()
    {
        var request = Bind("""{"name":"Mobile launch"}""");

        Assert.Equal("Mobile launch", request.Name);
        Assert.Null(request.Description);
        Assert.True(Validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"description":"no name"}""")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":42}""")]
    [InlineData("""{"name":["a"]}""")]
    [InlineData("""{"name":"ok","extra":"field"}""")]
    [InlineData("""{"name":"ok","Name":"also"}""")]
    [InlineData("""{"Name":"wrong case"}""")]
    [InlineData("""{"name":"ok","description":7}""")]
    [InlineData("[]")]
    public void A_body_that_does_not_match_the_contract_is_rejected_before_validation(string json) =>
        Assert.ThrowsAny<JsonException>(() => Bind(json));

    [Fact]
    public void A_json_null_body_reads_as_no_request_at_all_which_the_endpoint_filter_answers_with_400() =>
        Assert.Null(JsonSerializer.Deserialize<CreateProjectRequest>("null", ContractJson.Options));

    [Theory]
    [InlineData("""<script>alert(1)</script>""")]
    [InlineData("""'; DROP TABLE projects; --""")]
    [InlineData("""${jndi:ldap://x}""")]
    public void Markup_and_injection_text_is_valid_input_and_is_stored_as_plain_text_never_interpreted(string name) =>
        Assert.True(Validator.Validate(Request(name)).IsValid);
}
