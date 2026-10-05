using Taskify.Contracts;
using Taskify.Tasks.Api.Domain;

namespace Taskify.UnitTests.Domain;

/// <summary>
/// The comment lifecycle (spec FR-015 to FR-017, FR-024; User Story 4): active, edited, deleted. Only the author may
/// change a comment, and a deleted comment is gone for good.
/// </summary>
public class CommentTests
{
    private static readonly DateTimeOffset Posted = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 10, 4, 10, 30, 0, TimeSpan.Zero);
    private static readonly Guid Author = SeedIds.Priya;
    private static readonly Guid Other = SeedIds.Liam;

    private static Comment NewComment(string text = "First draft") => new(Guid.NewGuid(), Guid.NewGuid(), Author, text, Posted);

    [Fact]
    public void A_new_comment_is_active_with_its_author_text_and_time()
    {
        var comment = NewComment("Hello");

        Assert.Equal(Author, comment.AuthorUserId);
        Assert.Equal("Hello", comment.Text);
        Assert.Equal(Posted, comment.CreatedAt);
        Assert.Null(comment.EditedAt);
        Assert.Null(comment.DeletedAt);
        Assert.False(comment.IsDeleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_comment_cannot_be_created_with_no_text(string text) =>
        Assert.Throws<ArgumentException>(() => NewComment(text));

    // ----------------------------------------------------------------------------------------------------- edit

    [Fact]
    public void The_author_can_edit_and_the_comment_shows_it_was_edited()
    {
        var comment = NewComment();

        var change = comment.Edit("Second draft", Author, Later);

        Assert.Equal(new CommentChange(CommentOutcome.Ok, Changed: true), change);
        Assert.Equal("Second draft", comment.Text);
        Assert.Equal(Later, comment.EditedAt);
        Assert.Equal(Posted, comment.CreatedAt);   // the posting time never changes
        Assert.Equal(Author, comment.AuthorUserId);
    }

    [Fact]
    public void Each_edit_updates_the_edited_time()
    {
        var comment = NewComment();

        comment.Edit("v2", Author, Later);
        comment.Edit("v3", Author, Later.AddMinutes(5));

        Assert.Equal("v3", comment.Text);
        Assert.Equal(Later.AddMinutes(5), comment.EditedAt);
    }

    [Fact]
    public void Saving_the_same_text_again_changes_nothing_and_does_not_mark_the_comment_edited()
    {
        var comment = NewComment("Same");

        var change = comment.Edit("Same", Author, Later);

        Assert.Equal(new CommentChange(CommentOutcome.Ok, Changed: false), change);
        Assert.Null(comment.EditedAt);
    }

    [Fact]
    public void Another_user_cannot_edit_and_nothing_changes()
    {
        var comment = NewComment("Mine");

        var change = comment.Edit("Hijacked", Other, Later);

        Assert.Equal(new CommentChange(CommentOutcome.Forbidden, Changed: false), change);
        Assert.Equal("Mine", comment.Text);
        Assert.Null(comment.EditedAt);
    }

    [Fact]
    public void Even_the_product_manager_cannot_edit_someone_elses_comment()
    {
        var comment = NewComment("Mine");

        Assert.Equal(CommentOutcome.Forbidden, comment.Edit("Overruled", SeedIds.Maya, Later).Outcome);
        Assert.Equal("Mine", comment.Text);
    }

    [Fact]
    public void A_deleted_comment_cannot_be_edited_back_to_life()
    {
        var comment = NewComment();
        comment.Delete(Author, Later);

        var change = comment.Edit("Back again", Author, Later.AddMinutes(1));

        Assert.Equal(new CommentChange(CommentOutcome.AlreadyDeleted, Changed: false), change);
        Assert.Null(comment.Text);
        Assert.True(comment.IsDeleted);
        Assert.Null(comment.EditedAt);
    }

    // --------------------------------------------------------------------------------------------------- delete

    [Fact]
    public void The_author_can_delete_which_erases_the_text_and_records_when()
    {
        var comment = NewComment("Secret words");

        var change = comment.Delete(Author, Later);

        Assert.Equal(new CommentChange(CommentOutcome.Ok, Changed: true), change);
        Assert.Null(comment.Text);
        Assert.Equal(Later, comment.DeletedAt);
        Assert.True(comment.IsDeleted);
        Assert.Equal(Author, comment.AuthorUserId);   // the placeholder still says who wrote it
        Assert.Equal(Posted, comment.CreatedAt);
    }

    [Fact]
    public void Another_user_cannot_delete_and_the_text_stays()
    {
        var comment = NewComment("Mine");

        var change = comment.Delete(Other, Later);

        Assert.Equal(new CommentChange(CommentOutcome.Forbidden, Changed: false), change);
        Assert.Equal("Mine", comment.Text);
        Assert.False(comment.IsDeleted);
    }

    [Fact]
    public void A_comment_cannot_be_deleted_twice_and_the_first_deletion_time_stands()
    {
        var comment = NewComment();
        comment.Delete(Author, Later);

        var change = comment.Delete(Author, Later.AddHours(1));

        Assert.Equal(new CommentChange(CommentOutcome.AlreadyDeleted, Changed: false), change);
        Assert.Equal(Later, comment.DeletedAt);
    }

    [Fact]
    public void A_stranger_learns_nothing_about_a_deleted_comment_because_ownership_is_checked_first()
    {
        var comment = NewComment();
        comment.Delete(Author, Later);

        Assert.Equal(CommentOutcome.Forbidden, comment.Edit("x", Other, Later).Outcome);
        Assert.Equal(CommentOutcome.Forbidden, comment.Delete(Other, Later).Outcome);
    }

    [Fact]
    public void An_edited_comment_can_still_be_deleted_and_the_edit_time_is_kept()
    {
        var comment = NewComment();
        comment.Edit("Edited", Author, Later);

        comment.Delete(Author, Later.AddMinutes(10));

        Assert.True(comment.IsDeleted);
        Assert.Null(comment.Text);
        Assert.Equal(Later, comment.EditedAt);
    }

    [Fact]
    public void Editing_to_empty_text_is_refused_by_the_domain_as_well_as_by_validation() =>
        Assert.Throws<ArgumentException>(() => NewComment().Edit("   ", Author, Later));
}
