namespace Taskify.Security.Audit;

/// <summary>How an audited action ended (data-model.md "Audit event").</summary>
public enum AuditOutcome
{
    /// <summary>The action was carried out.</summary>
    Succeeded = 0,

    /// <summary>The input failed validation (HTTP 400).</summary>
    Validation,

    /// <summary>The API key or acting user was missing or unknown (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The caller or user is not allowed to do this (HTTP 403).</summary>
    Forbidden,

    /// <summary>The target does not exist (HTTP 404).</summary>
    NotFound,

    /// <summary>The target is in a state that does not allow the action (HTTP 409).</summary>
    Conflict,

    /// <summary>The request body was too large (HTTP 413).</summary>
    TooLarge,

    /// <summary>The request referred to a user, project or other entity that does not exist (HTTP 422).</summary>
    UnknownReference,

    /// <summary>A rate limit was exceeded (HTTP 429).</summary>
    RateLimited,

    /// <summary>An unexpected server error (HTTP 5xx).</summary>
    Error,
}

/// <summary>The audited action names (data-model.md "Audit event").</summary>
public static class AuditActions
{
    /// <summary>A request was refused. The outcome says why.</summary>
    public const string RequestRejected = nameof(RequestRejected);

    /// <summary>A person chose or switched the user they act as (spec FR-032).</summary>
    public const string UserSelected = nameof(UserSelected);

    /// <summary>A selection was restored from the cookie on a new session (spec FR-032).</summary>
    public const string UserSelectionRestored = nameof(UserSelectionRestored);

    /// <summary>A project was created.</summary>
    public const string ProjectCreated = nameof(ProjectCreated);

    /// <summary>A task was created.</summary>
    public const string TaskCreated = nameof(TaskCreated);

    /// <summary>A task's title or description was edited.</summary>
    public const string TaskUpdated = nameof(TaskUpdated);

    /// <summary>A task's assignee changed.</summary>
    public const string TaskAssigned = nameof(TaskAssigned);

    /// <summary>A task moved to another column.</summary>
    public const string TaskMoved = nameof(TaskMoved);

    /// <summary>A comment was added.</summary>
    public const string CommentAdded = nameof(CommentAdded);

    /// <summary>A comment was edited.</summary>
    public const string CommentEdited = nameof(CommentEdited);

    /// <summary>A comment was deleted.</summary>
    public const string CommentDeleted = nameof(CommentDeleted);

    /// <summary>A notification was marked read.</summary>
    public const string NotificationRead = nameof(NotificationRead);

    /// <summary>All of a user's notifications were marked read.</summary>
    public const string NotificationsReadAll = nameof(NotificationsReadAll);

    /// <summary>The Notifications API accepted a domain event.</summary>
    public const string EventReceived = nameof(EventReceived);

    /// <summary>The outbox gave up on an event because the receiver rejected it permanently.</summary>
    public const string EventDeadLettered = nameof(EventDeadLettered);

    /// <summary>A hub connection or method call was refused.</summary>
    public const string HubRejected = nameof(HubRejected);
}

/// <summary>
/// One audit record (constitution Security Requirements; spec FR-022, FR-032). It has no field for
/// titles, names, descriptions or comment text on purpose: content must never reach the logs.
/// </summary>
/// <param name="Action">What happened, from <see cref="AuditActions"/>.</param>
/// <param name="Outcome">How it ended.</param>
public sealed record AuditEntry(string Action, AuditOutcome Outcome)
{
    /// <summary>The user the request acted as, when known.</summary>
    public Guid? ActingUserId { get; init; }

    /// <summary>The previously selected user (<see cref="AuditActions.UserSelected"/> only).</summary>
    public Guid? PreviousUserId { get; init; }

    /// <summary>The kind of entity affected, for example <c>Task</c> or <c>Comment</c>.</summary>
    public string? EntityType { get; init; }

    /// <summary>The ID of the entity affected.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>The calling service. Taken from the current request when not set.</summary>
    public string? CallerService { get; init; }

    /// <summary>The end user's source IP. Taken from the current request when not set.</summary>
    public string? SourceIp { get; init; }
}

/// <summary>Maps HTTP status codes to <see cref="AuditOutcome"/> values.</summary>
public static class AuditOutcomes
{
    /// <summary>Converts a response status code to the matching outcome.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>The outcome.</returns>
    public static AuditOutcome FromStatusCode(int statusCode) => statusCode switch
    {
        < 400 => AuditOutcome.Succeeded,
        400 => AuditOutcome.Validation,
        401 => AuditOutcome.Unauthorized,
        403 => AuditOutcome.Forbidden,
        404 => AuditOutcome.NotFound,
        409 => AuditOutcome.Conflict,
        413 => AuditOutcome.TooLarge,
        422 => AuditOutcome.UnknownReference,
        429 => AuditOutcome.RateLimited,
        _ => statusCode >= 500 ? AuditOutcome.Error : AuditOutcome.Validation,
    };
}
