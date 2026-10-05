using Microsoft.EntityFrameworkCore;
using Taskify.Contracts;
using Taskify.Projects.Api.Domain;
using Taskify.Security.Outbox;

namespace Taskify.Projects.Api.Data;

/// <summary>
/// The Projects service database (<c>projectsdb</c>). Only this service reads or writes it (constitution
/// Principle III); other services reach the data through the Projects API.
/// </summary>
/// <param name="options">The context options.</param>
public sealed class ProjectsDbContext(DbContextOptions<ProjectsDbContext> options) : DbContext(options)
{
    /// <summary>Gets the five predefined users (read-only seed data).</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Gets the projects, including the three sample projects (spec FR-005).</summary>
    public DbSet<Project> Projects => Set<Project>();

    /// <summary>Gets the events waiting to be delivered to the Notifications API.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            // displayName: "1–100 chars, unique" (data-model.md)
            entity.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            entity.HasIndex(u => u.DisplayName).IsUnique();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsRequired();

            // "Exactly one ProductManager, four Engineer" (data-model.md; spec FR-001).
            entity.HasData(
                new User(SeedIds.Maya, "Maya Chen", UserRole.ProductManager),
                new User(SeedIds.Liam, "Liam Novak", UserRole.Engineer),
                new User(SeedIds.Priya, "Priya Patel", UserRole.Engineer),
                new User(SeedIds.Tomasz, "Tomasz Wiśniewski", UserRole.Engineer),
                new User(SeedIds.Jordan, "Jordan Lee", UserRole.Engineer));
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(p => p.Id);
            // name and description lengths are enforced by the API in user-perceived characters (spec FR-019);
            // the columns are plain text (data-model.md).
            entity.Property(p => p.Name).IsRequired();
            entity.Property(p => p.Description);
            entity.HasIndex(p => p.CreatedAt);

            // Three sample projects, available the first time Taskify is opened (spec FR-005).
            // "createdByUserId: Must be a seeded user (FR-004)": they were created by the Product Manager.
            entity.HasData(
                new Project(SeedIds.MobileAppLaunch, "Mobile App Launch", "Ship the first version of the Taskify mobile app.", SeedIds.Maya, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero)),
                new Project(SeedIds.WebsiteRedesign, "Website Redesign", "Refresh the public website: faster, clearer and responsive.", SeedIds.Maya, new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero)),
                new Project(SeedIds.InternalTools, "Internal Tools", "Small improvements that save the team time every week.", SeedIds.Maya, new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero)));
        });

        modelBuilder.ConfigureOutbox();
    }
}
