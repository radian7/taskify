---

description: "Task list for Taskify Kanban Board (phase 1), regenerated after the 2026-10-04 plan revision"
---

# Tasks: Taskify Kanban Board

**Input**: Design documents from `/specs/001-taskify-kanban-board/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R16), data-model.md, contracts/ (3 OpenAPI +
1 AsyncAPI), quickstart.md

**Tests**: Included. The constitution requires them: Principle II says every validation rule needs
automated tests for accepted and rejected input, and Quality Gate 2 requires contract tests for
every service interface. research.md R11 sets the test levels and the required test sets.

**Organization**: Tasks are grouped by the six user stories in spec.md (US1–US6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story the task belongs to (US1–US6)
- Paths follow the plan's layout: `src/Taskify.*/` and `tests/Taskify.*/` at the repository root

## Conventions that apply to every task

- **Docs (Principle IV)**: every public type and member has an XML doc comment (CS1591 is an
  error). Validation rules and security decisions also get an inline comment explaining why.
- **READMEs and ADRs in the same change (Principle IV, Quality Gate 4)**: each story ends with a
  docs task that updates the `README.md` of every service the story changed (responsibility,
  endpoints and accepted callers, configuration, events, run and test) and adds an ADR in
  `docs/adr/` for any significant decision the story made. A story is not done, and its work is
  not merged, until that task is done. Phase 6b backfills the docs for Setup through US4.
- **Validation (Principle II, FR-019)**:
  - Every endpoint goes through the shared `ValidationEndpointFilter<T>` (T030).
  - Strings are trimmed *before* checks, and an empty string in an optional field is stored as
    `null`.
  - Text lengths are counted with `TextLength` (T026): grapheme clusters, plus an abuse guard of
    16 UTF-16 code units per allowed character.
  - Unknown JSON properties are rejected. Errors return as RFC 9457 Problem Details with no
    internal detail.
- **Identity and keys (R8)**:
  - Every API request needs an `X-Api-Key` the route accepts, following the R8 key matrix.
    Missing or unknown key → `401`; a known key not allowed on the route → `403`.
  - Every route except the exempt ones needs `X-Taskify-User` naming a seeded user, otherwise
    `400`. Exempt: `GET /api/users`, `GET /api/users/{userId}`, `/internal/events`, `/health`,
    `/alive`.
- **Transport (R16)**: service URIs are always `https://<resource>`, never `https+http://`.
- **Outbox**: every data change writes its domain event to `OutboxMessage` in the same
  transaction as the change. Payloads must match
  [contracts/events.asyncapi.yaml](contracts/events.asyncapi.yaml) and never include
  descriptions or comment text.
- **Audit (FR-022, FR-032, R13)**: every data change and every rejection (`400`, `401`, `403`,
  `404` on a write, `409`, `413`, `422`, `429`) logs an audit event through `IAuditLogger`
  (T029). The event includes `sourceIp` and `callerService`. Titles, descriptions and comment
  text are never logged.
- **Concurrency (FR-011, R14)**: the last save wins, with no concurrency token. Every write runs
  in one transaction with its outbox row, and with its history row for moves.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the solution, projects and build/CI configuration.

- [X] T001 Create `Taskify.slnx` at the repository root, plus `src/` and `tests/` folders, `docs/adr/`, and a `.gitignore` for .NET (bin, obj, `*.user`, `*.suo`, `.vs/`, `appsettings.*.local.json`, `*.pfx`, `*.key`)
- [X] T002 Create `global.json` pinning the .NET 10 SDK (`10.0.1xx`, `rollForward: latestFeature`) at the repository root
- [X] T003 Create `Directory.Build.props` at the repository root: `TargetFramework` `net10.0`, `LangVersion` 14, `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, `GenerateDocumentationFile` true, `WarningsAsErrors` includes `CS1591`, `AnalysisLevel` `latest-recommended`, `EnforceCodeStyleInBuild` true. Test projects override `GenerateDocumentationFile` to false
- [X] T004 Create `Directory.Packages.props` with central package management (`ManagePackageVersionsCentrally` true) and versions for Aspire 13.x (`Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.Hosting.Testing`), `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore.Design` 10.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x, `FluentValidation` and `FluentValidation.DependencyInjectionExtensions`, `Microsoft.AspNetCore.SignalR.Client`, `xunit.v3`, `bunit`, `Microsoft.Playwright`, `Microsoft.NET.Test.Sdk`, `JsonSchema.Net` and `YamlDotNet` (for the AsyncAPI contract tests)
- [X] T005 Create the Aspire AppHost project `src/Taskify.AppHost/Taskify.AppHost.csproj` (Aspire AppHost SDK) with an empty `src/Taskify.AppHost/AppHost.cs`, and add it to `Taskify.slnx`
- [X] T006 [P] Create the Aspire ServiceDefaults project `src/Taskify.ServiceDefaults/` (from the `aspire-servicedefaults` template: OpenTelemetry, `/health` and `/alive` health checks, HTTP resilience, service discovery) and add it to `Taskify.slnx`
- [X] T007 [P] Create class libraries `src/Taskify.Contracts/Taskify.Contracts.csproj` and `src/Taskify.Security/Taskify.Security.csproj` (Security has a `FrameworkReference` to `Microsoft.AspNetCore.App` and references FluentValidation and EF Core), and add both to `Taskify.slnx`
- [X] T008 [P] Create web API projects `src/Taskify.Projects.Api/`, `src/Taskify.Tasks.Api/` and `src/Taskify.Notifications.Api/` (minimal APIs, `Microsoft.AspNetCore.OpenApi`, EF Core Npgsql via Aspire, references to ServiceDefaults, Contracts and Security). Create the folders `Endpoints/`, `Domain/`, `Validation/`, `Data/` (plus `Clients/` in Tasks and Notifications, and `Hubs/` in Notifications). Remove every `http` profile from each `Properties/launchSettings.json`, leaving HTTPS only (R16). Add the projects to `Taskify.slnx`
- [X] T009 [P] Create the Blazor Web App project `src/Taskify.Web/` (Interactive Server render mode, no WebAssembly, HTTPS-only launch profile) with `Components/Pages/`, `Components/Board/`, `Components/Shared/` and `Services/` folders, references to ServiceDefaults, Contracts and Security, and add it to `Taskify.slnx`
- [X] T010 [P] Create test projects `tests/Taskify.UnitTests/` (xUnit v3), `tests/Taskify.Web.Tests/` (xUnit v3 + bUnit), `tests/Taskify.IntegrationTests/` (xUnit v3 + `Aspire.Hosting.Testing` + `JsonSchema.Net` + `YamlDotNet`, references the AppHost) and `tests/Taskify.E2ETests/` (xUnit v3 + `Microsoft.Playwright`), and add them to `Taskify.slnx`
- [X] T011 [P] Create `.editorconfig` at the repository root with C# style rules and set `dotnet_diagnostic.CA2100.severity = error` (SQL built from user input) and `dotnet_diagnostic.CA3001.severity = error` (SQL injection)
- [X] T012 [P] Create the CI pipeline `.github/workflows/ci.yml` (R12). Steps:
  - restore; `dotnet build -warnaserror`;
  - `dotnet test` for Unit, Web and Integration tests (Docker available);
  - `dotnet list package --vulnerable --include-transitive`, failing on High or Critical;
  - CodeQL analysis for C#;
  - secret scanning (gitleaks action);
  - `npx @asyncapi/cli validate specs/001-taskify-kanban-board/contracts/events.asyncapi.yaml` (R15);
  - a grep step that fails if `MarkupString` appears under `src/Taskify.Web/` except on lines marked `// markup-allowed: <reason>` (R9).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared contracts, security plumbing, TLS orchestration, the user directory, the outbox
and the Web shell. Every user story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Shared contracts (`Taskify.Contracts`)

- [X] T013 [P] Create `TaskStatus` enum (`ToDo`, `InProgress`, `InReview`, `Done`, in this order) and `UserRole` enum (`ProductManager`, `Engineer`) in `src/Taskify.Contracts/Enums.cs`, serialized as strings
- [X] T014 [P] Create `SeedIds` static class with fixed UUIDs for the five users (Maya Chen – ProductManager; Liam Novak, Priya Patel, Tomasz Wiśniewski, Jordan Lee – Engineer) and the three sample projects (Mobile App Launch, Website Redesign, Internal Tools) in `src/Taskify.Contracts/SeedIds.cs`
- [X] T015 [P] Create the versioned event types (v1) in `src/Taskify.Contracts/Events/`: `EventEnvelope` (`eventId` UUID, `type` string, `version` int = 1, `occurredAt` UTC date-time, `actorUserId` UUID, `payload`), the `EventTypes` constants (`ProjectCreated`, `TaskCreated`, `TaskUpdated`, `TaskAssigned`, `TaskMoved`, `CommentAdded`, `CommentEdited`, `CommentDeleted`), and one payload record per type. Each record has exactly the fields of its `*Envelope.payload` schema in `contracts/events.asyncapi.yaml`, with nullable `assigneeUserId`/`previousAssigneeUserId` where the schema allows null. Add a doc comment on each record citing the AsyncAPI message name and stating that descriptions and comment text are never included
- [X] T016 [P] Create the hub message records `BoardChanged`, `TaskChanged`, `ProjectListChanged` and `NotificationCreated` in `src/Taskify.Contracts/Hub/HubMessages.cs`, matching the `*Payload` schemas in `contracts/events.asyncapi.yaml`

### Security plumbing (`Taskify.Security`)

- [X] T017 [P] Create the `IUserDirectory` interface (`Task<bool> ExistsAsync(Guid userId)`, `Task<IReadOnlyList<UserInfo>> GetAllAsync()`) and the `UserInfo` record (`Id`, `DisplayName`, `Role`) in `src/Taskify.Security/Users/IUserDirectory.cs`
- [X] T018 [P] Create `ApiKeyOptions` in `src/Taskify.Security/ApiKeys/ApiKeyOptions.cs`. It maps caller name (`web`, `projects`, `tasks`, `notifications`) → key, is read from configuration, and is never logged. Create the endpoint metadata `AllowCallersAttribute` and the extension `RequireCallers(params string[] callers)` in `src/Taskify.Security/ApiKeys/AllowCallers.cs`
- [X] T019 Create `ApiKeyMiddleware` in `src/Taskify.Security/ApiKeys/ApiKeyMiddleware.cs`:
  - finds the caller by comparing `X-Api-Key` against each configured key with `CryptographicOperations.FixedTimeEquals`;
  - missing or unknown key → `401` Problem Details;
  - a known caller not listed in the endpoint's `AllowCallers` metadata → `403` Problem Details;
  - sets `HttpContext.Items["Caller"]`;
  - skips only `/health` and `/alive`;
  - comment that this implements the R8 key matrix (deviation D2).
  (depends on T018)
