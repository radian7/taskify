using FluentValidation;
using Taskify.Notifications.Api.Data;
using Taskify.Security;
using Taskify.Security.ApiKeys;
using Taskify.Security.Data;
using Taskify.Security.Users;
using Taskify.ServiceDefaults;

// Notifications API: per-user in-app notifications, the internal event intake and the real-time board hub
// (research R2, R5, R10). Events come from the Projects and Tasks APIs; the hub is used only by the Web server.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTaskifySecurity();
builder.AddTaskifyDbContext<NotificationsDbContext>("notificationsdb");

// The five users are owned by the Projects API; this service reads them over HTTPS and caches them (research R4).
builder.Services.AddRemoteUserDirectory();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var app = builder.Build();

// Research R3: migrations are applied at startup in development; deployments run a migration step instead.
if (app.Environment.IsDevelopment())
{
    // Migrate as the administrator, then (re)create this service's least-privilege role, which is what the service runs as.
    await app.Services.MigrateAndSecureAsync<NotificationsDbContext>(
        app.Configuration,
        "notificationsdb",
        new DatabaseRolePlan("notificationsdb", "notifications_app", ReadOnlyTables: [], AppendOnlyTables: []),
        options => new NotificationsDbContext(options));
}

app.UseTaskifySecurity();

app.MapDefaultEndpoints();
app.MapOpenApi().RequireCallers(Callers.Web).AllowAnonymousActingUser();

app.Run();

/// <summary>Entry point marker so integration tests can reference the host.</summary>
public partial class Program;
