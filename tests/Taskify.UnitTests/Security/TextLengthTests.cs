using FluentValidation;
using Taskify.Security.Validation;

namespace Taskify.UnitTests.Security;

/// <summary>Tests for user-perceived character counting, trimming and the abuse guard (spec FR-019, research R7).</summary>
public class TextLengthTests
{
    // 👨‍👩‍👧‍👦: four emoji joined by zero-width joiners = 11 UTF-16 code units, one user-perceived character.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    // 🇵🇱: two regional indicator symbols = 4 code units, one character.
    private const string Flag = "\U0001F1F5\U0001F1F1";

    // é written as 'e' followed by a combining acute accent: 2 code units, one character.
    private const string DecomposedE = "é";

    [Fact]
    public void Count_treats_a_family_emoji_as_one_character() =>
        Assert.Equal(1, TextLength.Count(FamilyEmoji));

    [Fact]
    public void Count_treats_a_flag_as_one_character() =>
        Assert.Equal(1, TextLength.Count(Flag));

    [Fact]
    public void Count_treats_a_letter_with_a_combining_accent_as_one_character() =>
        Assert.Equal(1, TextLength.Count(DecomposedE));

    [Theory]
    [InlineData(null, 1, 200, false)]
    [InlineData("", 1, 200, false)]
    [InlineData("   ", 1, 200, false)]
    [InlineData("\t\n  ", 1, 200, false)]
    [InlineData("a", 1, 200, true)]
    [InlineData("  a  ", 1, 200, true)]
    [InlineData(null, 0, 1000, true)]
    [InlineData("", 0, 1000, true)]
    [InlineData("   ", 0, 1000, true)]
    public void IsWithin_trims_before_checking_the_minimum(string? value, int min, int max, bool expected) =>
        Assert.Equal(expected, TextLength.IsWithin(value, min, max));

    [Theory]
    [InlineData(1, 100, 99, true)]
    [InlineData(1, 100, 100, true)]
    [InlineData(1, 100, 101, false)]
    [InlineData(1, 200, 200, true)]
    [InlineData(1, 200, 201, false)]
    [InlineData(1, 2000, 2000, true)]
    [InlineData(1, 2000, 2001, false)]
    public void IsWithin_accepts_the_maximum_and_rejects_one_more(int min, int max, int length, bool expected) =>
        Assert.Equal(expected, TextLength.IsWithin(new string('a', length), min, max));

    [Fact]
    public void IsWithin_ignores_padding_when_counting_the_maximum() =>
        Assert.True(TextLength.IsWithin("   " + new string('a', 200) + "   ", 1, 200));

    [Theory]
    [InlineData(FamilyEmoji)]
    [InlineData(Flag)]
    [InlineData(DecomposedE)]
    public void IsWithin_accepts_exactly_200_multi_unit_characters_and_rejects_201(string character)
    {
        Assert.True(TextLength.IsWithin(string.Concat(Enumerable.Repeat(character, 200)), 1, 200));
        Assert.False(TextLength.IsWithin(string.Concat(Enumerable.Repeat(character, 201)), 1, 200));
    }

    [Fact]
    public void IsWithin_accepts_2000_family_emoji_in_a_comment()
    {
        var text = string.Concat(Enumerable.Repeat(FamilyEmoji, 2000));
        Assert.False(TextLength.ExceedsGuard(text, 2000));
        Assert.True(TextLength.IsWithin(text, 1, 2000));
    }

    [Fact]
    public void IsWithin_rejects_zalgo_text_that_counts_as_one_character_but_exceeds_the_guard()
    {
        // One base letter followed by thousands of combining marks is a single grapheme cluster.
        var zalgo = "e" + new string('́', 3300);

        Assert.Equal(1, TextLength.Count(zalgo));
        Assert.True(TextLength.ExceedsGuard(zalgo, 200));
        Assert.False(TextLength.IsWithin(zalgo, 1, 200));
    }

    [Theory]
    [InlineData(200, 3200, false)]
    [InlineData(200, 3201, true)]
    [InlineData(1000, 16000, false)]
    [InlineData(1000, 16001, true)]
    public void ExceedsGuard_allows_sixteen_code_units_per_allowed_character(int maxCharacters, int length, bool expected) =>
        Assert.Equal(expected, TextLength.ExceedsGuard(new string('a', length), maxCharacters));

    [Fact]
    public void Truncate_never_cuts_through_a_character()
    {
        var text = "ab" + FamilyEmoji + "cd";

        Assert.Equal("ab" + FamilyEmoji, TextLength.Truncate(text, 3));
        Assert.Equal(text, TextLength.Truncate(text, 10));
        Assert.Equal(string.Empty, TextLength.Truncate(text, 0));
    }

    [Fact]
    public void InputNormalizer_trims_and_turns_empty_optional_text_into_null()
    {
        Assert.Equal("x", InputNormalizer.Trim("  x "));
        Assert.Equal(string.Empty, InputNormalizer.Trim(null));
        Assert.Null(InputNormalizer.TrimToNull("   "));
        Assert.Null(InputNormalizer.TrimToNull(null));
        Assert.Equal("x", InputNormalizer.TrimToNull(" x "));
    }

    [Fact]
    public void MustHaveTextLength_reports_a_message_without_the_rejected_value()
    {
        var validator = new InlineValidator<Sample>();
        validator.RuleFor(s => s.Title).MustHaveTextLength(1, 200);

        var result = validator.Validate(new Sample("   "));

        var error = Assert.Single(result.Errors);
        Assert.Equal("Title", error.PropertyName);
        Assert.Equal("Title must be between 1 and 200 characters.", error.ErrorMessage);
    }

    [Fact]
    public void MustHaveTextLength_uses_an_at_most_message_for_optional_fields()
    {
        var validator = new InlineValidator<Sample>();
        validator.RuleFor(s => s.Title).MustHaveTextLength(0, 5);

        var result = validator.Validate(new Sample("abcdef"));

        Assert.Equal("Title must be at most 5 characters.", Assert.Single(result.Errors).ErrorMessage);
        Assert.True(validator.Validate(new Sample(null)).IsValid);
    }

    [Fact]
    public void MustBeId_rejects_the_empty_guid()
    {
        var validator = new InlineValidator<IdSample>();
        validator.RuleFor(s => s.Id).MustBeId();

        Assert.False(validator.Validate(new IdSample(Guid.Empty)).IsValid);
        Assert.True(validator.Validate(new IdSample(Guid.NewGuid())).IsValid);
    }

    private sealed record Sample(string? Title);

    private sealed record IdSample(Guid Id);
}

/// <summary>Server times are cut to what the database keeps (microseconds), so responses match later reads.</summary>
public class TimeProviderExtensionsTests
{
    [Fact]
    public void The_time_is_cut_to_whole_microseconds_and_stays_utc()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 16, 58, 46, TimeSpan.Zero).AddTicks(7288531));

        var now = Taskify.Security.TimeProviderExtensions.GetUtcNowMicroseconds(clock);

        Assert.Equal(0, now.Ticks % 10);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 16, 58, 46, TimeSpan.Zero).AddTicks(7288530), now);
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    [Fact]
    public void A_time_that_already_has_whole_microseconds_is_unchanged()
    {
        var exact = new DateTimeOffset(2026, 10, 4, 16, 58, 46, TimeSpan.Zero).AddTicks(7288530);

        Assert.Equal(exact, Taskify.Security.TimeProviderExtensions.GetUtcNowMicroseconds(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(exact)));
    }
}
