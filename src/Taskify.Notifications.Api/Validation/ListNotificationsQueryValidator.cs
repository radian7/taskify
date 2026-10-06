using System.Globalization;
using FluentValidation;
using Taskify.Notifications.Api.Endpoints;

namespace Taskify.Notifications.Api.Validation;

/// <summary>
/// Validates <see cref="ListNotificationsQuery"/> (constitution Principle II; contracts/notifications-api.yaml):
/// <c>limit</c> is an integer from 1 to 100 (default 50) and <c>unreadOnly</c> is a boolean (default false).
/// </summary>
public sealed class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    /// <summary>The limit used when none is given.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest allowed limit.</summary>
    public const int MaxLimit = 100;

    /// <summary>Creates the validator.</summary>
    public ListNotificationsQueryValidator()
    {
        // The values arrive as text so that "abc" is a field error with our Problem Details body, not a binder failure.
        RuleFor(q => q.Limit)
            .Must(value => value is null || IsValidLimit(value))
            .WithMessage($"limit must be a whole number from 1 to {MaxLimit}.");

        RuleFor(q => q.UnreadOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("unreadOnly must be true or false.");
    }

    private static bool IsValidLimit(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit is >= 1 and <= MaxLimit;
}
