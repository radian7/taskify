using FluentValidation;
using Taskify.Security.Validation;
using Taskify.Tasks.Api.Endpoints;

namespace Taskify.Tasks.Api.Validation;

/// <summary>
/// Validates <see cref="CreateTaskRequest"/> (constitution Principle II; spec FR-009, FR-010, FR-019). Lengths count
/// user-perceived characters after trimming. Whether the project and the assignee exist is checked by the endpoint, which
/// answers 422 for a reference that does not exist (spec FR-021).
/// </summary>
public sealed class CreateTaskValidator : AbstractValidator<CreateTaskRequest>
{
    /// <summary>The longest title, in user-perceived characters.</summary>
    public const int MaxTitle = 200;

    /// <summary>The longest description, in user-perceived characters.</summary>
    public const int MaxDescription = 5000;

    /// <summary>Creates the validator.</summary>
    public CreateTaskValidator()
    {
        // projectId: "Must exist in the Projects API (checked on create)"; an empty GUID can never exist.
        RuleFor(r => r.ProjectId).MustBeId();

        // title: "Required; 1–200 chars after trim (FR-009)"
        RuleFor(r => r.Title).MustHaveTextLength(1, MaxTitle);

        // description: "0–5,000 chars after trim"
        RuleFor(r => r.Description).MustHaveTextLength(0, MaxDescription);

        // assigneeUserId: "Null = unassigned; otherwise a seeded user (FR-010)"; an empty GUID is never a user.
        RuleFor(r => r.AssigneeUserId!.Value).MustBeId().When(r => r.AssigneeUserId.HasValue).OverridePropertyName(nameof(CreateTaskRequest.AssigneeUserId));
    }
}

/// <summary>Validates <see cref="UpdateTaskRequest"/> with the same rules as creation (spec FR-011).</summary>
public sealed class UpdateTaskValidator : AbstractValidator<UpdateTaskRequest>
{
    /// <summary>Creates the validator.</summary>
    public UpdateTaskValidator()
    {
        RuleFor(r => r.Title).MustHaveTextLength(1, CreateTaskValidator.MaxTitle);
        RuleFor(r => r.Description).MustHaveTextLength(0, CreateTaskValidator.MaxDescription);
        RuleFor(r => r.AssigneeUserId!.Value).MustBeId().When(r => r.AssigneeUserId.HasValue).OverridePropertyName(nameof(UpdateTaskRequest.AssigneeUserId));
    }
}
