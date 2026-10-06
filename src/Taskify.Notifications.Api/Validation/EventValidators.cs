using System.Globalization;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Taskify.Contracts.Events;
using Taskify.Security.Users;
using Taskify.Security.Validation;

namespace Taskify.Notifications.Api.Validation;

/// <summary>
/// Validates a domain event received on <c>/internal/events</c>: the envelope, then the payload for its type. Field
/// names, nullability and types follow the <c>*Envelope</c> schemas in contracts/events.asyncapi.yaml exactly.
/// </summary>
/// <remarks>
/// Internal origin does not imply trust (constitution Principle II): the caller holds a valid service key, but a
/// bug or a compromised service could still send anything, so every field is allow-listed here. The body is read as
/// a raw JSON tree so that unknown fields, missing fields, wrong types and duplicate names are all rejected before
/// any value is used. Rejection messages never contain the rejected value.
/// </remarks>
/// <param name="users">The directory of predefined users, for the actor and assignee IDs.</param>
public sealed class EventEnvelopeValidator(IUserDirectory users)
{
    private static readonly string[] EnvelopeFields = ["eventId", "type", "version", "occurredAt", "actorUserId", "payload"];

    private readonly EnvelopeRules envelopeRules = new(users);
    private readonly PayloadRules payloadRules = new(users);

    /// <summary>Validates one event given as raw JSON text.</summary>
    /// <param name="json">The request body.</param>
    /// <param name="cancellationToken">Cancels the validation.</param>
    /// <returns>The outcome; <see cref="ValidationResult.IsValid"/> is true when the event is acceptable.</returns>
    public async Task<ValidationResult> ValidateAsync(string json, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return Fail("body", "The body must be a JSON object.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail("body", "The body must be a JSON object.");
            }

            var reader = new FieldReader(root, failures, string.Empty);
            reader.RejectUnknownAndDuplicates(EnvelopeFields);

            var eventId = reader.Guid("eventId");
            var type = reader.String("type");
            var version = reader.Integer("version");
            var occurredAt = reader.Timestamp("occurredAt");
            var actor = reader.Guid("actorUserId");
            var hasPayload = reader.Object("payload", out var payload);

            var envelope = new ParsedEnvelope(eventId, type, version, occurredAt, actor);
            failures.AddRange((await envelopeRules.ValidateAsync(envelope, cancellationToken)).Errors);

            if (hasPayload && type is not null && EventTypes.All.Contains(type))
            {
                await ValidatePayloadAsync(type, payload, failures, cancellationToken);
            }
        }

        return new ValidationResult(failures);

