using FluentValidation;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Data;
using Taskify.Security.Outbox;
using Taskify.Security.Users;
using Taskify.ServiceDefaults;
using Taskify.Tasks.Api.Clients;
using Taskify.Tasks.Api.Data;
using Taskify.Tasks.Api.Endpoints;

// Tasks API: tasks, status history and comments (research R2). Reachable only from the Web app.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTaskifySecurity();
builder.AddTaskifyDbContext<TasksDbContext>("tasksdb");

// The five users are owned by the Projects API; this service reads them over HTTPS and caches them (research R4).
builder.Services.AddRemoteUserDirectory();
builder.Services.AddTaskifyHttpClient(ProjectsApiClient.HttpClientName, "https://projects-api");
builder.Services.AddScoped<ProjectsApiClient>();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddOpenApi();
builder.Services.AddOutboxDispatcher<TasksDbContext>();

var app = builder.Build();

// Research R3: migrations are applied at startup in development; deployments run a migration step instead.
if (app.Environment.IsDevelopment())
{
    // Migrate as the administrator, then (re)create this service's least-privilege role, which is what the service runs as.
    await app.Services.MigrateAndSecureAsync<TasksDbContext>(
        app.Configuration,
        "tasksdb",
        new DatabaseRolePlan("tasksdb", "tasks_app", ReadOnlyTables: [], AppendOnlyTables: ["status_changes"]),
        options => new TasksDbContext(options));
}

app.UseTaskifySecurity();

app.MapDefaultEndpoints();
app.MapOpenApi().RequireCallers(Callers.Web).AllowAnonymousActingUser();
app.MapTaskReadEndpoints();
app.MapMoveEndpoints();
app.MapTaskWriteEndpoints();
app.MapCommentEndpoints();

app.Run();

/// <summary>Entry point marker so integration tests can reference the host.</summary>
public partial class Program;
