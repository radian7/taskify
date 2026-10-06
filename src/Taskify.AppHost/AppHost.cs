// Orchestration for Taskify (plan: Project Structure; research R2, R3, R8, R16).
//
// Security intent, in one place:
//  * Least privilege: each resource receives only its own database, its own API key and the keys of the
//    callers it accepts (research R8 key matrix). No service can read another service's database.
//  * Encryption in transit: every endpoint is HTTPS (no HTTP endpoint exists on any API), and PostgreSQL
//    serves TLS with a certificate generated at startup (research R16).
//  * Only the Web resource is reachable from outside the Aspire network.
//  * All secrets are Aspire secret parameters: user secrets locally (see scripts/init-dev-secrets.ps1), the
//    platform secret store when deployed. Nothing secret is committed to the repository.
using Aspire.Hosting.ApplicationModel;
using Taskify.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// ---- Secret parameters --------------------------------------------------------------------------------------
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

// Each service runs as its own least-privilege database role (research R3; plan Constitution Check "Least privilege").
// The administrator password above is used only to migrate and to create these roles at startup.
var projectsDbPassword = builder.AddParameter("projects-db-password", secret: true);
var tasksDbPassword = builder.AddParameter("tasks-db-password", secret: true);
var notificationsDbPassword = builder.AddParameter("notifications-db-password", secret: true);
var webKey = builder.AddParameter("web-api-key", secret: true);
var projectsKey = builder.AddParameter("projects-api-key", secret: true);
var tasksKey = builder.AddParameter("tasks-api-key", secret: true);
var notificationsKey = builder.AddParameter("notifications-api-key", secret: true);

// Encrypts the Web app's Data Protection key ring at rest; that key ring protects the selected-user cookie.
var dataProtectionCert = builder.AddParameter("dataprotection-cert", secret: true);
var dataProtectionCertPassword = builder.AddParameter("dataprotection-cert-password", secret: true);

// ---- PostgreSQL over TLS (research R3, R16) -----------------------------------------------------------------
// The server certificate exists only in memory and inside the container. uid 999 is the "postgres" user of the
// Debian-based official image, which is why the image tag is pinned: PostgreSQL refuses a key it does not own.
const int PostgresUid = 999;
var devCertificate = DevPostgresCertificate.Generate();
const UnixFileMode ReadableByAll = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImageTag("17-bookworm")
    .WithContainerFiles(
        "/etc/taskify-certs",
        [
            new ContainerFile { Name = "server.crt", Contents = devCertificate.CertificatePem, Owner = PostgresUid, Group = PostgresUid, Mode = ReadableByAll },
            new ContainerFile { Name = "server.key", Contents = devCertificate.PrivateKeyPem, Owner = PostgresUid, Group = PostgresUid, Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite },
        ])
    .WithArgs(
        "-c", "ssl=on",
        "-c", "ssl_cert_file=/etc/taskify-certs/server.crt",
        "-c", "ssl_key_file=/etc/taskify-certs/server.key");

// Data survives restarts by default (a named volume). The automated tests turn this off with
// "--Taskify:PersistData=false", so every test run starts from a fresh database with exactly the sample data and never
// touches (or is confused by) the data of a developer's own run.
if (!string.Equals(builder.Configuration["Taskify:PersistData"], "false", StringComparison.OrdinalIgnoreCase))
{
    // The volume name can be overridden ("--Taskify:DataVolumeName=..."). Only the persistence test does, so it uses
    // its own throwaway volume and never the developer's.
    postgres.WithDataVolume(builder.Configuration["Taskify:DataVolumeName"] is { Length: > 0 } volumeName ? volumeName : "taskify-postgres-data");
}

// One database per service (constitution Principle III).
var projectsDb = postgres.AddDatabase("projectsdb");
var tasksDb = postgres.AddDatabase("tasksdb");
var notificationsDb = postgres.AddDatabase("notificationsdb");

// ---- Services -----------------------------------------------------------------------------------------------
// Key matrix (research R8):
//   Projects API accepts:      web, tasks, notifications
//   Tasks API accepts:         web
//   Notifications API accepts: web, projects, tasks
// Every service presents its own key (ApiKeys__OwnKey) when it calls another service.
var projectsApi = builder.AddProject<Projects.Taskify_Projects_Api>("projects-api")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(projectsDb).WaitFor(projectsDb)
    .WithEnvironment("Taskify__Database__AppRole", "projects_app")
    .WithEnvironment("Taskify__Database__AppPassword", projectsDbPassword)
    .WithEnvironment("ApiKeys__OwnKey", projectsKey)
    .WithEnvironment("ApiKeys__Accepted__web", webKey)
    .WithEnvironment("ApiKeys__Accepted__tasks", tasksKey)
    .WithEnvironment("ApiKeys__Accepted__notifications", notificationsKey);

var notificationsApi = builder.AddProject<Projects.Taskify_Notifications_Api>("notifications-api")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(notificationsDb).WaitFor(notificationsDb)
    .WithReference(projectsApi).WaitFor(projectsApi)
    .WithEnvironment("Taskify__Database__AppRole", "notifications_app")
    .WithEnvironment("Taskify__Database__AppPassword", notificationsDbPassword)
    .WithEnvironment("ApiKeys__OwnKey", notificationsKey)
    .WithEnvironment("ApiKeys__Accepted__web", webKey)
    .WithEnvironment("ApiKeys__Accepted__projects", projectsKey)
    .WithEnvironment("ApiKeys__Accepted__tasks", tasksKey);

var tasksApi = builder.AddProject<Projects.Taskify_Tasks_Api>("tasks-api")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(tasksDb).WaitFor(tasksDb)
    .WithReference(projectsApi).WaitFor(projectsApi)
    .WithReference(notificationsApi)
    .WithEnvironment("Taskify__Database__AppRole", "tasks_app")
    .WithEnvironment("Taskify__Database__AppPassword", tasksDbPassword)
    .WithEnvironment("ApiKeys__OwnKey", tasksKey)
    .WithEnvironment("ApiKeys__Accepted__web", webKey);

// The Projects API publishes ProjectCreated to the Notifications API through its outbox.
projectsApi.WithReference(notificationsApi);

// The only resource with an external endpoint (spec Assumptions: trusted network only in phase 1).
builder.AddProject<Projects.Taskify_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WithReference(projectsApi).WaitFor(projectsApi)
    .WithReference(tasksApi).WaitFor(tasksApi)
    .WithReference(notificationsApi).WaitFor(notificationsApi)
    .WithEnvironment("ApiKeys__OwnKey", webKey)
    .WithEnvironment("DataProtection__Certificate", dataProtectionCert)
    .WithEnvironment("DataProtection__CertificatePassword", dataProtectionCertPassword);

builder.Build().Run();