- [X] T020 [P] Create `ActingUserMiddleware` in `src/Taskify.Security/Users/ActingUserMiddleware.cs`, `ICurrentActingUser` in `src/Taskify.Security/Users/ICurrentActingUser.cs`, and `SkipActingUserAttribute` and the extension `AllowAnonymousActingUser()` in `src/Taskify.Security/Users/SkipActingUser.cs`. The middleware reads `X-Taskify-User`, which must be a UUID and exist in `IUserDirectory`, otherwise it returns `400` Problem Details. It exposes the user ID to endpoints. Endpoints with `SkipActingUser` metadata are exempt
- [X] T021 [P] Create `ClientIpMiddleware` and `IClientIpAccessor` in `src/Taskify.Security/Audit/ClientIp.cs`. When the caller is `web`, the middleware reads `X-Taskify-Client-Ip`, accepts it only if `IPAddress.TryParse` succeeds, and exposes it for audit; it ignores the header from any other caller. Comment the trust rule (R8)
- [X] T022 [P] Create `ProblemDetailsSetup` in `src/Taskify.Security/Errors/ProblemDetailsSetup.cs`. It registers `AddProblemDetails` with a customization that adds `traceId` and removes the exception detail. It adds a global exception handler that returns a generic `500` with no stack trace, type name or SQL text, and maps `BadHttpRequestException` for body size to `413`. It also defines helpers for `403`, `404`, `409` and `422`
- [X] T023 [P] Configure JSON options in `src/Taskify.Security/Json/StrictJsonSetup.cs`: `UnmappedMemberHandling = Disallow` (unknown fields → `400`), enums as strings with integer values rejected, and case-insensitive property names off
- [X] T024 [P] Create `RequestLimitsSetup` in `src/Taskify.Security/Hosting/RequestLimitsSetup.cs`. It sets Kestrel `MaxRequestBodySize` to 1 MB (1,048,576 bytes) on every API (R7) and audits `413` rejections
- [X] T025 [P] Create rate-limit policies in `src/Taskify.Security/RateLimiting/RateLimitPolicies.cs` (FR-031, R9). All use `SlidingWindowRateLimiter` with a 1-minute window, 6 segments and `QueueLimit = 0`:
  - `writes`: 60 per minute, partitioned by acting user;
  - `reads`: 300 per minute, partitioned by acting user, or by caller name for exempt requests with no acting user;
  - `internal-events`: 3,000 per minute, partitioned by caller;
  - `web-ip`: 1,200 per minute, partitioned by client IP (used by the Web app).
  `OnRejected` returns `429` Problem Details ("Too many requests, please wait a moment") with a `Retry-After` header and audits the rejection. Add the extensions `RequireWrites()` and `RequireReads()`. Comment that limits are per instance and that phase 1 runs one instance per service
