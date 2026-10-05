using FluentValidation;
using Taskify.Tasks.Api.Endpoints;

namespace Taskify.Tasks.Api.Validation;

/// <summary>
/// Validates <see cref="MoveTaskRequest"/> (constitution Principle II; spec FR-021). The strict JSON settings already
/// refuse a missing field, extra fields, integers, other casing and unknown names while reading the body; this is the
/// second, independent check that the value is one of the four defined columns.
/// </summary>
public sealed class MoveTaskValidator : AbstractValidator<MoveTaskRequest>
{
    /// <summary>Creates the validator.</summary>
    public MoveTaskValidator() =>
        RuleFor(request => request.ToStatus)
            .IsInEnum()
            .WithMessage("{PropertyName} must be one of ToDo, InProgress, InReview or Done.");
}
