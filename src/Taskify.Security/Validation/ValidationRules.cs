using FluentValidation;

namespace Taskify.Security.Validation;

/// <summary>Reusable FluentValidation rules (constitution Principle II: explicit, allow-list, server-side validation).</summary>
public static class ValidationRules
{
    /// <summary>
    /// Requires the trimmed text to have between <paramref name="min"/> and <paramref name="max"/>
    /// user-perceived characters (spec FR-019). Use <c>min = 0</c> for optional fields. The message never
    /// contains the rejected value.
    /// </summary>
    /// <typeparam name="T">The validated object type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="min">The fewest characters allowed after trimming.</param>
    /// <param name="max">The most characters allowed after trimming.</param>
    /// <returns>The rule builder, for chaining.</returns>
    public static IRuleBuilderOptions<T, string?> MustHaveTextLength<T>(this IRuleBuilder<T, string?> ruleBuilder, int min, int max)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);

        // "{PropertyName}" is a FluentValidation placeholder, hence the doubled braces in the interpolation.
        var message = min <= 0
            ? $"{{PropertyName}} must be at most {max} characters."
            : $"{{PropertyName}} must be between {min} and {max} characters.";

        return ruleBuilder.Must(value => TextLength.IsWithin(value, min, max)).WithMessage(message);
    }

    /// <summary>Requires a non-empty GUID. Used for IDs in request bodies and queries.</summary>
    /// <typeparam name="T">The validated object type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The rule builder, for chaining.</returns>
    public static IRuleBuilderOptions<T, Guid> MustBeId<T>(this IRuleBuilder<T, Guid> ruleBuilder)
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);
        return ruleBuilder.NotEqual(Guid.Empty).WithMessage("{PropertyName} must be a valid ID.");
    }
}
