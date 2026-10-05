using System.Globalization;

namespace Taskify.Security.Validation;

/// <summary>
/// Counts text length in user-perceived characters (spec FR-019, research R7). One emoji, flag or letter with
/// combining accents counts as one character, so the number the user sees while typing is the number the
/// server enforces.
/// </summary>
public static class TextLength
{
    /// <summary>
    /// The abuse guard: no field may hold more than this many UTF-16 code units per allowed character. It stops
    /// input stuffed with endless combining marks ("Zalgo" text) that counts as one grapheme cluster, and it is
    /// checked <b>before</b> counting so the cost of counting stays bounded. A family emoji needs 11 code units
    /// and a kiss emoji with skin tones about 15, so 16 never rejects a real character.
    /// </summary>
    public const int GuardFactor = 16;

    /// <summary>Counts the user-perceived characters (extended grapheme clusters, Unicode UAX #29).</summary>
    /// <param name="value">The text to count. It is not trimmed.</param>
    /// <returns>The number of characters.</returns>
    public static int Count(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new StringInfo(value).LengthInTextElements;
    }

    /// <summary>Checks the abuse guard.</summary>
    /// <param name="value">The text to check.</param>
    /// <param name="maxCharacters">The allowed number of user-perceived characters.</param>
    /// <returns><see langword="true"/> when the text is longer than <see cref="GuardFactor"/> × <paramref name="maxCharacters"/> code units.</returns>
    public static bool ExceedsGuard(string value, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > (long)maxCharacters * GuardFactor;
    }

    /// <summary>Checks that trimmed input has between <paramref name="min"/> and <paramref name="max"/> characters.</summary>
    /// <param name="value">The raw input.</param>
    /// <param name="min">The fewest characters allowed (0 for optional fields).</param>
    /// <param name="max">The most characters allowed.</param>
    /// <returns><see langword="true"/> when the input is acceptable.</returns>
    public static bool IsWithin(string? value, int min, int max)
    {
        var trimmed = InputNormalizer.Trim(value);

        // Guard first: reject absurdly long input without spending time counting it.
        if (ExceedsGuard(trimmed, max))
        {
            return false;
        }

        var count = Count(trimmed);
        return count >= min && count <= max;
    }

    /// <summary>
    /// Shortens text to at most <paramref name="maxCharacters"/> user-perceived characters without cutting
    /// through an emoji or accented letter (used for notification summaries, spec FR-028).
    /// </summary>
    /// <param name="value">The text to shorten.</param>
    /// <param name="maxCharacters">The most characters to keep.</param>
    /// <returns>The original text if it is short enough, otherwise its first characters.</returns>
    public static string Truncate(string value, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCharacters);

        var info = new StringInfo(value);
        return info.LengthInTextElements <= maxCharacters ? value : info.SubstringByTextElements(0, maxCharacters);
    }
}
