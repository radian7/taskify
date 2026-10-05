using FluentValidation;
using Taskify.Projects.Api.Endpoints;
using Taskify.Security.Validation;

namespace Taskify.Projects.Api.Validation;

/// <summary>
/// Validates <see cref="CreateProjectRequest"/> (constitution Principle II; spec FR-006, FR-019). Lengths count
/// user-perceived characters after trimming.
/// </summary>
public sealed class CreateProjectValidator : AbstractValidator<CreateProjectRequest>
{
    /// <summary>The longest name, in user-perceived characters.</summary>
    public const int MaxName = 100;

    /// <summary>The longest description, in user-perceived characters.</summary>
    public const int MaxDescription = 1000;

    /// <summary>Creates the validator.</summary>
    public CreateProjectValidator()
    {
        // name: "Required; 1–100 chars after trim (FR-006); duplicates allowed"
        RuleFor(r => r.Name).MustHaveTextLength(1, MaxName);

        // description: "0–1,000 chars after trim; empty string stored as null"
        RuleFor(r => r.Description).MustHaveTextLength(0, MaxDescription);
    }
}
