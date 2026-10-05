using FluentValidation;
using Taskify.Security.Validation;
using Taskify.Tasks.Api.Endpoints;

namespace Taskify.Tasks.Api.Validation;

/// <summary>
/// Validates <see cref="CommentTextRequest"/> (constitution Principle II; spec FR-015, FR-019). The length counts
/// user-perceived characters after trimming.
/// </summary>
public sealed class CommentTextValidator : AbstractValidator<CommentTextRequest>
{
    /// <summary>The longest comment, in user-perceived characters.</summary>
    public const int MaxText = 2000;

    /// <summary>Creates the validator.</summary>
    public CommentTextValidator() =>
        // text: "1–2,000 chars after trim (FR-015)"
        RuleFor(r => r.Text).MustHaveTextLength(1, MaxText);
}