- [X] T026 [P] Create `TextLength` in `src/Taskify.Security/Validation/TextLength.cs`:
  - `int Count(string s)` returns `StringInfo.LengthInTextElements` (grapheme clusters, UAX #29);
  - `bool ExceedsGuard(string s, int maxChars)` returns `s.Length > maxChars * 16`;
  - a doc comment explaining FR-019 and the guard (a family emoji is 11 code units, a kiss emoji with skin tones about 15).
  Create the trim helper `InputNormalizer` (trims; returns null for empty optional strings) in `src/Taskify.Security/Validation/InputNormalizer.cs`. Create the FluentValidation extension `MustHaveTextLength(min, max)` in `src/Taskify.Security/Validation/ValidationRules.cs`. It trims, checks the guard first and then the grapheme count, with messages like "Title must be 1–200 characters".
- [X] T027 [P] Create the `OutboxMessage` entity in `src/Taskify.Security/Outbox/OutboxMessage.cs`. Fields: `id` UUID ("Generated; also the event ID used for de-duplication"), `type` text ("Event type name"), `payload` jsonb ("Event body"), `occurredAt` timestamptz ("Same transaction as the change"), `dispatchedAt` timestamptz nullable ("Set after delivery succeeds"), `attempts` int ("Retry count; backoff capped at 1 minute"), `deadLetteredAt` timestamptz nullable ("Set when the receiver rejects the event permanently (`400` or `403`); the dispatcher never retries it")
- [X] T028 [P] Create the `OutboxWriter` helper (`Add(DbContext, eventType, actorUserId, payload)`, which stages an outbox row in the current change tracker so it saves in the same `SaveChanges` transaction) in `src/Taskify.Security/Outbox/OutboxWriter.cs`
- [X] T029 Create `IAuditLogger` and `AuditLogger` in `src/Taskify.Security/Audit/AuditLogger.cs`. They write structured log events with the fields from data-model.md "Audit event": `action`, `actingUserId`, `previousUserId`, `entityType`, `entityId`, `outcome` (`Succeeded`, `Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `TooLarge`, `UnknownReference`, `RateLimited`), `callerService`, `sourceIp` and `correlationId`. There are no parameters for text content. Every non-`Succeeded` outcome also increments the OpenTelemetry counter `taskify.rejections`, tagged with `reason`, `caller` and `service` (R13) (depends on T021)
- [X] T030 Create `ValidationEndpointFilter<T>` in `src/Taskify.Security/Validation/ValidationEndpointFilter.cs`. It resolves `IValidator<T>`, returns `400` with field errors when validation fails, and audits the rejection. It never echoes rejected values back (depends on T022, T029)
- [X] T031 Create `SecurityServiceExtensions.AddTaskifySecurity(callerConfig)` and `UseTaskifySecurity()` in `src/Taskify.Security/SecurityServiceExtensions.cs`. Middleware order: exception handler → `ApiKeyMiddleware` → `ClientIpMiddleware` → `ActingUserMiddleware` → rate limiter. It also wires up T022–T025 and T029–T030 (depends on T019–T030)
- [X] T032 Create the generic `OutboxDispatcher<TDbContext> : BackgroundService` in `src/Taskify.Security/Outbox/OutboxDispatcher.cs`. It polls rows where `dispatchedAt` and `deadLetteredAt` are both null, in `occurredAt` order, and POSTs each `EventEnvelope` to `https://notifications-api/internal/events` with the service's own API key. It sets `dispatchedAt` on a `202`. On a `400` or `403` it sets `deadLetteredAt`, writes an error-level `EventDeadLettered` audit event (event ID and type only) and increments the OpenTelemetry counter `taskify.outbox.deadlettered`; it never retries that row. Otherwise (`404`, 5xx, timeout, connection error) it increments `attempts` with exponential backoff capped at 1 minute. It never logs the payload (depends on T027, T029)
- [X] T033 [P] Unit tests for `TextLength` and `MustHaveTextLength` in `tests/Taskify.UnitTests/Security/TextLengthTests.cs`:
  - whitespace-only and leading or trailing spaces;
  - exactly min and max;
  - max+1;
  - 200 family emoji (`👨‍👩‍👧‍👦`) accepted as 200;
  - 200 `é` written as `e` + combining accent accepted as 200;
  - 200 flags accepted as 200;
  - a Zalgo string with a valid grapheme count but over the 16× guard rejected.
- [X] T034 [P] Unit tests for the middleware in `tests/Taskify.UnitTests/Security/MiddlewareTests.cs`:
  - `ApiKeyMiddleware`: missing → 401, unknown → 401, known but not allowed on the route → 403, allowed → passes, health path skipped;
  - `ActingUserMiddleware`: missing, non-UUID and unknown → 400; valid → passes; skip metadata respected;
  - `ClientIpMiddleware`: the header is honored from `web`, ignored from `tasks`, and an invalid IP is ignored.
- [X] T035 [P] Unit tests for rate-limit policies in `tests/Taskify.UnitTests/Security/RateLimitTests.cs`: the defaults and window settings match FR-031; 60 writes in a minute pass and the 61st is rejected; 300 reads pass and the 301st is rejected; each partition has its own budget; the partition key prefers acting user, then caller, then IP. The framework's sliding-window limiter has no injectable clock, so the two time-based checks run a scaled-down copy (1.2 s window, same six segments): a steady rate below the limit is never rejected, and permits return once the window has moved on. The real one-minute check at 1 write per second (SC-010) is in T149
- [X] T036 [P] Unit tests for `OutboxDispatcher` in `tests/Taskify.UnitTests/Outbox/OutboxDispatcherTests.cs`, with a fake `HttpMessageHandler`: retries with capped backoff, and marks dispatched on 202. On 400 and 403 it sets `deadLetteredAt`, writes `EventDeadLettered`, increments `taskify.outbox.deadlettered` and never re-sends the row. It leaves the row pending on 404, 5xx or timeout

### Orchestration with TLS (`Taskify.AppHost`)

- [X] T037 Create `DevPostgresCertificate` in `src/Taskify.AppHost/DevPostgresCertificate.cs`. It generates a self-signed RSA-2048 server certificate for `postgres` with `CertificateRequest` at AppHost startup, valid for 30 days, kept in memory only. It returns the PEM certificate and key. Doc comment: development only; deployments use a managed PostgreSQL with `VerifyFull` (R16)
- [X] T038 Configure `src/Taskify.AppHost/AppHost.cs`:
  - PostgreSQL with a secret password parameter and a data volume. Put the T037 certificate into the container with `WithContainerFiles("/var/lib/postgresql/certs", …)`, owned by `postgres` (uid 999) with key mode `0600`, and add `WithArgs("-c","ssl=on","-c","ssl_cert_file=/var/lib/postgresql/certs/server.crt","-c","ssl_key_file=/var/lib/postgresql/certs/server.key")`. Create databases `projectsdb`, `tasksdb` and `notificationsdb`, with the connection string option `SSL Mode=Require`;
  - secret parameters `web-api-key`, `projects-api-key`, `tasks-api-key` and `notifications-api-key`, plus `dataprotection-cert` and `dataprotection-cert-password` (given to `web` only; T050);
  - resources `projects-api`, `tasks-api`, `notifications-api` and `web`, each with only its own database, its own key and the keys of the callers it accepts (R8 key matrix);
  - `WithReference` and `WaitFor` for these links: tasks-api → projects-api and notifications-api; projects-api → notifications-api; notifications-api → projects-api; web → all three APIs;
  - HTTPS endpoints only, with no HTTP endpoint on any API, and `WithExternalHttpEndpoints()` on `web` only.
  Comment the least-privilege and TLS intent (depends on T037) **Implemented as:** each service also gets its own least-privilege database role (`projects-db-password`, `tasks-db-password`, `notifications-db-password` parameters; `Taskify__Database__AppRole` and `AppPassword` environment). The service migrates as the administrator, then creates the role (`DatabaseRoles`) and runs as it. `scripts/init-dev-secrets.ps1` creates the three passwords. See `DatabaseRoleTests`.

### Projects service foundation: user directory

- [X] T039 Create the `User` entity in `src/Taskify.Projects.Api/Domain/User.cs`. Fields: `id` UUID ("Fixed seed value"), `displayName` text ("1–100 chars, unique"), `role` enum `ProductManager` | `Engineer` ("Exactly one `ProductManager`, four `Engineer`"). The type has no setters (read-only, FR-003)
- [X] T040 Create `ProjectsDbContext` with `Users` and `OutboxMessages` in `src/Taskify.Projects.Api/Data/ProjectsDbContext.cs`. Add a unique index on `displayName`, seed the five users from `SeedIds` with `HasData`, and add the initial EF Core migration under `src/Taskify.Projects.Api/Data/Migrations/` (depends on T039)
- [X] T041 Create `LocalUserDirectory : IUserDirectory` (reads `ProjectsDbContext.Users`, cached in memory) in `src/Taskify.Projects.Api/Data/LocalUserDirectory.cs`
- [X] T042 Implement `GET /api/users` (all five users) and `GET /api/users/{userId}` (`404` if unknown) per contracts/projects-api.yaml in `src/Taskify.Projects.Api/Endpoints/UserEndpoints.cs`. Both use `.AllowAnonymousActingUser()` (no `X-Taskify-User` needed), `.RequireCallers("web","tasks","notifications")` and `.RequireReads()`
- [X] T043 Wire up `src/Taskify.Projects.Api/Program.cs`: ServiceDefaults; `AddNpgsqlDbContext<ProjectsDbContext>("projectsdb")`; migrations applied at startup in Development; `AddTaskifySecurity` with known callers `web`, `tasks` and `notifications`; FluentValidation validators from the assembly; OpenAPI at `/openapi/v1.json`; `OutboxDispatcher<ProjectsDbContext>`; endpoint groups

### Tasks and Notifications service foundation

- [X] T044 [P] Create `ProjectsApiClient` in `src/Taskify.Tasks.Api/Clients/ProjectsApiClient.cs`: a typed `HttpClient` with base address `https://projects-api` that sends the `tasks` API key, with `GetUsersAsync()` and `ProjectExistsAsync(Guid)`. `ProjectExistsAsync` forwards the acting user and client IP of the current request. Create `RemoteUserDirectory : IUserDirectory` in `src/Taskify.Tasks.Api/Clients/RemoteUserDirectory.cs`. It caches the user list in memory after the first successful fetch (users never change in phase 1, R4) and retries on failure without caching the error
- [X] T045 [P] Create `TasksDbContext` with `OutboxMessages` (task entities are added by the stories) in `src/Taskify.Tasks.Api/Data/TasksDbContext.cs`, and wire up `src/Taskify.Tasks.Api/Program.cs`: ServiceDefaults; `AddNpgsqlDbContext<TasksDbContext>("tasksdb")`; startup migrations in Development; `AddTaskifySecurity` with known caller `web`; `RemoteUserDirectory`; OpenAPI; `OutboxDispatcher<TasksDbContext>`
- [X] T046 [P] Create `NotificationsDbContext` (empty model for now) in `src/Taskify.Notifications.Api/Data/NotificationsDbContext.cs`, and a Projects user-directory client (`https://projects-api`, `notifications` key; same caching as T044) in `src/Taskify.Notifications.Api/Clients/ProjectsUserDirectory.cs`. Wire up `src/Taskify.Notifications.Api/Program.cs`: ServiceDefaults; `AddNpgsqlDbContext<NotificationsDbContext>("notificationsdb")`; `AddTaskifySecurity` with known callers `web`, `projects` and `tasks`; SignalR; OpenAPI

### Web shell (`Taskify.Web`)

- [X] T047 Create `ClientIpCapture` in `src/Taskify.Web/Services/ClientIpCapture.cs`. It is a scoped service that stores `HttpContext.Connection.RemoteIpAddress` from the initial HTTP request of each circuit (through `IHttpContextAccessor` in `App.razor` `OnInitialized`) for later API calls (R8) **Implemented as:** the Blazor circuit has no `HttpContext`, so `SelectedUserMiddleware` puts the browser IP (and the validated selected user) on the request principal as claims; `CircuitIdentity` reads them once per circuit and `ClientIpCapture` exposes the IP from it.
- [X] T048 Create typed API clients in `src/Taskify.Web/Services/ApiClients/`: `ProjectsClient` (users and projects), `TasksClient` (tasks, moves, history, comments) and `NotificationsClient`, all with `https://` base addresses. They share the `DelegatingHandler` `TaskifyHeadersHandler.cs`, which adds `X-Api-Key` (web key), `X-Taskify-User` from `CurrentUserService` (left out for the user routes) and `X-Taskify-Client-Ip` from `ClientIpCapture`. Problem Details map into an `ApiError` result with field errors, and `429` becomes "Too many requests, please wait a moment" (depends on T047) **Implemented as:** the headers are added per request by `ApiClientBase` (not a `DelegatingHandler`, whose HTTP-client-factory scope cannot see per-circuit state); the API key is a default header of each HTTP client.
- [X] T049 Create `CurrentUserService` (scoped per circuit) in `src/Taskify.Web/Services/CurrentUserService.cs`:
  - holds the selected user ID and raises `Changed`;
  - persists the choice in an HttpOnly, Secure, SameSite=Strict, data-protected cookie so it survives a refresh (R8), and re-validates the cookie value against `GET /api/users` on load;
  - writes the `UserSelected` audit event (chosen user, previous user or null, time, `sourceIp` from `ClientIpCapture`) on every selection and switch, and `UserSelectionRestored` when the cookie restores a selection on a new circuit (FR-032);
  - discards a cookie that fails Data Protection unprotection or names a GUID that is not a seeded user: no selection is restored, the cookie is deleted, and a `RequestRejected` audit event with outcome `Validation` is written (Principle II).
  (depends on T047, T029) **Implemented as:** the cookie is read by `SelectedUserMiddleware` and written by `POST /session/select` (`SessionEndpoints`), because only an HTTP response can set an `HttpOnly` cookie; `CurrentUserService` reads the circuit's selection and audits `UserSelectionRestored`; the selection endpoint audits `UserSelected` with the previous user and IP, and invalid or unknown cookies are rejected, deleted and audited by the middleware.
- [X] T050 Wire up `src/Taskify.Web/Program.cs` with:
  - ServiceDefaults, Razor components with Interactive Server, and `IHttpContextAccessor`;
  - antiforgery;
  - HTTPS redirection and HSTS;
  - security-header middleware: `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: no-referrer`;
  - the `web-ip` rate limiter;
  - Data Protection keys persisted to a volume, so the cookie survives a restart, and **encrypted at rest** with `ProtectKeysWithCertificate`. The certificate comes from the `dataprotection-cert` secret parameter (base64 PFX) and `dataprotection-cert-password`; nothing is stored in the repository (Principle I);
  - the typed clients from T048.
  Add a comment that `MarkupString` is banned for user content (R9)
- [X] T051 Create the layout and routing in `src/Taskify.Web/Components/Layout/MainLayout.razor` and `src/Taskify.Web/Components/Routes.razor`. The header shows the selected user and a "Switch user" button. Pages other than `/` redirect to `/` when no user is selected. Add a shared `NotFound.razor` page in `src/Taskify.Web/Components/Pages/NotFound.razor` with a link back to the project list **Implemented as:** "Switch user" links to `/` without clearing the selection, so the next choice is audited with the previous user (FR-032); `/` and `/not-found` use `SimpleLayout`.
- [X] T052 [P] Create the `ErrorBanner.razor` component, which shows `ApiError` messages and field errors as plain text (including the rate-limit message), in `src/Taskify.Web/Components/Shared/ErrorBanner.razor`
- [X] T053 [P] Create the `CharacterCounter.razor` component in `src/Taskify.Web/Components/Shared/CharacterCounter.razor`. It shows "n / max" using `TextLength.Count`, the same code the APIs use (FR-019), so what the user sees always matches what the API enforces

### Foundational integration tests

- [X] T054 [P] Create the integration test fixture `TaskifyAppFixture` in `tests/Taskify.IntegrationTests/Infrastructure/TaskifyAppFixture.cs`. It starts the AppHost with `DistributedApplicationTestingBuilder`, provides test API keys for all four callers, waits for all resources to be healthy, and exposes `HttpClient`s for each API with helpers that set `X-Api-Key`, `X-Taskify-User` and `X-Taskify-Client-Ip`, plus a log-capture provider for audit assertions **Implemented as:** the fixture lives in the shared project `tests/Taskify.TestSupport/TaskifyAppFixture.cs`, so the integration and E2E test assemblies use the same one; it starts the AppHost once per test assembly.
- [X] T055 [P] Integration tests for cross-cutting API behavior in `tests/Taskify.IntegrationTests/Security/CrossCuttingTests.cs`:
  - no `X-Api-Key` → `401`; the Tasks key on a Tasks API route → `401`, because it is not a configured caller there;
  - the Notifications key on `GET /api/projects` → `403`, with a body matching the `CallerNotAllowed` response in contracts/projects-api.yaml;
  - `GET /api/users` with no `X-Taskify-User` → `200`;
  - an unknown `X-Taskify-User` on `/api/projects` → `400`;
  - an unknown JSON property → `400`;
  - a 1.1 MB body → `413`;
  - the error body has no stack trace;
  - only `web` has an external endpoint;
  - `/health` → 200 on all services. **Implemented as:** the checks that need a route taking a user or a body are added with the first such routes: the unknown or missing acting user (400) in `ProjectsReadContractTests` (T058); the unknown JSON property (400) and 1.1 MB body (413) with `POST /api/projects` (T093). The caller-not-allowed 403 is checked on `/openapi/v1.json` (Web-only) and on `GET /api/projects` (T058).
- [X] T056 [P] TLS integration tests in `tests/Taskify.IntegrationTests/Security/TlsTests.cs` (R16): each API has no `http` endpoint in the app model, and a plain-HTTP connection to its port fails; each service database session reports `ssl = true` (`SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()`)
- [X] T057 [P] Contract tests for `GET /api/users` and `GET /api/users/{userId}` (exactly five users, one ProductManager, schema matches contracts/projects-api.yaml) in `tests/Taskify.IntegrationTests/Contracts/UsersContractTests.cs`

**Checkpoint**: The AppHost starts all resources over TLS, the user directory works for every
allowed caller, and the security plumbing rejects bad requests. User stories can now begin.

---

## Phase 3: User Story 1 - Pick a user and view a project board (Priority: P1) 🎯 MVP

**Goal**: Pick one of five users without a password, list the three sample projects, open a board
with four columns, and see your own tasks highlighted. You can also switch user, and every
selection is audited.

**Independent Test**: Start Taskify with the sample data, select any user, open each of the three
sample projects, and confirm every task appears in the correct column and that the selected
user's tasks are highlighted.

### Tests for User Story 1

> Write these first and make sure they fail before you implement.

- [X] T058 [P] [US1] Contract tests for `GET /api/projects` (three seeded projects, newest first) and `GET /api/projects/{projectId}` (`404` for an unknown ID) in `tests/Taskify.IntegrationTests/Contracts/ProjectsReadContractTests.cs`
- [X] T059 [P] [US1] Contract tests for `GET /api/tasks?projectId=` (every seeded task appears once; `400` without or with a malformed `projectId`; newest first within each status) and `GET /api/tasks/{taskId}` (`404` for unknown) against the `TaskSummary` and `TaskDetail` schemas in contracts/tasks-api.yaml, in `tests/Taskify.IntegrationTests/Contracts/TasksReadContractTests.cs`
- [X] T060 [P] [US1] bUnit tests in `tests/Taskify.Web.Tests/Board/BoardTests.cs`:
  - four columns in the order To Do, In Progress, In Review, Done;
  - each card shows its title and assignee name, or "Unassigned";
  - the selected user's cards have the `card--mine` class and an "Assigned to you" label;
  - an empty project shows four empty columns and an "Add the first task" prompt.
- [X] T061 [P] [US1] bUnit tests for `UserSelect` in `tests/Taskify.Web.Tests/Pages/UserSelectTests.cs`: five users with role, no password field, choosing sets `CurrentUserService`, and "Switch user" returns to `/`. Unit tests for `CurrentUserService` in `tests/Taskify.Web.Tests/Services/CurrentUserServiceTests.cs`: select and switch write `UserSelected` with the previous user and IP; a cookie restore writes `UserSelectionRestored` (FR-032). Rejected cases (Principle II): a cookie that fails Data Protection unprotection and a cookie naming a GUID that is not a seeded user both result in no selection and a redirect to `/`; no `UserSelectionRestored` is written, a `RequestRejected` audit event with outcome `Validation` is written instead, and the cookie is deleted

### Implementation for User Story 1

- [X] T062 [P] [US1] Create the `Project` entity in `src/Taskify.Projects.Api/Domain/Project.cs`. Fields: `id` UUID ("Generated"), `name` text ("Required; 1–100 chars after trim (FR-006); duplicates allowed"), `description` text nullable ("0–1,000 chars after trim; empty string stored as null"), `createdByUserId` UUID ("Must be a seeded user (FR-004)"), `createdAt` timestamptz ("Set by the server"). Here "chars" means user-perceived characters (FR-019)
- [X] T063 [US1] Add `Projects` to `ProjectsDbContext`. Seed the three sample projects (Mobile App Launch, Website Redesign, Internal Tools) with `SeedIds` and fixed `createdAt` values, created by Maya Chen. Add migration `AddProjects` in `src/Taskify.Projects.Api/Data/Migrations/` (depends on T062)
- [X] T064 [US1] Implement `GET /api/projects` (all projects, "List order: `createdAt` descending") and `GET /api/projects/{projectId}` (`404` when missing) per contracts/projects-api.yaml in `src/Taskify.Projects.Api/Endpoints/ProjectEndpoints.cs`. Use `.RequireCallers("web")` on the list and `.RequireCallers("web","tasks")` on the single project, and `.RequireReads()` on both. Validate `projectId` as a UUID route parameter
- [X] T065 [P] [US1] Create the `TaskItem` entity in `src/Taskify.Tasks.Api/Domain/TaskItem.cs`. Fields: `id` UUID ("Generated"), `projectId` UUID ("Must exist in the Projects API (checked on create)"), `title` text ("Required; 1–200 chars after trim (FR-009)"), `description` text nullable ("0–5,000 chars after trim"), `status` enum `ToDo` | `InProgress` | `InReview` | `Done` ("New tasks start as `ToDo`"), `assigneeUserId` UUID nullable ("Null = unassigned; otherwise a seeded user (FR-010)"), `createdByUserId` UUID ("Acting user"), `createdAt` timestamptz ("Set by the server"), `updatedAt` timestamptz ("Updated on any change, including moves"). No delete operation and no concurrency token (R14)
- [X] T066 [US1] Add `Tasks` to `TasksDbContext`: status stored as a string, and index (`projectId`, `status`, `createdAt` desc). Seed 10 sample tasks per sample project: at least 2 in each column, assigned across all five users, with 2 unassigned per project (FR-005). Add migration `AddTasks` in `src/Taskify.Tasks.Api/Data/Migrations/` (depends on T065)
- [X] T067 [US1] Implement `GET /api/tasks?projectId=` (required UUID; returns `TaskSummary[]` sorted by status, then `createdAt` desc; `commentCount` is 0 until US4 adds comments) and `GET /api/tasks/{taskId}` (`TaskDetail`, `404` when missing) in `src/Taskify.Tasks.Api/Endpoints/TaskReadEndpoints.cs`, with DTOs in `src/Taskify.Tasks.Api/Endpoints/Dtos.cs`. Use `.RequireCallers("web")` and `.RequireReads()`
- [X] T068 [US1] Create the `UserSelect` page at route `/` in `src/Taskify.Web/Components/Pages/UserSelect.razor`. It lists the five users from `ProjectsClient` with display name and role ("Product Manager" or "Engineer"), has no password, and choosing a user calls `CurrentUserService.Select` and goes to `/projects`
- [X] T069 [US1] Create the `Projects` page at route `/projects` in `src/Taskify.Web/Components/Pages/Projects.razor`. It lists all projects newest first with name, description excerpt and creation date (which tells apart projects with the same name), and links to `/projects/{id}`
- [X] T070 [P] [US1] Create the `TaskCard.razor` component in `src/Taskify.Web/Components/Board/TaskCard.razor`. It shows the title and the assignee display name, or "Unassigned". When the assignee is the current user it adds the `card--mine` class (a distinct border and background) plus a visible "Assigned to you" label, so the cue does not rely on color alone (FR-013, FR-014). It links to the task details
- [X] T071 [P] [US1] Create the `BoardColumn.razor` component (heading, count, list of `TaskCard`, empty state) in `src/Taskify.Web/Components/Board/BoardColumn.razor`, and add styles in `src/Taskify.Web/wwwroot/app.css`. Use no inline `style` attributes, because of the CSP
- [X] T072 [US1] Create the `Board` page at route `/projects/{ProjectId:guid}` in `src/Taskify.Web/Components/Pages/Board.razor`. It loads the project and tasks, renders exactly four `BoardColumn`s in the order To Do, In Progress, In Review, Done, and shows "Add the first task" when there are no tasks. It shows `NotFound` when the project returns 404 (depends on T070, T071)
- [X] T073 [US1] Create the `TaskDetails` page at route `/projects/{ProjectId:guid}/tasks/{TaskId:guid}` in `src/Taskify.Web/Components/Pages/TaskDetails.razor`. It shows the title, description, column, assignee, creator and dates as plain text, and has empty sections for history (US2) and comments (US4). It shows `NotFound` when either ID returns 404
- [X] T074 [US1] Implement "Switch user" in `src/Taskify.Web/Components/Layout/MainLayout.razor`: call `CurrentUserService.Clear` (audited as part of the next selection), clear the cookie, then go to `/` **Implemented as:** superseded by T051. "Switch user" is a plain link to `/` that does not call `Clear` or delete the cookie, so the next selection is audited as a switch with the previous user (FR-032). The cookie is replaced when the new user is chosen (`POST /session/select`).

**Checkpoint**: US1 is fully usable on its own with the seed data. This is the MVP demo.

---

## Phase 4: User Story 2 - Move tasks across Kanban columns (Priority: P1)

**Goal**: Move a task to any other column by dragging it or with a keyboard "Move to…" menu.
The move is saved, recorded in a read-only status history, and visible after a reload.

**Independent Test**: Using the sample data, move a task to each of the other three columns in
turn, reload the board, and confirm the task stays in the last column it was moved to.

### Tests for User Story 2

- [X] T075 [P] [US2] Unit tests for `MoveTaskValidator` in `tests/Taskify.UnitTests/Validation/MoveTaskValidatorTests.cs`. Accepted: each of the four `toStatus` values. Rejected: missing, unknown string, integer, wrong case, extra property
- [X] T076 [P] [US2] Unit tests for the move domain rule in `tests/Taskify.UnitTests/Domain/TaskMoveTests.cs`: a move to a different status changes the status, creates a `StatusChange` with the correct from/to/user/time, and updates `updatedAt`. A move to the same status changes nothing and creates no history
- [X] T077 [P] [US2] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/MoveContractTests.cs`:
  - `POST /api/tasks/{taskId}/moves` returns `MoveTaskResult` with `changed=true`, and the task stays moved after a re-fetch;
  - a backwards move (Done → InProgress) is allowed;
  - a same-column move returns `changed=false` and `statusChange: null`, and adds no history row or outbox row;
  - an unknown task → `404`;
  - `GET /api/tasks/{taskId}/history` is newest first and has no `PUT`, `PATCH` or `DELETE` (→ `405`);
  - a direct `UPDATE` or `DELETE` on `status_changes` with the Tasks DB role fails;
  - two moves of the same task sent at the same time to different columns both succeed, the task ends in the column of the move committed last, and the history has both entries (FR-011).
- [X] T078 [P] [US2] bUnit tests in `tests/Taskify.Web.Tests/Board/MoveTests.cs`:
  - the `MoveToMenu` lists the three other columns and calls `TasksClient.MoveAsync`;
  - when the API rejects a move (including `429`), the card snaps back and `ErrorBanner` shows;
  - `StatusHistory` renders entries newest first with mover name, from → to, and time.

### Implementation for User Story 2

- [X] T079 [P] [US2] Create the `StatusChange` entity in `src/Taskify.Tasks.Api/Domain/StatusChange.cs`. Fields: `id` UUID ("Generated"), `taskId` UUID ("FK → Task"), `fromStatus` status enum ("Must differ from `toStatus`"), `toStatus` status enum, `movedByUserId` UUID ("Acting user"), `movedAt` timestamptz ("Set by the server"). Read-only after insert, with no public setters
- [X] T080 [US2] Add `TaskItem.MoveTo(TaskStatus to, Guid actingUserId, DateTimeOffset now)` returning `StatusChange?` in `src/Taskify.Tasks.Api/Domain/TaskItem.cs`. It returns null and changes nothing for a same-status move. Add a doc comment citing FR-012 and US2 scenario 4 (depends on T079)
- [X] T081 [US2] Add `StatusChanges` to `TasksDbContext` (FK to Task, index (`taskId`, `movedAt` desc)) and add migration `AddStatusHistory` in `src/Taskify.Tasks.Api/Data/Migrations/`. The migration must `REVOKE UPDATE, DELETE ON status_changes FROM` the Tasks API role, with a comment citing FR-023 and the threat "Tampering with status history" **Implemented as:** the table is created by this migration; the `REVOKE UPDATE, DELETE` is applied by `DatabaseRoles` on every start, right after the migrations, because the `tasks_app` role does not exist yet when the migration runs. It is conditional on the table existing, and it is verified by `MoveContractTests` and `DatabaseRoleTests` (T077) against the real database.
- [X] T082 [P] [US2] Create `MoveTaskValidator` (`toStatus` required, must be one of `ToDo` | `InProgress` | `InReview` | `Done`) in `src/Taskify.Tasks.Api/Validation/MoveTaskValidator.cs`
- [X] T083 [US2] Implement `POST /api/tasks/{taskId}/moves` in `src/Taskify.Tasks.Api/Endpoints/MoveEndpoints.cs` with `.RequireCallers("web")` and `.RequireWrites()`:
  - validates the body, returns `404` for an unknown task, and calls `MoveTo`;
  - when the task changed, saves the task, the `StatusChange` and an outbox `TaskMoved` event (`taskId`, `projectId`, `title`, `fromStatus`, `toStatus`, `assigneeUserId?`) in one transaction;
  - the last write wins, and a no-op writes nothing and sends no event;
  - returns `MoveTaskResult`, and audits `TaskMoved` (IDs and statuses only).
  (depends on T080–T082)
- [X] T084 [US2] Implement `GET /api/tasks/{taskId}/history` (newest first, `404` for an unknown task, no write routes, `.RequireReads()`) in `src/Taskify.Tasks.Api/Endpoints/HistoryEndpoints.cs`
- [X] T085 [P] [US2] Create the `MoveToMenu.razor` component (a keyboard-accessible button and menu listing the other three columns, with an ARIA menu role) in `src/Taskify.Web/Components/Board/MoveToMenu.razor`
- [X] T086 [US2] Add native HTML5 drag-and-drop:
  - `draggable="true"` and `@ondragstart` on `TaskCard`;
  - `@ondragover:preventDefault` and `@ondrop` on `BoardColumn`;
  - add `MoveToMenu` to `TaskCard`.
  A drop in the same column does nothing. The UI moves the card straight away, calls `TasksClient.MoveAsync`, and on error snaps the card back and shows `ErrorBanner` (R6). Files: `src/Taskify.Web/Components/Board/TaskCard.razor`, `src/Taskify.Web/Components/Board/BoardColumn.razor` and `src/Taskify.Web/Components/Pages/Board.razor` (depends on T085)
- [X] T087 [P] [US2] Create the `StatusHistory.razor` component (newest first: mover display name, from → to column names, and local time) in `src/Taskify.Web/Components/Shared/StatusHistory.razor`, and add it to `src/Taskify.Web/Components/Pages/TaskDetails.razor`

**Checkpoint**: US1 and US2 work. Moves are saved and the history is shown.

---

## Phase 5: User Story 3 - Create projects and tasks and assign them (Priority: P2)

**Goal**: Create projects and tasks, assign or unassign any predefined user, and edit a task's
title, description and assignee. The last save wins when edits overlap. Invalid input is rejected
with a clear message and changes nothing.

**Independent Test**: Create a new project, add three tasks, assign two of them to different users,
edit one task's assignee, and confirm everything shows up correctly on the board (new tasks start
in To Do).

### Tests for User Story 3

- [X] T088 [P] [US3] Unit tests for `CreateProjectValidator` in `tests/Taskify.UnitTests/Validation/CreateProjectValidatorTests.cs`:
  - name accepted at 1 and 100 characters, and at 100 accented or emoji characters;
  - name rejected when empty, whitespace-only, 101 characters, or over the 1,600 code-unit guard;
  - description accepted when null, empty or 1,000 characters; rejected at 1,001 characters;
  - an extra property is rejected.
- [X] T089 [P] [US3] Unit tests for `CreateTaskValidator` and `UpdateTaskValidator` in `tests/Taskify.UnitTests/Validation/TaskValidatorTests.cs`:
  - title accepted at 1 and 200 characters (including 200 family emoji);
  - title rejected when empty, whitespace-only, 201 characters, or over the 3,200 code-unit guard;
  - description rejected at 5,001 characters;
  - `projectId` must be a non-empty UUID;
  - `assigneeUserId`: null accepted, an unknown user rejected;
  - `UpdateTaskRequest` requires `title` and `assigneeUserId` to be present.
- [X] T090 [P] [US3] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/CreateEditContractTests.cs`:
  - `POST /api/projects` → `201` with the creator set from `X-Taskify-User`;
  - `POST /api/tasks` → `201` with status `ToDo`;
  - an unknown `projectId` or `assigneeUserId` → `422`;
  - `PUT /api/tasks/{id}` updates the fields and unassigns when `assigneeUserId` is null;
  - invalid input → `400`, and the stored data is unchanged (re-fetch and compare);
  - a `<script>` title is stored and returned verbatim, not executed;
  - two `PUT`s on the same task at the same time both return `200`, and the stored values equal the one committed last (FR-011, R14);
  - the outbox holds `ProjectCreated`, `TaskCreated`, `TaskUpdated` and `TaskAssigned` rows, and none of them contain a description.
- [X] T091 [P] [US3] bUnit tests in `tests/Taskify.Web.Tests/Forms/FormTests.cs` for:
  - `CreateProjectForm`;
  - `TaskForm` in create and edit modes: the assignee picker lists the five users plus "Unassigned", the `CharacterCounter` shows 200/200 for 200 emoji, and server field errors are shown;
  - plain-text rendering of `<script>` titles.

### Implementation for User Story 3

- [X] T092 [P] [US3] Create `CreateProjectValidator` in `src/Taskify.Projects.Api/Validation/CreateProjectValidator.cs`. Rules: `name` "Required; 1–100 chars after trim (FR-006)" via `MustHaveTextLength(1,100)`; `description` "0–1,000 chars after trim; empty string stored as null" via `MustHaveTextLength(0,1000)`
- [X] T093 [US3] Implement `POST /api/projects` in `src/Taskify.Projects.Api/Endpoints/ProjectEndpoints.cs` with `.RequireCallers("web")` and `.RequireWrites()`. It validates, trims the input, sets `createdByUserId` from the acting user and `createdAt` from the server, and saves the project with an outbox `ProjectCreated` event (`projectId`, `name`) in one transaction. It returns `201` with `Location` and audits the change (depends on T092)
- [X] T094 [P] [US3] Create `CreateTaskValidator` and `UpdateTaskValidator` in `src/Taskify.Tasks.Api/Validation/TaskValidators.cs`. Rules: `title` "Required; 1–200 chars after trim (FR-009)" via `MustHaveTextLength(1,200)`; `description` "0–5,000 chars after trim" via `MustHaveTextLength(0,5000)`; `assigneeUserId` "Null = unassigned; otherwise a seeded user (FR-010)", checked through `IUserDirectory`; `projectId` is a required UUID
- [X] T095 [US3] Implement `POST /api/tasks` in `src/Taskify.Tasks.Api/Endpoints/TaskWriteEndpoints.cs` with `.RequireCallers("web")` and `.RequireWrites()`:
  - validates the body and checks `projectId` with `ProjectsApiClient.ProjectExistsAsync` (`422` when missing);
  - an unknown assignee → `422`;
  - creates the task with status `ToDo`, `createdByUserId` set to the acting user and server timestamps;
  - saves it with an outbox `TaskCreated` event (`taskId`, `projectId`, `title`, `status`, `assigneeUserId?`) in one transaction;
  - returns `201` and audits the change.
  (depends on T094)
- [X] T096 [US3] Implement `PUT /api/tasks/{taskId}` in `src/Taskify.Tasks.Api/Endpoints/TaskWriteEndpoints.cs` with `.RequireWrites()`:
  - validates; an unknown task → `404`; an unknown assignee → `422`;
  - replaces the title, description and assignee as one unit and sets `updatedAt`, with no version check (last save wins, R14);
  - stages outbox `TaskUpdated` when the text changed, and `TaskAssigned` (with `previousAssigneeUserId?` and `assigneeUserId?`) when the assignee changed, both in the same transaction;
  - works for tasks in any column, including Done (clarification Q5), and audits the change.
- [X] T097 [P] [US3] Create the `CreateProjectForm.razor` component in `src/Taskify.Web/Components/Shared/CreateProjectForm.razor`, and add it to `src/Taskify.Web/Components/Pages/Projects.razor`. It has name and description inputs with `CharacterCounter` (100 and 1,000), shows server errors through `ErrorBanner`, and uses antiforgery
- [X] T098 [P] [US3] Create the `TaskForm.razor` component in `src/Taskify.Web/Components/Shared/TaskForm.razor` and `AssigneePicker.razor` in `src/Taskify.Web/Components/Shared/AssigneePicker.razor`. `TaskForm` has create and edit modes; a title with `CharacterCounter` of 200 and a description with `CharacterCounter` of 5,000; an `AssigneePicker` with the five users plus "Unassigned"; client-side limits only as a usability aid; and server field errors shown
- [X] T099 [US3] Add an "Add task" action to `src/Taskify.Web/Components/Pages/Board.razor` (opens `TaskForm` in create mode; the new task appears in To Do) and an "Edit" action to `src/Taskify.Web/Components/Pages/TaskDetails.razor` (opens `TaskForm` in edit mode) (depends on T098)

**Checkpoint**: US1–US3 work. Users can create and assign real work.

---

## Phase 6: User Story 4 - Comment on tasks (Priority: P3)

**Goal**: Read and post comments on any task. Authors can edit their own comments, which then show
"edited", or delete them, which leaves a "Comment deleted by [author]" placeholder. Comments by
other users cannot be edited or deleted.

**Independent Test**: Open a task, add a comment as one user, switch to another user, confirm the
comment is visible but cannot be edited or deleted, switch back, and edit and delete the comment.

### Tests for User Story 4

- [X] T100 [P] [US4] Unit tests for `CommentTextValidator` in `tests/Taskify.UnitTests/Validation/CommentTextValidatorTests.cs`. Accepted: 1 and 2,000 characters after trim, including 2,000 emoji. Rejected: empty, whitespace-only, 2,001 characters, over the 32,000 code-unit guard, extra property
- [X] T101 [P] [US4] Unit tests for the comment lifecycle in `tests/Taskify.UnitTests/Domain/CommentTests.cs`:
  - an author edit sets `editedAt`;
  - a non-author edit or delete → forbidden;
  - delete sets `text = null` and `deletedAt`;
  - editing or deleting an already deleted comment → conflict, and the comment cannot be restored.
- [X] T102 [P] [US4] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/CommentsContractTests.cs`:
  - `POST` → `201` with the author set to the acting user;
  - `GET` lists comments oldest first, including deleted placeholders (`isDeleted=true`, `text=null`);
  - an edit by a non-author → `403`;
  - an edit or delete of a deleted comment → `409`;
  - an unknown task or comment → `404`;
  - commenting on a Done task is allowed;
  - after delete, the comment text is gone from the database row and from the outbox payload;
  - `commentCount` on `TaskSummary` excludes deleted comments.
- [X] T103 [P] [US4] bUnit tests for `CommentThread` in `tests/Taskify.Web.Tests/Comments/CommentThreadTests.cs`:
  - author name and time shown, oldest first;
  - an "edited" indicator;
  - the placeholder "Comment deleted by Priya Patel" with the deletion time;
  - no edit or delete buttons on other users' comments or on deleted ones;
  - `<script>` text shown as plain text.

### Implementation for User Story 4

- [X] T104 [P] [US4] Create the `Comment` entity in `src/Taskify.Tasks.Api/Domain/Comment.cs`. Fields: `id` UUID ("Generated"), `taskId` UUID ("FK → Task"), `authorUserId` UUID ("Acting user at creation; never changes"), `text` text nullable ("1–2,000 chars after trim (FR-015); set to null on delete (FR-024)"), `createdAt` timestamptz ("Set by the server"), `editedAt` timestamptz nullable ("Set on each edit (FR-016)"), `deletedAt` timestamptz nullable ("Set on delete"). Add methods `Edit(text, actingUserId, now)` and `Delete(actingUserId, now)`, which return a result of `Ok`, `Forbidden` or `AlreadyDeleted` following the comment lifecycle in data-model.md
- [X] T105 [US4] Add `Comments` to `TasksDbContext` (FK to Task, index (`taskId`, `createdAt`)) and add migration `AddComments` in `src/Taskify.Tasks.Api/Data/Migrations/`. Update the `GET /api/tasks` query in `src/Taskify.Tasks.Api/Endpoints/TaskReadEndpoints.cs` so `commentCount` counts only comments that are not deleted (depends on T104)
- [X] T106 [P] [US4] Create `CommentTextValidator` (`text` "1–2,000 chars after trim (FR-015)" via `MustHaveTextLength(1,2000)`) in `src/Taskify.Tasks.Api/Validation/CommentTextValidator.cs`
- [X] T107 [US4] Implement the comment endpoints in `src/Taskify.Tasks.Api/Endpoints/CommentEndpoints.cs`, all with `.RequireCallers("web")`:
  - `GET /api/tasks/{taskId}/comments` (`.RequireReads()`): oldest first, including deleted placeholders; `404` for an unknown task.
  - `POST /api/tasks/{taskId}/comments` (`.RequireWrites()`): validated, with the acting user as author. Outbox `CommentAdded` with `taskId`, `projectId`, `title`, `commentId` and `assigneeUserId?`; no comment text.
  - `PUT /api/tasks/{taskId}/comments/{commentId}` (`.RequireWrites()`): `403` for a non-author, `409` when deleted. Outbox `CommentEdited`.
  - `DELETE /api/tasks/{taskId}/comments/{commentId}` (`.RequireWrites()`): `403` and `409` as above. Sets the text to null and returns the placeholder `Comment`. Outbox `CommentDeleted`.
  Audit every change and every `403` (IDs only, never the text) (depends on T104–T106)
- [X] T108 [US4] Create the `CommentThread.razor` component in `src/Taskify.Web/Components/Shared/CommentThread.razor`:
  - lists comments oldest first with the author display name and posted time, plus "edited" when `editedAt` is set;
  - shows deleted comments as "Comment deleted by {author}" with the `deletedAt` time;
  - an add-comment box with a `CharacterCounter` of 2,000 and server errors shown;
  - Edit and Delete buttons only when `authorUserId` is the current user and the comment is not deleted.
  Add the thread to `src/Taskify.Web/Components/Pages/TaskDetails.razor`
- [X] T109 [US4] Show the comment count on `src/Taskify.Web/Components/Board/TaskCard.razor` when `commentCount > 0`

**Checkpoint**: The four core user stories (US1–US4) work, and each can be tested on its own.

---

## Phase 6b: Documentation backfill for Setup through US4 (blocks US5)

**Purpose**: Setup, Foundational and US1–US4 were merged without the service READMEs and ADRs
that Principle IV and Quality Gate 4 require (analysis finding D1, 2026-10-05). These tasks,
moved here from Polish, document what is built *now*. Each README covers only the behavior that
exists today; US5 (T152) and US6 (T153) extend them. Every statement must match the code: read
the endpoints, `Program.cs` and `AppHost.cs` rather than copying from the plan, and note where
the code differs from the plan (the "Implemented as" notes in T038, T047–T049, T051, T055, T081).
T144 also moves here, so the CI check that the OpenAPI contracts match the code runs before US5
adds to them.

- [X] T138 [P] Write `src/Taskify.Projects.Api/README.md`: responsibility (projects and the user directory); endpoints with their accepted callers (R8 key matrix) and the routes exempt from `X-Taskify-User`; how to run and test it (`scripts/verify.ps1` filters); configuration (DB connection with TLS, the least-privilege DB role from T038, allowed API keys, secrets from `scripts/init-dev-secrets.ps1`); rate limits (per service, single instance, FR-031); dependencies; emitted events (`ProjectCreated`)
- [X] T139 [P] Write `src/Taskify.Tasks.Api/README.md`: responsibility (tasks, history, comments); endpoints and accepted callers; how to run and test it; configuration and the DB role; its dependency on the Projects API (project check, cached user directory); emitted events (no description or comment text); the last-save-wins rule (R14); the `status_changes` permission rule applied by `DatabaseRoles` (T081); the comment ownership and deletion rules
- [X] T140 [P] Write `src/Taskify.Notifications.Api/README.md` for the current foundation: responsibility (notifications and the board hub, both arriving in US5 and US6), configuration, the DB role, accepted callers, and the user-directory client. Mark the event intake, hub and notification sections as "added by US5/US6" so T152 and T153 fill them in
- [X] T141 [P] Write `src/Taskify.Web/README.md`: responsibility; pages; how the acting user is selected, stored (`SelectedUserMiddleware`, `POST /session/select`, the data-protected cookie, `CircuitIdentity`) and audited (FR-032); client-IP forwarding; the typed API clients and headers (`ApiClientBase`); security headers and CSP; Data Protection key encryption; the `MarkupString` ban (R9) and its CI check
- [X] T142 [P] Write the ADRs for decisions already in the code, in `docs/adr/` (one file each: context, decision, alternatives, consequences):
  - `0001-service-decomposition.md` (R2);
  - `0002-outbox-http-dispatch.md` (R4, including dead-lettering on `400`/`403`);
  - `0003-phase1-identity-and-api-keys.md` (R8, the key matrix, and deviations D1 and D2 with owner Adrian Rogalczyk, review date 2027-01-04, and expiry in phase 2);
  - `0004-encryption-in-transit.md` (R16, including the dev Postgres TLS approach and its fallback);
  - `0005-message-contracts-asyncapi.md` (R15);
  - `0006-per-service-database-roles.md` (T038 "Implemented as": migrate as administrator, run as a least-privilege role, `REVOKE` on `status_changes`);
  - `0007-per-service-rate-limits.md` (R9 and the 2026-10-05 clarification: limits count per service and per instance).
- [X] T144 (moved from Polish, analysis finding G3: Quality Gates 4 and 5 need the contracts kept in sync before US5 changes them) Add a CI step to `.github/workflows/ci.yml` that builds each API with `Microsoft.Extensions.ApiDescription.Server`, generates the OpenAPI documents and diffs them against `specs/001-taskify-kanban-board/contracts/*-api.yaml` (no copy of the contracts, R12), failing on drift

**Checkpoint**: every service built so far has a README that matches its code, every decision
made so far has an ADR, and CI fails when an API drifts from its OpenAPI contract. From here on each story keeps them current.

---

## Phase 7: User Story 5 - See other users' changes live (Priority: P3)

**Goal**: Changes by other users show on open boards, task details and project lists within 2
seconds without a refresh (FR-025, SC-008). After an interruption, screens catch up with nothing
lost (FR-026).

**Independent Test**: Open the same board in two browsers as different users, move, create, edit
and comment on a task in one, and confirm the other shows each change within 2 seconds without a
refresh.

### Tests for User Story 5

- [X] T110 [P] [US5] Create the AsyncAPI schema helper `AsyncApiSchemas` in `tests/Taskify.IntegrationTests/Infrastructure/AsyncApiSchemas.cs`. It loads `specs/001-taskify-kanban-board/contracts/events.asyncapi.yaml` with YamlDotNet, extracts `components.schemas`, and builds a `JsonSchema.Net` `JsonSchema` for each message payload, resolving local `$ref`s
- [X] T111 [P] [US5] Contract tests in `tests/Taskify.IntegrationTests/Contracts/AsyncApiContractTests.cs` (R15): for each of the 8 `Taskify.Contracts` event records and the 4 hub message records, serialize a fully populated sample and a minimal sample, and assert each validates against its AsyncAPI schema. Assert that adding an unknown field fails validation. For each of the 6 hub method messages (`JoinProject`, `LeaveProject`, `JoinTask`, `LeaveTask`, `JoinUser`, `LeaveUser`), assert that a sample argument (a bare UUID string, as SignalR sends it as invocation argument 0) with a non-empty GUID validates, and that the empty GUID and a non-UUID string are both rejected (depends on T110)
- [X] T112 [P] [US5] Unit tests for `EventEnvelopeValidator` and the per-type payload validators in `tests/Taskify.UnitTests/Validation/EventValidatorTests.cs`:
  - each of the 8 types: a valid payload is accepted;
  - rejected: unknown type, `version` other than 1, unknown or missing payload field, non-UUID IDs, a `title` over 200 characters or over the guard, an unknown status value.
- [X] T113 [P] [US5] Unit tests for `EventCallerPolicy` in `tests/Taskify.UnitTests/Security/EventCallerPolicyTests.cs`: `projects` may send only `ProjectCreated`; `tasks` may send only the other seven types; `web` and `notifications` may send nothing (`403`)
- [X] T114 [P] [US5] Unit tests for hub argument validation in `tests/Taskify.UnitTests/Hubs/BoardHubValidationTests.cs` (Principle II; contracts/realtime-hub.md):
  - `JoinProject`, `LeaveProject`, `JoinTask` and `LeaveTask` accept a non-empty GUID and reject `Guid.Empty` with `HubException("Invalid request")`;
  - `JoinUser` and `LeaveUser` accept each of the five seeded users and reject an unknown GUID and `Guid.Empty`;
  - the exception message never contains the argument value.
- [X] T115 [P] [US5] Integration tests in `tests/Taskify.IntegrationTests/Contracts/EventsContractTests.cs`:
  - `POST /internal/events` → `202`;
  - a repeated `eventId` → `202` with no second effect;
  - the wrong caller for the type → `403`;
  - the Web key → `403`;
  - a bad envelope → `400`;
  - a hub connection with a non-Web key is refused;
  - a task move in the Tasks API reaches a test SignalR client (Web key, joined to `project:{projectId}`) as `BoardChanged` within 2 s (SC-008);
  - FR-026: stop the `notifications-api` resource, move a task, restart it, and the outbox delivers the event and the client receives `BoardChanged` after reconnecting.

### Implementation for User Story 5

- [X] T116 [P] [US5] Create `ProcessedEvent` (`eventId` UUID primary key "`eventId` from the envelope", `receivedAt` timestamptz "Set by the server") in `src/Taskify.Notifications.Api/Domain/ProcessedEvent.cs`, add it to `NotificationsDbContext`, and add migration `AddProcessedEvents` in `src/Taskify.Notifications.Api/Data/Migrations/`
- [X] T117 [P] [US5] Create `EventEnvelopeValidator` and one payload validator per event type in `src/Taskify.Notifications.Api/Validation/EventValidators.cs`. Fields and nullability match the `*Envelope` schemas in contracts/events.asyncapi.yaml exactly; unknown fields are rejected; `version` must be 1; `title` uses `MustHaveTextLength(1,200)` and `name` uses `MustHaveTextLength(1,100)`; `actorUserId` and every user ID must exist in `IUserDirectory`. Comment that internal origin does not imply trust (Principle II)
- [X] T118 [P] [US5] Create `EventCallerPolicy` in `src/Taskify.Notifications.Api/Security/EventCallerPolicy.cs`. It maps the caller from `ApiKeyMiddleware` to its allowed event types (the Projects key only for `ProjectCreated`; the Tasks key for the rest) and returns `403` otherwise
- [X] T119 [P] [US5] Create `HubArgumentValidator` in `src/Taskify.Notifications.Api/Validation/HubArgumentValidator.cs`. It has `ValidateEntityId(Guid)` (rejects `Guid.Empty`) and `ValidateUserIdAsync(Guid)` (the ID must be in `IUserDirectory`). A failure throws `HubException("Invalid request")` and audits the rejection
- [ ] T120 [US5] Create `BoardHub` at `/hubs/board` in `src/Taskify.Notifications.Api/Hubs/BoardHub.cs`, with methods `JoinProject` and `LeaveProject` (`projectId: Guid`), `JoinTask` and `LeaveTask` (`taskId: Guid`), and `JoinUser` and `LeaveUser` (`userId: Guid`). Each method validates through `HubArgumentValidator` first. The connection auto-joins `projects`. Only the Web API key may connect: map the hub with `.RequireCallers("web")`, `.AllowAnonymousActingUser()` and a check in `OnConnectedAsync` (depends on T119)
- [ ] T121 [US5] Create `RealtimeBroadcaster` in `src/Taskify.Notifications.Api/Hubs/RealtimeBroadcaster.cs`. It maps each event type to its AsyncAPI hub message: `BoardChanged` to `project:{projectId}` (for TaskCreated, TaskUpdated, TaskAssigned, TaskMoved, CommentAdded and CommentDeleted); `TaskChanged` to `task:{taskId}` (for TaskUpdated, TaskAssigned, TaskMoved and the three comment events); `ProjectListChanged` to `projects`. Payloads contain IDs, the type, the actor and `occurredAt` only
- [ ] T122 [US5] Implement `POST /internal/events` in `src/Taskify.Notifications.Api/Endpoints/InternalEventEndpoints.cs`:
  - uses `.RequireCallers("projects","tasks")`, `.AllowAnonymousActingUser()` and the `internal-events` rate-limit policy;
  - validates the envelope and payload (T117) and checks the caller policy (T118);
  - in one transaction, inserts `ProcessedEvent` and runs the event handlers (US6 adds notification creation here); a duplicate `eventId` → `202` and nothing happens;
  - after the commit, calls `RealtimeBroadcaster`, then returns `202`;
  - audits rejections.
  (depends on T116–T118, T121)
- [ ] T123 [US5] Create `RealtimeBoardService` (singleton) in `src/Taskify.Web/Services/RealtimeBoardService.cs`:
  - keeps one `HubConnection` to `https://notifications-api/hubs/board` with the Web API key header and automatic reconnect;
  - reference-counts group membership across circuits (`SubscribeProject`, `SubscribeTask`, `SubscribeUser` return `IDisposable`);
  - raises C# events per group;
  - on reconnect, rejoins all groups and raises a `Resync` event (FR-026);
  - ignores `eventId`s it has already handled (bounded LRU of 1,000).
- [ ] T124 [US5] Create `CoalescingRefresher` in `src/Taskify.Web/Services/CoalescingRefresher.cs`. It runs at most one re-fetch per second per open screen: signals inside the window are merged into a single trailing re-fetch, which keeps viewers inside the 300 reads per minute limit (research R5)
- [ ] T125 [US5] Subscribe the pages to real-time signals, with every update re-fetched from the REST API rather than taken from the message contents. Each page uses `CoalescingRefresher` and `InvokeAsync(StateHasChanged)`, and disposes its subscriptions when the component is disposed:
  - `src/Taskify.Web/Components/Pages/Board.razor` subscribes to `project:{id}` and re-fetches tasks on `BoardChanged` or `Resync`;
  - `src/Taskify.Web/Components/Pages/TaskDetails.razor` subscribes to `task:{id}` and re-fetches the task, history and comments on `TaskChanged` or `Resync`;
  - `src/Taskify.Web/Components/Pages/Projects.razor` re-fetches the project list on `ProjectListChanged` or `Resync`.
  Every open screen handles `Resync`, because signals sent while the hub connection was down are lost and only a re-fetch brings the screen up to date (FR-026, research R5).
  (depends on T123, T124)
- [ ] T126 [P] [US5] bUnit and unit tests in `tests/Taskify.Web.Tests/Realtime/RealtimeTests.cs`, using a fake `RealtimeBoardService` and a fake time provider:
  - when a `BoardChanged` signal arrives, the board re-fetches and re-renders;
  - 10 signals within 1 second cause at most 2 re-fetches;
  - `Resync` causes a re-fetch on each of `Board`, `TaskDetails` and `Projects`;
  - the subscription is disposed when the component is disposed.
- [ ] T152 [US5] Update the docs for US5 (Principle IV, same change as the code):
  - `src/Taskify.Notifications.Api/README.md`: `/internal/events` (accepted callers, the caller policy per event type, envelope and payload validation, de-duplication, the `internal-events` rate limit), the `BoardHub` contract with its groups and argument validation, and the Web-key-only connection rule;
  - `src/Taskify.Web/README.md`: `RealtimeBoardService`, group reference counting, `Resync` after reconnect (FR-026), `CoalescingRefresher` and the read budget;
  - `src/Taskify.Projects.Api/README.md` and `src/Taskify.Tasks.Api/README.md`: where their outbox events are delivered and what happens while the Notifications API is down;
  - ADR `docs/adr/0008-realtime-signals-and-refetch.md` (R5: SignalR hub in the Notifications API, signals carry IDs only, the Web app re-fetches over REST).
  (depends on T110–T126)

**Checkpoint**: Boards update live. If the Notifications API is down, changes are still saved and
the board catches up after reconnect (outbox plus resync).

---

## Phase 8: User Story 6 - Receive in-app notifications (Priority: P3)

**Goal**: A user gets an in-app notification when another user assigns them a task, moves a task
assigned to them, or comments on a task assigned to them. Users can list notifications, see the
unread count, and mark one or all as read. Only the recipient can see a notification, and
notifications are removed after 30 days (FR-027–FR-030).

**Independent Test**: As one user, assign a task to a second user, move it, and comment on it;
switch to the second user and confirm three unread notifications that each link to the task; mark
all read and confirm the unread count is zero. Then act on a task assigned to yourself and confirm
no notification is created.

### Tests for User Story 6

- [ ] T127 [P] [US6] Unit tests for `NotificationTriggerRules` in `tests/Taskify.UnitTests/Notifications/TriggerRuleTests.cs`:
  - `TaskAssigned` notifies the new assignee when the assignee ≠ actor;
  - `TaskCreated` with an assignee ≠ actor creates a `TaskAssigned` notification;
  - `TaskMoved` notifies the current assignee when one exists and ≠ actor;
  - `CommentAdded` creates a `TaskCommented` notification for the assignee when one exists and ≠ actor;
  - other events and self-actions create nothing (SC-009);
  - summaries are at most 300 user-perceived characters, and long or emoji titles are truncated on a grapheme boundary.
- [ ] T128 [P] [US6] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/NotificationsContractTests.cs`:
  - `GET /api/notifications` returns only the acting user's notifications, newest first;
  - `unreadOnly` works, and `limit` accepts 1–100 and rejects 0 and 101 with `400`;
  - `GET /api/notifications/unread-count` is correct;
  - `POST /api/notifications/{id}/read` is idempotent `204`, and another user's ID → `404` (FR-029);
  - `POST /api/notifications/read-all` → `204` and affects only the acting user;
  - an event delivered twice creates one notification;
  - the three US6 independent-test actions produce exactly three notifications (SC-009).
- [ ] T129 [P] [US6] bUnit tests for `NotificationBell` (shows the unread count, the list renders summaries as plain text, marking read updates the count, live `NotificationCreated` increments the count, `Resync` re-fetches the count, switching user re-subscribes) in `tests/Taskify.Web.Tests/Notifications/NotificationBellTests.cs`

### Implementation for User Story 6

- [ ] T130 [P] [US6] Create the `Notification` entity in `src/Taskify.Notifications.Api/Domain/Notification.cs`. Fields: `id` UUID ("Generated"), `recipientUserId` UUID ("A seeded user, never the acting user"), `type` enum `TaskAssigned` | `TaskMoved` | `TaskCommented`, `taskId` UUID ("Task that triggered it"), `projectId` UUID ("For linking to the board"), `actorUserId` UUID ("Who made the change"), `summary` text ("Generated by the server, max 300 chars"), `createdAt` timestamptz, `readAt` timestamptz nullable ("Null = unread"), `sourceEventId` UUID unique ("Event ID; makes event delivery idempotent")
- [ ] T131 [US6] Add `Notifications` to `NotificationsDbContext` (unique index on `sourceEventId`, plus index (`recipientUserId`, `readAt`, `createdAt` desc)) and add migration `AddNotifications` in `src/Taskify.Notifications.Api/Data/Migrations/` (depends on T130)
- [ ] T132 [US6] Create `NotificationTriggerRules` in `src/Taskify.Notifications.Api/Domain/NotificationTriggerRules.cs`. It follows the trigger table in data-model.md, including `TaskCreated` with an assignee ≠ actor → `TaskAssigned`. Summaries are built from the actor display name (from the user directory), the task title and the column name, for example "Ana moved 'Login page' to In Review", truncated to 300 user-perceived characters on a grapheme boundary using `TextLength`. The recipient must never be the actor
- [ ] T133 [US6] Add notification creation to the `/internal/events` handler in `src/Taskify.Notifications.Api/Endpoints/InternalEventEndpoints.cs`. It runs in the same transaction as `ProcessedEvent`, and after the commit it sends `NotificationCreated` to `user:{recipientUserId}` through `RealtimeBroadcaster` (depends on T122, T132)
- [ ] T134 [P] [US6] Create `ListNotificationsQueryValidator` (`limit` integer 1–100, default 50; `unreadOnly` boolean, default false) in `src/Taskify.Notifications.Api/Validation/ListNotificationsQueryValidator.cs`
- [ ] T135 [US6] Implement the endpoints in `src/Taskify.Notifications.Api/Endpoints/NotificationEndpoints.cs`, all with `.RequireCallers("web")`:
  - `GET /api/notifications` (`.RequireReads()`): the acting user's notifications only, newest first;
  - `GET /api/notifications/unread-count` (`.RequireReads()`);
  - `POST /api/notifications/{notificationId}/read` (`.RequireWrites()`): idempotent `204`; `404` when the notification is missing *or belongs to another user*, so it does not reveal that the notification exists (FR-029);
  - `POST /api/notifications/read-all` (`.RequireWrites()`): `204`.
  Audit the read-state changes (depends on T131, T134)
- [ ] T136 [P] [US6] Create `NotificationRetentionJob : BackgroundService`, which runs daily and deletes notifications whose `createdAt` is more than 30 days ago (FR-030) and `ProcessedEvent` rows whose `receivedAt` is more than 30 days ago, in `src/Taskify.Notifications.Api/Data/NotificationRetentionJob.cs`. Add a unit test with a fake time provider in `tests/Taskify.UnitTests/Notifications/RetentionJobTests.cs`
- [ ] T137 [US6] Create the `NotificationBell.razor` component in `src/Taskify.Web/Components/Shared/NotificationBell.razor`:
  - the header badge shows the unread count;
  - a dropdown lists notifications (summary as plain text, relative time, a link to `/projects/{projectId}/tasks/{taskId}`, marked read on click) with a "Mark all read" button;
  - it subscribes to `user:{currentUserId}` through `RealtimeBoardService` and re-subscribes when the user switches;
  - on `Resync` it re-fetches the unread count and, if the dropdown is open, the list (FR-026).
  Add it to `src/Taskify.Web/Components/Layout/MainLayout.razor`
- [ ] T153 [US6] Update the docs for US6 (Principle IV, same change as the code):
  - `src/Taskify.Notifications.Api/README.md`: the notification endpoints and their ownership rule (FR-029), the trigger rules (FR-027), summaries, and the 30-day retention job (FR-030);
  - `src/Taskify.Web/README.md`: `NotificationBell` and how it re-subscribes on user switch.
  Add an ADR only if US6 made a decision not already covered by R10.
  (depends on T127–T137)

**Checkpoint**: All six stories work. Notifications follow FR-027–FR-030.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final documentation check, contract sync, end-to-end tests, performance, and the
remaining security test sets. The service READMEs and ADRs are no longer written here: Phase 6b
backfills them and each story keeps them current (T152, T153).

- [ ] T143 [P] Create the alert rules `deploy/alerts/rejections.yaml` (Prometheus rule format, read by the deployment's monitoring; R13):
  - `TaskifyAbnormalRejections` fires when `sum by (source) (increase(taskify_rejections_total[5m])) > 50`, where `source` is the client IP or the calling service;
  - `TaskifyOutboxDeadLettered` fires when `increase(taskify_outbox_deadlettered_total[15m]) > 0`.
  The thresholds are set by template variables with these defaults, not hard-coded. Document both alerts and how to view `taskify.rejections` in the Aspire dashboard in `src/Taskify.Web/README.md`
- [ ] T145 [P] End-to-end Playwright tests in `tests/Taskify.E2ETests/SmokeTests.cs`:
  - pick a user;
  - drag a card from To Do to In Progress and see it in under 1 s;
  - a second browser context sees the move in under 2 s (SC-008);
  - the keyboard "Move to…" menu gives the same result;
  - a `<script>alert(1)</script>` title triggers no dialog;
  - the CSP header is present, and Blazor's reconnect overlay shows no CSP violations in the console when the connection drops.
- [ ] T146 [P] Performance test: seed a project with 200 tasks and assert that the `Board` page loads and is interactive within 2 s (SC-007) in `tests/Taskify.E2ETests/PerformanceTests.cs`
- [ ] T147 [P] Injection test set (SC-006) in `tests/Taskify.IntegrationTests/Security/InjectionTests.cs`. Use common XSS payloads in titles, descriptions, comments and project names (script tags, `onerror` attributes, `javascript:` URLs, SVG). Each payload must be stored verbatim, returned verbatim, and HTML-encoded in rendered Web output and notification summaries. Also send SQL-injection strings and assert no 500 errors and no data change
- [ ] T148 [P] Audit-log tests in `tests/Taskify.IntegrationTests/Security/AuditLogTests.cs`, using the fixture's log capture:
  - data changes and rejections (`400`, `401`, `403`, `409`, `413`, `422`, `429`) produce audit events with the acting user, action, entity, outcome, `callerService` and the forwarded `sourceIp`;
  - user selection and switch produce `UserSelected` with the previous user and IP (FR-032);
  - the `taskify.rejections` counter increments;
  - no title, description or comment text appears in any log line (FR-022).
- [ ] T149 [P] Rate-limit integration tests (SC-010) in `tests/Taskify.IntegrationTests/Security/RateLimitTests.cs`: the 61st write by one user to one service within a minute → `429` with `Retry-After`, and nothing is saved (re-fetch); the 301st read → `429`; another user is unaffected; the same user can still write to a different service, because limits count per service (FR-031); the rejection is audited
- [ ] T150 Persistence test (SC-004) in `tests/Taskify.IntegrationTests/PersistenceTests.cs`: create a project, task, move and comment; stop and restart the AppHost with the same data volume; confirm all of them are still there
- [ ] T154 Documentation currency check (Principle IV, Quality Gate 4): confirm every `src/*` service README and every ADR in `docs/adr/` matches the final code (endpoints, callers, configuration, events, rate limits), that `docs/adr/` has an index listing each ADR, and that T143's alert documentation is in `src/Taskify.Web/README.md`. Fix any drift in the same change
- [ ] T151 Run every scenario in `specs/001-taskify-kanban-board/quickstart.md` (automated validation, manual scenarios 1–20 and the API smoke checks) and record the results in `specs/001-taskify-kanban-board/quickstart-results.md`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. **Blocks every user story.**
- **US1 (Phase 3)**: depends on Foundational only. This is the MVP.
- **US2 (Phase 4)**: needs the `TaskItem` entity, seed tasks and board UI from US1 (T065–T067,
  T070–T073).
- **US3 (Phase 5)**: needs the `Project` and `TaskItem` entities and the `Board` and
  `TaskDetails` pages from US1. It is independent of US2.
- **US4 (Phase 6)**: needs `TaskItem` and the `TaskDetails` page from US1. It is independent of
  US2 and US3.
- **Docs backfill (Phase 6b)**: documents Setup through US4 (T138–T142) and adds the OpenAPI
  drift check (T144). It blocks US5, so no
  new story starts while the existing services are undocumented.
- **US5 (Phase 7)**: needs Phase 6b, Foundational (outbox) and at least one event-emitting story (US2, US3
  or US4) to have anything to broadcast. Events written before US5 is deployed are delivered
  once the intake accepts them. Until then the dispatcher retries; it does not dead-letter on
  connection errors.
- **US6 (Phase 8)**: depends on US5 (the `/internal/events` intake and the hub).
- **Polish (Phase 9)**: depends on the stories it covers.

### Story dependency graph

```text
Setup ─► Foundational ─► US1 (MVP) ─┬─► US2 ─┐
                                    ├─► US3 ─┼─► Docs backfill ─► US5 ─► US6 ─► Polish
                                    └─► US4 ─┘
```

### Within each story

- Tests first; they should fail before you implement.
- Entities → DbContext and migration → validators → endpoints → Web components → page wiring.
- The outbox event and audit call are written in the same task as the endpoint that changes data.
- The story's last task updates the READMEs of the services it changed and adds any ADR it needs
  (T152, T153). The story is not done until that task is.

---

## Parallel Opportunities

- **Setup**: T006–T012 run in parallel once T001–T005 exist.
- **Foundational**: T013–T018 and T020–T028 all run in parallel (separate files). T019 waits for
  T018, and T029–T032 are sequential. T033–T036 (unit tests) run in parallel. T044, T045 and
  T046 run in parallel after T031 and T038. T052–T057 run in parallel.
- **After US1**: US2, US3 and US4 can be built by different developers at the same time. Shared
  files that need care: `TasksDbContext.cs` (one migration per story; merge in order),
  `TaskDetails.razor` and `TaskCard.razor`.
- **Inside each story**: every test task marked [P] and every entity or validator marked [P] can
  start together.
- **Docs backfill**: T138–T142 and T144 run in parallel (one file or folder each).
- **Polish**: T143 and T145–T149 run in parallel.

### Parallel example: User Story 1

```text
# Tests together:
Task: "T058 Contract tests for GET /api/projects… in tests/Taskify.IntegrationTests/Contracts/ProjectsReadContractTests.cs"
Task: "T059 Contract tests for GET /api/tasks… in tests/Taskify.IntegrationTests/Contracts/TasksReadContractTests.cs"
Task: "T060 bUnit board tests in tests/Taskify.Web.Tests/Board/BoardTests.cs"
Task: "T061 UserSelect + CurrentUserService audit tests"

# Entities and components together:
Task: "T062 Project entity in src/Taskify.Projects.Api/Domain/Project.cs"
Task: "T065 TaskItem entity in src/Taskify.Tasks.Api/Domain/TaskItem.cs"
Task: "T070 TaskCard.razor"   Task: "T071 BoardColumn.razor"
```

### Parallel example: User Story 3

```text
Task: "T088 CreateProjectValidator tests"   Task: "T089 Task validator tests (grapheme boundaries)"
Task: "T092 CreateProjectValidator"         Task: "T094 Task validators"
Task: "T097 CreateProjectForm.razor"        Task: "T098 TaskForm.razor + AssigneePicker.razor"
```

### Parallel example: User Story 5

```text
Task: "T110 AsyncApiSchemas helper"   Task: "T112 Event validator tests"
Task: "T113 Caller policy tests"      Task: "T114 Hub argument validation tests"
Task: "T116 ProcessedEvent"           Task: "T117 Event validators"
Task: "T118 EventCallerPolicy"        Task: "T119 HubArgumentValidator"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational. This is critical: security plumbing, TLS, the key matrix and the user
   directory.
3. Phase 3: US1.
4. **Stop and validate**: quickstart scenarios 1, 2 and 18, with the T055–T061 tests passing.
5. Demo on the trusted internal network only (spec assumption, deviation D1).

### Incremental delivery

1. Setup + Foundational → the foundation is ready.
2. + US1 → the board can be viewed (MVP).
3. + US2 → the Kanban workflow and history (both P1 stories done).
4. + US3 → real projects and tasks.
5. + US4 → comments.
6. + Docs backfill → READMEs and ADRs for everything built so far, and the OpenAPI drift check.
7. + US5 → live updates. + US6 → notifications (each with its docs task).
8. Polish → final docs check, E2E, performance and security test sets,
   quickstart run.

### Parallel team strategy

After Foundational and US1: developer A takes US2, developer B takes US3 and developer C takes
US4. Then one developer takes US5 and US6 (Notifications API and Web real-time) while the others
start on Polish.

---

## Notes

- [P] tasks touch different files and have no dependency on an unfinished task.
- Constraints from data-model.md are quoted in the entity and validator tasks; "chars" always
  means user-perceived characters (FR-019). Do not loosen them while implementing.
- Commit after each task or logical group. Every commit must pass `dotnet build -warnaserror`
  (the XML-doc rule).
- Stop at any checkpoint to check that story on its own.
- `[x]` in this file tracks implementation progress. It is separate from the reviewer-owned
  requirement checklists in `checklists/`.