        static ValidationResult Fail(string property, string message) =>
            new([new ValidationFailure(property, message)]);
    }

    private async Task ValidatePayloadAsync(
        string type, JsonElement payload, List<ValidationFailure> failures, CancellationToken cancellationToken)
    {
        var reader = new FieldReader(payload, failures, "payload.");
        ValidationResult result;
        switch (type)
        {
            case EventTypes.ProjectCreated:
            {
                reader.RejectUnknownAndDuplicates(["projectId", "name"]);
                var value = new ProjectCreatedPayload(reader.Guid("projectId"), reader.String("name") ?? string.Empty);
                result = await payloadRules.ProjectCreated.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.TaskCreated:
            {
                reader.RejectUnknownAndDuplicates(["taskId", "projectId", "title", "status", "assigneeUserId"]);
                var value = new TaskCreatedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.String("title") ?? string.Empty,
                    reader.Status("status"), reader.NullableGuid("assigneeUserId"));
                result = await payloadRules.TaskCreated.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.TaskUpdated:
            {
                reader.RejectUnknownAndDuplicates(["taskId", "projectId", "title"]);
                var value = new TaskUpdatedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.String("title") ?? string.Empty);
                result = await payloadRules.TaskUpdated.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.TaskAssigned:
            {
                reader.RejectUnknownAndDuplicates(
                    ["taskId", "projectId", "title", "previousAssigneeUserId", "assigneeUserId"]);
                var value = new TaskAssignedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.String("title") ?? string.Empty,
                    reader.NullableGuid("previousAssigneeUserId"), reader.NullableGuid("assigneeUserId"));
                result = await payloadRules.TaskAssigned.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.TaskMoved:
            {
                reader.RejectUnknownAndDuplicates(
                    ["taskId", "projectId", "title", "fromStatus", "toStatus", "assigneeUserId"]);
                var value = new TaskMovedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.String("title") ?? string.Empty,
                    reader.Status("fromStatus"), reader.Status("toStatus"), reader.NullableGuid("assigneeUserId"));
                result = await payloadRules.TaskMoved.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.CommentAdded:
            {
                reader.RejectUnknownAndDuplicates(["taskId", "projectId", "title", "commentId", "assigneeUserId"]);
                var value = new CommentAddedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.String("title") ?? string.Empty,
                    reader.Guid("commentId"), reader.NullableGuid("assigneeUserId"));
                result = await payloadRules.CommentAdded.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.CommentEdited:
            {
                reader.RejectUnknownAndDuplicates(["taskId", "projectId", "commentId"]);
                var value = new CommentEditedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.Guid("commentId"));
                result = await payloadRules.CommentEdited.ValidateAsync(value, cancellationToken);
                break;
            }

            case EventTypes.CommentDeleted:
            {
                reader.RejectUnknownAndDuplicates(["taskId", "projectId", "commentId"]);
                var value = new CommentDeletedPayload(
                    reader.Guid("taskId"), reader.Guid("projectId"), reader.Guid("commentId"));
                result = await payloadRules.CommentDeleted.ValidateAsync(value, cancellationToken);
                break;
            }

            default:
                return;
        }

        failures.AddRange(result.Errors);
    }

    /// <summary>The envelope after structural parsing; a field that failed parsing is null or empty.</summary>
    private sealed record ParsedEnvelope(Guid EventId, string? Type, int? Version, DateTimeOffset? OccurredAt, Guid ActorUserId);

    private sealed class EnvelopeRules : AbstractValidator<ParsedEnvelope>
    {
        public EnvelopeRules(IUserDirectory users)
        {
            RuleFor(e => e.EventId).MustBeNonEmptyId();
            RuleFor(e => e.Type).Must(type => type is not null && EventTypes.All.Contains(type))
                .WithMessage("type must be one of the known event types.");
            // Only version 1 exists; a different number is a contract this service does not understand.
            RuleFor(e => e.Version).Must(version => version == EventEnvelope.CurrentVersion)
                .WithMessage("version must be 1.");
            RuleFor(e => e.OccurredAt).NotNull().WithMessage("occurredAt must be a timestamp.");
            RuleFor(e => e.ActorUserId).MustBeKnownUser(users);
        }
    }

    /// <summary>One payload validator per event type (the contract's <c>*Envelope.payload</c> schemas).</summary>
    private sealed class PayloadRules(IUserDirectory users)
    {
        public ProjectCreatedValidator ProjectCreated { get; } = new();

        public TaskCreatedValidator TaskCreated { get; } = new(users);

        public TaskUpdatedValidator TaskUpdated { get; } = new();

        public TaskAssignedValidator TaskAssigned { get; } = new(users);

        public TaskMovedValidator TaskMoved { get; } = new(users);

        public CommentAddedValidator CommentAdded { get; } = new(users);

        public CommentEditedValidator CommentEdited { get; } = new();

        public CommentDeletedValidator CommentDeleted { get; } = new();
    }

    private sealed class ProjectCreatedValidator : AbstractValidator<ProjectCreatedPayload>
    {
        public ProjectCreatedValidator()
        {
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            // name: "1-100 characters" (FR-019), the same rule the Projects API applies to its own input.
            RuleFor(p => p.Name).MustHaveTextLength(1, 100);
        }
    }

    private sealed class TaskCreatedValidator : AbstractValidator<TaskCreatedPayload>
    {
        public TaskCreatedValidator(IUserDirectory users)
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.Title).MustHaveTextLength(1, 200);
            RuleFor(p => p.AssigneeUserId).MustBeKnownUserWhenSet(users);
        }
    }

    private sealed class TaskUpdatedValidator : AbstractValidator<TaskUpdatedPayload>
    {
        public TaskUpdatedValidator()
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.Title).MustHaveTextLength(1, 200);
        }
    }

    private sealed class TaskAssignedValidator : AbstractValidator<TaskAssignedPayload>
    {
        public TaskAssignedValidator(IUserDirectory users)
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.Title).MustHaveTextLength(1, 200);
            RuleFor(p => p.PreviousAssigneeUserId).MustBeKnownUserWhenSet(users);
            RuleFor(p => p.AssigneeUserId).MustBeKnownUserWhenSet(users);
        }
    }

    private sealed class TaskMovedValidator : AbstractValidator<TaskMovedPayload>
    {
        public TaskMovedValidator(IUserDirectory users)
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.Title).MustHaveTextLength(1, 200);
            RuleFor(p => p.AssigneeUserId).MustBeKnownUserWhenSet(users);
        }
    }

    private sealed class CommentAddedValidator : AbstractValidator<CommentAddedPayload>
    {
        public CommentAddedValidator(IUserDirectory users)
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.Title).MustHaveTextLength(1, 200);
            RuleFor(p => p.CommentId).MustBeNonEmptyId();
            RuleFor(p => p.AssigneeUserId).MustBeKnownUserWhenSet(users);
        }
    }

    private sealed class CommentEditedValidator : AbstractValidator<CommentEditedPayload>
    {
        public CommentEditedValidator()
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.CommentId).MustBeNonEmptyId();
        }
    }

    private sealed class CommentDeletedValidator : AbstractValidator<CommentDeletedPayload>
    {
        public CommentDeletedValidator()
        {
            RuleFor(p => p.TaskId).MustBeNonEmptyId();
            RuleFor(p => p.ProjectId).MustBeNonEmptyId();
            RuleFor(p => p.CommentId).MustBeNonEmptyId();
        }
    }

    /// <summary>
    /// Reads typed fields from a JSON object and records a failure (without the value) for each one that is missing
    /// or of the wrong type. A failed field yields an empty value, which the rule validators then also reject.
    /// </summary>
    private sealed class FieldReader(JsonElement element, List<ValidationFailure> failures, string prefix)
    {
        public void RejectUnknownAndDuplicates(string[] allowed)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (Array.IndexOf(allowed, property.Name) < 0)
                {
                    Fail("(unknown)", "An unknown field is not allowed.");
                }
                else if (!seen.Add(property.Name))
                {
                    // A duplicate name could make two parsers disagree about the value.
                    Fail(property.Name, "A field appears more than once.");
                }
            }
        }

        public Guid Guid(string name)
        {
            if (!Present(name, out var value))
            {
                Fail(name, $"{name} is required.");
                return System.Guid.Empty;
            }

            return ParseGuid(name, value) ?? System.Guid.Empty;
        }

        public Guid? NullableGuid(string name)
        {
            // Optional and nullable in the contract: absent and null both mean "nobody".
            if (!Present(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            // A malformed value becomes Guid.Empty, which the user rule rejects as well.
            return ParseGuid(name, value) ?? System.Guid.Empty;
        }

        public string? String(string name)
        {
            if (!Present(name, out var value))
            {
                Fail(name, $"{name} is required.");
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Fail(name, $"{name} must be a string.");
                return null;
            }

            return value.GetString();
        }

        public int? Integer(string name)
        {
            if (Present(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            Fail(name, $"{name} must be an integer.");
            return null;
        }

        public DateTimeOffset? Timestamp(string name)
        {
            if (Present(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                return at;
            }

            Fail(name, $"{name} must be a timestamp.");
            return null;
        }

        public TaskStatus Status(string name)
        {
            // Exact names only, as in the contract's TaskStatus enum: no numbers, no other casing.
            if (Present(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (Enum.GetNames<TaskStatus>().Contains(text, StringComparer.Ordinal))
                {
                    return Enum.Parse<TaskStatus>(text!);
                }
            }

            Fail(name, $"{name} must be a known status.");
            return default;
        }

        public bool Object(string name, out JsonElement value)
        {
            if (Present(name, out value) && value.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            Fail(name, $"{name} must be an object.");
            value = default;
            return false;
        }

        private bool Present(string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private void Fail(string name, string message) => failures.Add(new ValidationFailure(prefix + name, message));

        private Guid? ParseGuid(string name, JsonElement value)
        {
            // "D" format only (36 characters with hyphens): braces, "N" and other forms are not UUIDs per the contract.
            if (value.ValueKind == JsonValueKind.String && System.Guid.TryParseExact(value.GetString(), "D", out var id))
            {
                return id;
            }

            Fail(name, $"{name} must be a UUID.");
            return null;
        }
    }
}

/// <summary>Shared rules for the event validators.</summary>
internal static class EventRules
{
    /// <summary>An ID that failed to parse is <see cref="Guid.Empty"/>, so this also covers the parse failure.</summary>
    public static IRuleBuilderOptions<T, Guid> MustBeNonEmptyId<T>(this IRuleBuilder<T, Guid> rule) =>
        rule.NotEqual(Guid.Empty).WithMessage("{PropertyName} must be a non-empty UUID.");

    /// <summary>The user must be one of the predefined users.</summary>
    public static IRuleBuilderOptions<T, Guid> MustBeKnownUser<T>(this IRuleBuilder<T, Guid> rule, IUserDirectory users) =>
        rule.MustAsync((id, ct) => id != Guid.Empty ? users.ExistsAsync(id, ct) : Task.FromResult(false))
            .WithMessage("{PropertyName} must be a known user.");

    /// <summary>When set, the user must be one of the predefined users.</summary>
    public static IRuleBuilderOptions<T, Guid?> MustBeKnownUserWhenSet<T>(this IRuleBuilder<T, Guid?> rule, IUserDirectory users) =>
        rule.MustAsync((id, ct) => id is null ? Task.FromResult(true) : users.ExistsAsync(id.Value, ct))
            .WithMessage("{PropertyName} must be a known user.");
}
