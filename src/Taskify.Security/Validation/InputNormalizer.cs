namespace Taskify.Security.Validation;

/// <summary>
/// Trims user input the same way everywhere (spec FR-006, FR-009, FR-015: lengths are checked "after trimming").
/// </summary>
public static class InputNormalizer
{
    /// <summary>Trims leading and trailing whitespace. A <see langword="null"/> input becomes an empty string.</summary>
    /// <param name="value">The raw input.</param>
    /// <returns>The trimmed text, never <see langword="null"/>.</returns>
    public static string Trim(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>
    /// Trims the input and turns an empty result into <see langword="null"/>, which is how optional
    /// fields (project and task descriptions) are stored (data-model.md).
    /// </summary>
    /// <param name="value">The raw input.</param>
    /// <returns>The trimmed text, or <see langword="null"/> when nothing is left.</returns>
    public static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
