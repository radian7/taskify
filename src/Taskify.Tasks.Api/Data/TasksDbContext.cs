using Microsoft.EntityFrameworkCore;
using Taskify.Security.Outbox;
using Taskify.Tasks.Api.Domain;

namespace Taskify.Tasks.Api.Data;

/// <summary>
/// The Tasks service database (<c>tasksdb</c>): tasks, status history and comments, one aggregate (research R2).
/// Only this service reads or writes it (constitution Principle III).
/// </summary>
/// <param name="options">The context options.</param>
public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    /// <summary>Gets the tasks, including the sample tasks (spec FR-005).</summary>
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>Gets the status history: one row per move (spec FR-023). The service's database role can only read and insert.</summary>
    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();

    /// <summary>Gets the comments, including deleted ones (kept as placeholders, spec FR-024).</summary>
    public DbSet<Comment> Comments => Set<Comment>();

    /// <summary>Gets the events waiting to be delivered to the Notifications API.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(t => t.Id);
            // title and description lengths are enforced by the API in user-perceived characters (spec FR-019).
            entity.Property(t => t.Title).IsRequired();
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Board loading: one project's tasks by column, newest first (data-model.md).
            entity.HasIndex(t => new { t.ProjectId, t.Status, t.CreatedAt })
                .IsDescending(false, false, true);

            entity.HasData(SeedData.Tasks());
        });

        modelBuilder.Entity<StatusChange>(entity =>
        {
            entity.ToTable("status_changes");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.FromStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

            // "taskId: FK → Task". Tasks are never deleted in phase 1, so deleting one is refused rather than cascading.
            entity.HasOne<TaskItem>().WithMany().HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Restrict);

            // The history of one task, newest first.
            entity.HasIndex(s => new { s.TaskId, s.MovedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.ToTable("comments");
            entity.HasKey(c => c.Id);
            // text: "1–2,000 chars after trim (FR-015); set to null on delete (FR-024)". Enforced by the API in
            // user-perceived characters; the column is plain text and nullable.
            entity.Property(c => c.Text);
            entity.Ignore(c => c.IsDeleted);

            // "taskId: FK → Task". Tasks are never deleted in phase 1.
            entity.HasOne<TaskItem>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);

            // One task's thread, oldest first.
            entity.HasIndex(c => new { c.TaskId, c.CreatedAt });
        });
        modelBuilder.ConfigureOutbox();
    }
}
