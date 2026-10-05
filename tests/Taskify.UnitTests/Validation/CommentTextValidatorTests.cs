using System.Text.Json;
using Taskify.Contracts;
using Taskify.Tasks.Api.Endpoints;
using Taskify.Tasks.Api.Validation;

namespace Taskify.UnitTests.Validation;

/// <summary>
/// Validation of a comment (spec FR-015, FR-019; constitution Principle II): 1–2,000 user-perceived characters after
/// trimming, with every other shape of body refused before it is read.
/// </summary>
public class CommentTextValidatorTests
{
    // 👨‍👩‍👧‍👦: one user-perceived character made of 11 UTF-16 code units.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    private static readonly CommentTextValidator Validator = new();

    private static CommentTextRequest Bind(string json) =>
        JsonSerializer.Deserialize<CommentTextRequest>(json, ContractJson.Options)!;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1999)]
    [InlineData(2000)]
    public void A_comment_of_one_to_two_thousand_characters_is_accepted(int length) =>
        Assert.True(Validator.Validate(new CommentTextRequest(new string('c', length))).IsValid);

    [Theory]
    [InlineData(2001)]
    [InlineData(10000)]
    public void A_comment_over_two_thousand_characters_is_rejected_with_the_limit_in_the_message(int length)
    {
        var result = Validator.Validate(new CommentTextRequest(new string('c', length)));

        var error = Assert.Single(result.Errors);
        Assert.Equal("Text", error.PropertyName);
        Assert.Equal("Text must be between 1 and 2000 characters.", error.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n  ")]
    [InlineData("  ")]
    public void An_empty_or_whitespace_only_comment_is_rejected(string text) =>
        Assert.False(Validator.Validate(new CommentTextRequest(text)).IsValid);

    [Fact]
    public void Whitespace_around_a_comment_does_not_count_towards_the_limit() =>
        Assert.True(Validator.Validate(new CommentTextRequest("  " + new string('c', 2000) + "  ")).IsValid);

    [Fact]
    public void Exactly_two_thousand_emoji_are_accepted_and_two_thousand_and_one_are_not()
    {
        Assert.True(Validator.Validate(new CommentTextRequest(string.Concat(Enumerable.Repeat(FamilyEmoji, 2000)))).IsValid);
        Assert.False(Validator.Validate(new CommentTextRequest(string.Concat(Enumerable.Repeat(FamilyEmoji, 2001)))).IsValid);
    }

    [Fact]
    public void A_comment_stuffed_with_combining_marks_is_rejected_by_the_abuse_guard()
    {
        var zalgo = "e" + new string('́', 32_001);

        Assert.False(Validator.Validate(new CommentTextRequest(zalgo)).IsValid);
    }

    [Theory]
    [InlineData("""<script>alert(1)</script>""")]
    [InlineData("""<img src=x onerror=alert(1)>""")]
    [InlineData("""'; DROP TABLE comments; --""")]
    [InlineData("line one\nline two\r\nline three")]
    public void Markup_and_multi_line_text_are_valid_input_and_are_stored_as_plain_text(string text) =>
        Assert.True(Validator.Validate(new CommentTextRequest(text)).IsValid);

    [Fact]
    public void The_error_message_never_repeats_the_rejected_text()
    {
        var secret = "SECRET-" + new string('x', 2100);

        var message = Assert.Single(Validator.Validate(new CommentTextRequest(secret)).Errors).ErrorMessage;

        Assert.DoesNotContain("SECRET", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_body_with_just_the_text_is_read()
    {
        var request = Bind("""{"text":"Looks good to me."}""");

        Assert.Equal("Looks good to me.", request.Text);
        Assert.True(Validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"text":null}""")]
    [InlineData("""{"text":42}""")]
    [InlineData("""{"text":["a"]}""")]
    [InlineData("""{"text":"ok","authorUserId":"11111111-1111-1111-1111-000000000001"}""")]
    [InlineData("""{"text":"ok","extra":1}""")]
    [InlineData("""{"Text":"wrong case"}""")]
    [InlineData("""{"text":"ok","Text":"duplicate with another case"}""")]
    [InlineData("[]")]
    public void A_body_that_does_not_match_the_contract_is_rejected_before_validation(string json) =>
        Assert.ThrowsAny<JsonException>(() => Bind(json));
}
