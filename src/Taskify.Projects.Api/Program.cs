using FluentValidation;
using Taskify.Projects.Api.Data;
using Taskify.Projects.Api.Endpoints;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Data;
using Taskify.Security.Outbox;
using Taskify.Security.Users;
using Taskify.ServiceDefaults;

// Projects API: projects and the read-only directory of predefined users (research R2).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTaskifySecurity();
builder.AddTaskifyDbContext<ProjectsDbContext>("projectsdb");

builder.Services.AddSingleton<IUserDirectory, LocalUserDirectory>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddOpenApi();
builder.Services.AddOutboxDispatcher<ProjectsDbContext>();

var app = builder.Build();

// Research R3: migrations are applied at startup in development; deployments run a migration step instead.
if (app.Environment.IsDevelopment())
{
    // Migrate as the administrator, then (re)create this service's least-privilege role, which is what the service runs as.
    await app.Services.MigrateAndSecureAsync<ProjectsDbContext>(
        app.Configuration,
        "projectsdb",
        new DatabaseRolePlan("projectsdb", "projects_app", ReadOnlyTables: ["users"], AppendOnlyTables: []),
        options => new ProjectsDbContext(options));
}

app.UseTaskifySecurity();

app.MapDefaultEndpoints();
app.MapOpenApi().RequireCallers(Callers.Web).AllowAnonymousActingUser();
app.MapUserEndpoints();
app.MapProjectEndpoints();

app.Run();

/// <summary>Entry point marker so integration tests can reference the host.</summary>
public partial class Program;
