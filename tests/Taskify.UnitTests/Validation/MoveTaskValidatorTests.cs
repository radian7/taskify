using System.Text.Json;
using Taskify.Contracts;
using Taskify.Tasks.Api.Endpoints;
using Taskify.Tasks.Api.Validation;

namespace Taskify.UnitTests.Validation;

/// <summary>
/// Validation of the move request (spec FR-012, FR-019, FR-021; constitution Principle II: allow-list, with tests for
/// accepted and rejected input). The column must be exactly one of the four known names: integers, other casing,
/// unknown names, a missing field and extra fields are all refused before any data is touched.
/// </summary>
public class MoveTaskValidatorTests
{
    private static readonly MoveTaskValidator Validator = new();

    private static MoveTaskRequest Bind(string json) =>
        JsonSerializer.Deserialize<MoveTaskRequest>(json, ContractJson.Options)!;

    [Theory]
    [InlineData("ToDo", TaskStatus.ToDo)]
    [InlineData("InProgress", TaskStatus.InProgress)]
    [InlineData("InReview", TaskStatus.InReview)]
    [InlineData("Done", TaskStatus.Done)]
    public void Each_of_the_four_columns_is_accepted(string name, TaskStatus expected)
    {
        var request = Bind($$"""{"toStatus":"{{name}}"}""");

        Assert.Equal(expected, request.ToStatus);
        Assert.True(Validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"toStatus":null}""")]
    [InlineData("""{"toStatus":""}""")]
    [InlineData("""{"toStatus":"Blocked"}""")]
    [InlineData("""{"toStatus":"todo"}""")]
    [InlineData("""{"toStatus":"DONE"}""")]
    [InlineData("""{"toStatus":"To Do"}""")]
    [InlineData("""{"toStatus":" Done"}""")]
    [InlineData("""{"toStatus":1}""")]
    [InlineData("""{"toStatus":99}""")]
    [InlineData("""{"toStatus":true}""")]
    [InlineData("""{"toStatus":["Done"]}""")]
    [InlineData("""{"toStatus":"Done","extra":"field"}""")]
    [InlineData("""{"toStatus":"Done","ToStatus":"ToDo"}""")]
    [InlineData("""{"ToStatus":"Done"}""")]
    [InlineData("[]")]
    [InlineData("\"Done\"")]
    public void Anything_else_is_rejected_before_it_can_reach_the_database(string json) =>
        Assert.ThrowsAny<JsonException>(() => Bind(json));

    [Fact]
    public void A_value_that_is_not_a_defined_column_is_rejected_even_if_built_in_code()
    {
        var result = Validator.Validate(new MoveTaskRequest((TaskStatus)99));

        Assert.False(result.IsValid);
        Assert.Equal("ToStatus", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void The_error_message_does_not_contain_the_rejected_value()
    {
        var result = Validator.Validate(new MoveTaskRequest((TaskStatus)12345));

        Assert.DoesNotContain("12345", Assert.Single(result.Errors).ErrorMessage, StringComparison.Ordinal);
    }
}
