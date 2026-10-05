using Microsoft.EntityFrameworkCore;
using Taskify.Contracts.Events;
using Taskify.Projects.Api.Data;
using Taskify.Projects.Api.Domain;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Audit;
using Taskify.Security.Errors;
using Taskify.Security.Outbox;
using Taskify.Security.RateLimiting;
using Taskify.Security.Users;
using Taskify.Security.Validation;

namespace Taskify.Projects.Api.Endpoints;

/// <summary>A project as returned by the API (contracts/projects-api.yaml, schema <c>Project</c>).</summary>
/// <param name="Id">The project ID.</param>
/// <param name="Name">The name (1–100 characters). Display it as plain text.</param>
/// <param name="Description">The description (up to 1,000 characters), or <see langword="null"/>. Display it as plain text.</param>
/// <param name="CreatedByUserId">The creator.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
public sealed record ProjectDto(Guid Id, string Name, string? Description, Guid CreatedByUserId, DateTimeOffset CreatedAt)
{
    /// <summary>Maps a project to its API shape.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The DTO.</returns>
    public static ProjectDto From(Project project) =>
        new(project.Id, project.Name, project.Description, project.CreatedByUserId, project.CreatedAt);
}

/// <summary>The body of <c>POST /api/projects</c> (contracts/projects-api.yaml, schema <c>CreateProjectRequest</c>).</summary>
/// <param name="Name">The name: 1–100 user-perceived characters after trimming.</param>
/// <param name="Description">The description: up to 1,000 user-perceived characters after trimming, or <see langword="null"/>.</param>
public sealed record CreateProjectRequest(string Name, string? Description = null);

/// <summary>Project routes (spec FR-005 to FR-007).</summary>
public static class ProjectEndpoints
{
    /// <summary>Maps the project routes.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects");

        // Every user sees every project (spec FR-007). Only the Web app lists them.
        group.MapGet("/", async (ProjectsDbContext db, CancellationToken cancellationToken) =>
        {
            var projects = await db.Projects.AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(projects.Select(ProjectDto.From));
        })
            .RequireCallers(Callers.Web)
            .RequireReads()
            .WithName("listProjects")
            .Produces<IEnumerable<ProjectDto>>();

        // The Tasks API also reads one project, to check that it exists before creating a task in it.
        group.MapGet("/{projectId:guid}", async (Guid projectId, ProjectsDbContext db, IAuditLogger audit, CancellationToken cancellationToken) =>
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                audit.Rejected(AuditOutcome.NotFound, "Project", projectId);
                return Problems.NotFound("Project");
            }

            return Results.Ok(ProjectDto.From(project));
        })
            .RequireCallers(Callers.Web, Callers.Tasks)
            .RequireReads()
            .WithName("getProject")
            .Produces<ProjectDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Any user may create a project (spec FR-006). Only the Web app does so.
        group.MapPost("/", CreateAsync)
            .RequireCallers(Callers.Web)
            .RequireWrites()
            .RequireValidation<CreateProjectRequest>()
            .WithName("createProject")
            .Produces<ProjectDto>(StatusCodes.Status201Created);

        return app;
    }

    /// <summary>
    /// Creates a project (spec FR-006). The creator is the acting user and the time is the server's (FR-004). The
    /// project and its <c>ProjectCreated</c> event are saved together or not at all (research R4).
    /// </summary>
    /// <param name="request">The new project (already validated).</param>
    /// <param name="db">The projects database.</param>
    /// <param name="user">The acting user.</param>
    /// <param name="time">The clock.</param>
    /// <param name="audit">Records the change.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>201 with the new project.</returns>
    public static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        ProjectsDbContext db,
        ICurrentActingUser user,
        TimeProvider time,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNowMicroseconds();
        var project = new Project(
            Guid.CreateVersion7(),
            InputNormalizer.Trim(request.Name),
            InputNormalizer.TrimToNull(request.Description),   // "empty string stored as null"
            user.UserId,
            now);

        db.Projects.Add(project);
        OutboxWriter.Add(db, EventTypes.ProjectCreated, user.UserId, new ProjectCreatedPayload(project.Id, project.Name), now);
        await db.SaveChangesAsync(cancellationToken);

        audit.Changed(AuditActions.ProjectCreated, "Project", project.Id);
        return Results.Created($"/api/projects/{project.Id}", ProjectDto.From(project));
    }
}
