namespace Taskify.Tasks.Api.Domain;

/// <summary>How an edit or delete of a comment ended.</summary>
public enum CommentOutcome
{
    /// <summary>The action was carried out (or there was nothing to change).</summary>
    Ok,

    /// <summary>The acting user is not the comment's author (spec FR-017).</summary>
    Forbidden,

    /// <summary>The comment was already deleted and can no longer be changed or restored (spec FR-024).</summary>
    AlreadyDeleted,
}

/// <summary>The result of <see cref="Comment.Edit"/> or <see cref="Comment.Delete"/>.</summary>
/// <param name="Outcome">How the action ended.</param>
/// <param name="Changed">Whether the comment was actually changed (an edit to the same text is not a change).</param>
public readonly record struct CommentChange(CommentOutcome Outcome, bool Changed);

/// <summary>
/// A message on a task (spec FR-015 to FR-017, FR-024). Only its author can edit or delete it. Deleting erases the text
/// for good and leaves a placeholder; a deleted comment cannot be edited, deleted again or restored.
/// </summary>
/// <remarks>"Chars" means user-perceived characters (spec FR-019): each visible character, including an emoji, counts as one.</remarks>
public sealed class Comment
{
    /// <summary>Creates a comment.</summary>
    /// <param name="id">The ID. "Generated".</param>
    /// <param name="taskId">The task it is on. "FK → Task".</param>
    /// <param name="authorUserId">The author. "Acting user at creation; never changes".</param>
    /// <param name="text">The text. "1–2,000 chars after trim (FR-015)", already trimmed.</param>
    /// <param name="createdAt">When it was posted. "Set by the server".</param>
    public Comment(Guid id, Guid taskId, Guid authorUserId, string text, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Id = id;
        TaskId = taskId;
        AuthorUserId = authorUserId;
        Text = text;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the comment ID.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the task the comment is on.</summary>
    public Guid TaskId { get; private set; }

    /// <summary>Gets the author. It never changes.</summary>
    public Guid AuthorUserId { get; private set; }

    /// <summary>Gets the text (1–2,000 chars after trim), or <see langword="null"/> once the comment is deleted (spec FR-024).</summary>
    public string? Text { get; private set; }

    /// <summary>Gets when the comment was posted (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the comment was last edited (UTC), or <see langword="null"/> if it never was.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>Gets when the comment was deleted (UTC), or <see langword="null"/> if it was not.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Gets a value indicating whether the comment has been deleted.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Edits the text. Only the author may edit (spec FR-017), and a deleted comment cannot be edited (FR-024). The author
    /// is checked first, so a stranger learns nothing about whether the comment was deleted.
    /// </summary>
    /// <param name="newText">The new text, already trimmed and validated (1–2,000 chars).</param>
    /// <param name="actingUserId">The user making the edit.</param>
    /// <param name="now">The time of the edit (UTC).</param>
    /// <returns>What happened. Saving the same text again changes nothing.</returns>
    public CommentChange Edit(string newText, Guid actingUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newText);

        if (actingUserId != AuthorUserId)
        {
            return new CommentChange(CommentOutcome.Forbidden, Changed: false);
        }

        if (IsDeleted)
        {
            return new CommentChange(CommentOutcome.AlreadyDeleted, Changed: false);
        }

        if (string.Equals(Text, newText, StringComparison.Ordinal))
        {
            return new CommentChange(CommentOutcome.Ok, Changed: false);
        }

        Text = newText;
        EditedAt = now;
        return new CommentChange(CommentOutcome.Ok, Changed: true);
    }

    /// <summary>
    /// Deletes the comment: the text is erased permanently and a placeholder remains (spec FR-024). Only the author may
    /// delete (FR-017), and a deleted comment cannot be deleted again or restored.
    /// </summary>
    /// <param name="actingUserId">The user deleting the comment.</param>
    /// <param name="now">The time of the deletion (UTC).</param>
    /// <returns>What happened.</returns>
    public CommentChange Delete(Guid actingUserId, DateTimeOffset now)
    {
        if (actingUserId != AuthorUserId)
        {
            return new CommentChange(CommentOutcome.Forbidden, Changed: false);
        }

        if (IsDeleted)
        {
            return new CommentChange(CommentOutcome.AlreadyDeleted, Changed: false);
        }

        Text = null;
        DeletedAt = now;
        return new CommentChange(CommentOutcome.Ok, Changed: true);
    }
}
