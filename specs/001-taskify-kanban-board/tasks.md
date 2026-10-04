---

description: "Task list for Taskify Kanban Board (phase 1)"
---

# Tasks: Taskify Kanban Board

**Input**: Design documents from `/specs/001-taskify-kanban-board/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The constitution requires them: Principle II says every validation rule needs
automated tests for accepted and rejected input, and Quality Gate 2 requires contract tests for
every service interface. research.md R11 sets the test levels (xUnit v3 unit tests, bUnit,
`Aspire.Hosting.Testing` for integration and contract tests, Playwright for end-to-end).

**Organization**: Tasks are grouped by user story. US1–US4 come from spec.md. US5 (real-time board
updates) and US6 (in-app notifications) were added by the plan (plan Summary, research R5 and
R10) and are not yet in spec.md; task T137 updates the spec to match.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story the task belongs to (US1–US6)
- Paths follow the plan's layout: `src/Taskify.*/` and `tests/Taskify.*/` at the repository root

## Conventions that apply to every task

- **Docs (Principle IV)**: every public type and member has an XML doc comment (CS1591 is an error).
  Validation rules and security decisions also get an inline comment explaining why.
- **Validation (Principle II)**: every endpoint goes through the shared `ValidationEndpointFilter`
  (T026). Strings are trimmed *before* length checks, and an empty string in an optional field is
  stored as `null`. Unknown JSON properties are rejected. Errors return as RFC 9457 Problem
  Details with no internal detail.
- **Identity**: every API request requires `X-Api-Key` (else `401`) and `X-Taskify-User` naming
  one of the five seeded users (else `400`).
- **Outbox**: every data change writes its domain event to `OutboxMessage` in the same
  transaction as the change (research R4, contracts/events.md). Payloads never include
  descriptions or comment text.
- **Audit (FR-022, R13)**: every data change and every rejected request logs an audit event
  through `IAuditLogger` (T029). Titles, descriptions and comment text are never logged.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the solution, projects and build/CI configuration.

- [ ] T001 Create `Taskify.slnx` at the repository root, plus `src/` and `tests/` folders, `docs/adr/`, and a `.gitignore` for .NET (bin, obj, `*.user`, `appsettings.*.local.json`, `.vs/`)
- [ ] T002 Create `global.json` pinning the .NET 10 SDK (`10.0.1xx`, `rollForward: latestFeature`) at the repository root
- [ ] T003 Create `Directory.Build.props` at the repository root: `TargetFramework` `net10.0`, `LangVersion` 14, `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, `GenerateDocumentationFile` true, `WarningsAsErrors` includes `CS1591`, `AnalysisLevel` `latest-recommended`, `EnforceCodeStyleInBuild` true. Test projects override `GenerateDocumentationFile` to false
- [ ] T004 Create `Directory.Packages.props` with central package management (`ManagePackageVersionsCentrally` true) and versions for Aspire 13.x (`Aspire.Hosting.AppHost`, `Aspire.Hosting.PostgreSQL`, `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.Hosting.Testing`), `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore.Design` 10.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x, `FluentValidation` and `FluentValidation.DependencyInjectionExtensions`, `Microsoft.AspNetCore.SignalR.Client`, `xunit.v3`, `bunit`, `Microsoft.Playwright`, and `Microsoft.NET.Test.Sdk`
- [ ] T005 Create the Aspire AppHost project `src/Taskify.AppHost/Taskify.AppHost.csproj` (Aspire AppHost SDK) with an empty `src/Taskify.AppHost/AppHost.cs`, and add it to `Taskify.slnx`
- [ ] T006 [P] Create the Aspire ServiceDefaults project `src/Taskify.ServiceDefaults/` (from the `aspire-servicedefaults` template: OpenTelemetry, `/health` and `/alive` health checks, HTTP resilience, service discovery) and add it to `Taskify.slnx`
- [ ] T007 [P] Create class libraries `src/Taskify.Contracts/Taskify.Contracts.csproj` and `src/Taskify.Security/Taskify.Security.csproj` (Security has a `FrameworkReference` to `Microsoft.AspNetCore.App` and references FluentValidation), and add both to `Taskify.slnx`
- [ ] T008 [P] Create web API projects `src/Taskify.Projects.Api/`, `src/Taskify.Tasks.Api/` and `src/Taskify.Notifications.Api/` (minimal APIs, `Microsoft.AspNetCore.OpenApi`, EF Core Npgsql via Aspire, references to ServiceDefaults, Contracts and Security). Create the folders `Endpoints/`, `Domain/`, `Validation/`, `Data/` (plus `Clients/` in Tasks and `Hubs/` in Notifications) and add the projects to `Taskify.slnx`
- [ ] T009 [P] Create the Blazor Web App project `src/Taskify.Web/` (Interactive Server render mode, no WebAssembly) with `Components/Pages/`, `Components/Board/`, `Components/Shared/` and `Services/` folders, references to ServiceDefaults and Contracts, and add it to `Taskify.slnx`
- [ ] T010 [P] Create test projects `tests/Taskify.UnitTests/` (xUnit v3), `tests/Taskify.Web.Tests/` (xUnit v3 + bUnit), `tests/Taskify.IntegrationTests/` (xUnit v3 + `Aspire.Hosting.Testing`, references the AppHost) and `tests/Taskify.E2ETests/` (xUnit v3 + `Microsoft.Playwright`), and add them to `Taskify.slnx`
- [ ] T011 [P] Create `.editorconfig` at the repository root with C# style rules and set `dotnet_diagnostic.CA2100.severity = error` (SQL built from user input) and `dotnet_diagnostic.CA3001.severity = error` (SQL injection)
- [ ] T012 [P] Create the CI pipeline `.github/workflows/ci.yml` (R12). Steps: restore; `dotnet build -warnaserror`; `dotnet test` for Unit, Web and Integration tests (Docker available); `dotnet list package --vulnerable --include-transitive`, failing on High or Critical; CodeQL analysis for C#; and secret scanning (gitleaks action)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared contracts, security plumbing, databases, the user directory, the outbox and
the Web shell. Every user story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Shared contracts (`Taskify.Contracts`)

- [ ] T013 [P] Create `TaskStatus` enum (`ToDo`, `InProgress`, `InReview`, `Done`, in this order) and `UserRole` enum (`ProductManager`, `Engineer`) in `src/Taskify.Contracts/Enums.cs`, serialized as strings
- [ ] T014 [P] Create `SeedIds` static class with fixed UUIDs for the five users (Maya Chen – ProductManager; Liam Novak, Priya Patel, Tomasz Wiśniewski, Jordan Lee – Engineer) and the three sample projects (Mobile App Launch, Website Redesign, Internal Tools) in `src/Taskify.Contracts/SeedIds.cs`
- [ ] T015 [P] Create the versioned event types (v1) in `src/Taskify.Contracts/Events/`: `EventEnvelope` (`eventId` UUID, `type` string, `version` int = 1, `occurredAt` UTC date-time, `actorUserId` UUID, `payload`), the `EventTypes` constants (`ProjectCreated`, `TaskCreated`, `TaskUpdated`, `TaskAssigned`, `TaskMoved`, `CommentAdded`, `CommentEdited`, `CommentDeleted`), and one payload record per type with exactly the fields listed in contracts/events.md. Document on each record that descriptions and comment text are never included

### Security plumbing (`Taskify.Security`)

- [ ] T016 [P] Create the `IUserDirectory` interface (`Task<bool> ExistsAsync(Guid userId)`, `Task<IReadOnlyList<UserInfo>> GetAllAsync()`) and the `UserInfo` record in `src/Taskify.Security/Users/IUserDirectory.cs`
- [ ] T017 [P] Create `ApiKeyOptions`, which maps caller name → key, read from configuration and never logged, in `src/Taskify.Security/ApiKeys/ApiKeyOptions.cs`. Create `ApiKeyMiddleware` in `src/Taskify.Security/ApiKeys/ApiKeyMiddleware.cs`. The middleware checks `X-Api-Key` with `CryptographicOperations.FixedTimeEquals` against the allowed callers, sets `HttpContext.Items["Caller"]`, returns `401` Problem Details when the key is missing or unknown, and skips only `/health` and `/alive`
- [ ] T018 [P] Create `ActingUserMiddleware` in `src/Taskify.Security/Users/ActingUserMiddleware.cs` and `ICurrentActingUser` in `src/Taskify.Security/Users/ICurrentActingUser.cs`. The middleware reads `X-Taskify-User`, which must be a UUID and exist in `IUserDirectory`, otherwise it returns `400` Problem Details. It exposes the user ID to endpoints. Endpoints marked with the `SkipActingUser` metadata (used by `/internal/events`) are exempt
- [ ] T019 [P] Create `ProblemDetailsSetup` in `src/Taskify.Security/Errors/ProblemDetailsSetup.cs`. It registers `AddProblemDetails` with a customization that adds `traceId` and removes the exception detail. It adds a global exception handler that returns a generic `500` with no stack trace, type name or SQL text. It also defines helpers for `404`, `403`, `409` and `422` responses
- [ ] T020 [P] Configure JSON options in `src/Taskify.Security/Json/StrictJsonSetup.cs`: `UnmappedMemberHandling = Disallow` (unknown fields → `400`), enums as strings with integer values rejected, and case-insensitive property names off
- [ ] T021 [P] Create the trim helper `InputNormalizer` (trims; returns null for empty optional strings) in `src/Taskify.Security/Validation/InputNormalizer.cs`, and the FluentValidation extension `MustHaveTrimmedLength(min, max)` in `src/Taskify.Security/Validation/ValidationRules.cs`. Comment why length is checked after trimming (FR-006, FR-009, FR-015)
- [ ] T022 [P] Create rate-limit policies in `src/Taskify.Security/RateLimiting/RateLimitPolicies.cs`: a fixed window per acting user and per caller for writes (`writes`), a looser one for reads (`reads`) and a per-IP limit for the Web app. A rejected request returns `429` Problem Details
- [ ] T023 [P] Create `IAuditLogger` and `AuditLogger` in `src/Taskify.Security/Audit/AuditLogger.cs`. They write structured log events with `actingUserId`, `action`, `entityType`, `entityId`, `outcome`, `correlationId` and caller. There are no parameters for text content, and a doc comment explains the FR-022 rule
- [ ] T024 Create the `ValidationEndpointFilter<T>` in `src/Taskify.Security/Validation/ValidationEndpointFilter.cs`. It resolves `IValidator<T>`, returns `400` with field errors when validation fails, and logs the rejection through `IAuditLogger`. It never echoes rejected values back (depends on T019, T023)
- [ ] T025 Create `SecurityServiceExtensions.AddTaskifySecurity()` and `UseTaskifySecurity()` in `src/Taskify.Security/SecurityServiceExtensions.cs`. These wire up T017–T024 and rejection auditing for `401`, `400` and `429` (depends on T017–T024)
- [ ] T026 [P] Unit tests for `InputNormalizer` and `MustHaveTrimmedLength` covering whitespace-only, leading and trailing spaces, exactly min and max, and max+1 in `tests/Taskify.UnitTests/Security/ValidationRulesTests.cs`
- [ ] T027 [P] Unit tests for `ApiKeyMiddleware` (missing, wrong, valid and health-path keys) and `ActingUserMiddleware` (missing, non-UUID, unknown and valid users; skip metadata) in `tests/Taskify.UnitTests/Security/MiddlewareTests.cs`

### Orchestration (`Taskify.AppHost`)

- [ ] T028 Configure `src/Taskify.AppHost/AppHost.cs`:
  - a PostgreSQL server with a secret password parameter and a data volume, plus databases `projectsdb`, `tasksdb` and `notificationsdb`;
  - secret parameters `web-api-key`, `projects-api-key` and `tasks-api-key`;
  - resources `projects-api`, `tasks-api`, `notifications-api` and `web`, each referencing only its own database and getting only the keys it needs (Web gets its own key; each API gets the keys of its allowed callers);
  - `WithExternalHttpEndpoints()` on `web` only, and HTTPS endpoints for all resources.
  Comment the least-privilege intent

### Projects service foundation: user directory

- [ ] T029 Create the `User` entity in `src/Taskify.Projects.Api/Domain/User.cs`. Fields: `id` UUID ("Fixed seed value"), `displayName` text ("1–100 chars, unique"), `role` enum `ProductManager` | `Engineer` ("Exactly one `ProductManager`, four `Engineer`"). The type has no setters (read-only, FR-003)
- [ ] T030 Create `OutboxMessage` in `src/Taskify.Security/Outbox/OutboxMessage.cs`, shared by services that publish events. Fields: `id` UUID ("Generated; also the event ID used for de-duplication"), `type` text, `payload` jsonb, `occurredAt` timestamptz ("Same transaction as the change"), `dispatchedAt` timestamptz nullable ("Set after delivery succeeds"), `attempts` int ("Retry count; backoff capped at 1 minute")
- [ ] T031 Create `ProjectsDbContext` with `Users` and `OutboxMessages` in `src/Taskify.Projects.Api/Data/ProjectsDbContext.cs`. Add a unique index on `displayName`, seed the five users from `SeedIds` with `HasData`, and add the initial EF Core migration under `src/Taskify.Projects.Api/Data/Migrations/`
- [ ] T032 Create `LocalUserDirectory` (reads `ProjectsDbContext.Users`, cached in memory) in `src/Taskify.Projects.Api/Data/LocalUserDirectory.cs`
- [ ] T033 Implement `GET /api/users` (all five users) and `GET /api/users/{userId}` (`404` if unknown) per contracts/projects-api.yaml in `src/Taskify.Projects.Api/Endpoints/UserEndpoints.cs`
- [ ] T034 Wire up `src/Taskify.Projects.Api/Program.cs`: ServiceDefaults; `AddNpgsqlDbContext<ProjectsDbContext>("projectsdb")`; migrations applied at startup in Development; `AddTaskifySecurity` with allowed caller `web`; FluentValidation validators from the assembly; OpenAPI at `/openapi/v1.json`; rate limiter; endpoint groups

### Tasks and Notifications service foundation

- [ ] T035 [P] Create `ProjectsApiClient` (typed `HttpClient` for `https+http://projects-api`, sends the `tasks` API key) with `GetUsersAsync()` and `ProjectExistsAsync(Guid)` in `src/Taskify.Tasks.Api/Clients/ProjectsApiClient.cs`. Create `RemoteUserDirectory : IUserDirectory`, which caches the user list in memory after the first successful fetch (users never change in phase 1, R4), in `src/Taskify.Tasks.Api/Clients/RemoteUserDirectory.cs`
- [ ] T036 [P] Create `TasksDbContext` with `OutboxMessages` (task entities are added by the stories) in `src/Taskify.Tasks.Api/Data/TasksDbContext.cs`, and wire up `src/Taskify.Tasks.Api/Program.cs`: ServiceDefaults; `AddNpgsqlDbContext<TasksDbContext>("tasksdb")`; startup migrations in Development; `AddTaskifySecurity` with allowed caller `web`; `RemoteUserDirectory`; OpenAPI; rate limiter
- [ ] T037 [P] Create `NotificationsDbContext` (empty model for now) in `src/Taskify.Notifications.Api/Data/NotificationsDbContext.cs` and wire up `src/Taskify.Notifications.Api/Program.cs`: ServiceDefaults; `AddNpgsqlDbContext<NotificationsDbContext>("notificationsdb")`; `AddTaskifySecurity` with allowed callers `web`, `projects` and `tasks`; a user directory using the Projects API client pattern from T035 (copied into `src/Taskify.Notifications.Api/Clients/`); OpenAPI; rate limiter
- [ ] T038 Create `OutboxDispatcher : BackgroundService` in `src/Taskify.Security/Outbox/OutboxDispatcher.cs`. It is generic over the `DbContext` and polls undispatched rows in `occurredAt` order. It POSTs each `EventEnvelope` to `https+http://notifications-api/internal/events` with the service's own API key and sets `dispatchedAt` on a `202`. It increments `attempts` with exponential backoff capped at 1 minute and never logs the payload. Register it in the Projects and Tasks APIs (depends on T030, T034, T036)
- [ ] T039 Create the `OutboxWriter` helper (`Add(DbContext, eventType, actorUserId, payload)`, which stages an outbox row in the current change tracker so it saves in the same `SaveChanges` transaction) in `src/Taskify.Security/Outbox/OutboxWriter.cs`
- [ ] T040 [P] Unit tests for `OutboxDispatcher`: retries with capped backoff, marks dispatched on 202, and leaves the row pending on failure. Use a fake `HttpMessageHandler` in `tests/Taskify.UnitTests/Outbox/OutboxDispatcherTests.cs`

### Web shell (`Taskify.Web`)

- [ ] T041 Create typed API clients in `src/Taskify.Web/Services/ApiClients/`: `ProjectsClient` (users and projects), `TasksClient` (tasks, moves, history, comments) and `NotificationsClient`. All share a `DelegatingHandler` (`TaskifyHeadersHandler.cs`) that adds `X-Api-Key` (web key) and `X-Taskify-User` from `CurrentUserService`, and maps Problem Details into an `ApiError` result with field errors
- [ ] T042 Create `CurrentUserService` (scoped per circuit) in `src/Taskify.Web/Services/CurrentUserService.cs`. It holds the selected user ID and raises `Changed`. It persists the choice in an HttpOnly, Secure, SameSite=Strict data-protected cookie so it survives a refresh (R8), and re-validates the cookie value against `GET /api/users` on load
- [ ] T043 Wire up `src/Taskify.Web/Program.cs` with:
  - ServiceDefaults and Razor components with Interactive Server;
  - antiforgery;
  - HTTPS redirection and HSTS;
  - security-header middleware: `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: no-referrer`;
  - a per-IP rate limiter;
  - the typed clients from T041.
  Add a comment that `MarkupString` is banned for user content (R9)
- [ ] T044 Create the layout and routing in `src/Taskify.Web/Components/Layout/MainLayout.razor` and `src/Taskify.Web/Components/Routes.razor`. The header shows the selected user and a "Switch user" button. Pages other than `/` redirect to `/` when no user is selected. Add a shared `NotFound.razor` page in `src/Taskify.Web/Components/Pages/NotFound.razor` with a link back to the project list
- [ ] T045 [P] Create the `ErrorBanner.razor` component, which shows `ApiError` messages and field errors as plain text, in `src/Taskify.Web/Components/Shared/ErrorBanner.razor`
- [ ] T046 [P] Integration test fixture `TaskifyAppFixture` in `tests/Taskify.IntegrationTests/Infrastructure/TaskifyAppFixture.cs`. It starts the AppHost with `DistributedApplicationTestingBuilder`, provides test API keys, waits for all resources to be healthy, and exposes `HttpClient`s for each API with helpers that set `X-Api-Key` and `X-Taskify-User`
- [ ] T047 [P] Integration tests for cross-cutting API behavior in `tests/Taskify.IntegrationTests/Security/CrossCuttingTests.cs`: no `X-Api-Key` → `401`; an unknown `X-Taskify-User` → `400`; an unknown JSON property → `400`; the error body has no stack trace; only `web` has an external endpoint; `/health` → 200 on all services
- [ ] T048 [P] Contract test for `GET /api/users` and `GET /api/users/{userId}` (exactly five users, one ProductManager, schema matches contracts/projects-api.yaml) in `tests/Taskify.IntegrationTests/Contracts/UsersContractTests.cs`

**Checkpoint**: The AppHost starts all resources, the user directory works, and the security
plumbing rejects bad requests. User stories can now begin.

---

## Phase 3: User Story 1 - Pick a user and view a project board (Priority: P1) 🎯 MVP

**Goal**: Pick one of five users without a password, list the three sample projects, open a board
with four columns, and see your own tasks highlighted. You can also switch user.

**Independent Test**: Start Taskify with the sample data, select any user, open each of the three
sample projects, and confirm every task appears in the correct column and that the selected
user's tasks are highlighted.

### Tests for User Story 1

> Write these first and make sure they fail before you implement.

- [ ] T049 [P] [US1] Contract tests for `GET /api/projects` (three seeded projects, newest first) and `GET /api/projects/{projectId}` (`404` for an unknown ID) in `tests/Taskify.IntegrationTests/Contracts/ProjectsReadContractTests.cs`
- [ ] T050 [P] [US1] Contract tests for `GET /api/tasks?projectId=` (every seeded task appears once; `400` without or with a malformed `projectId`; newest first within each status) and `GET /api/tasks/{taskId}` (`404` for unknown) against the `TaskSummary` and `TaskDetail` schemas in contracts/tasks-api.yaml, in `tests/Taskify.IntegrationTests/Contracts/TasksReadContractTests.cs`
- [ ] T051 [P] [US1] bUnit tests in `tests/Taskify.Web.Tests/Board/BoardTests.cs`:
  - four columns in the order To Do, In Progress, In Review, Done;
  - each card shows its title and assignee name, or "Unassigned";
  - the selected user's cards have the `card--mine` class and an "Assigned to you" label;
  - an empty project shows four empty columns and an "Add the first task" prompt.
- [ ] T052 [P] [US1] bUnit tests for `UserSelect` (five users with role, no password field, choosing sets `CurrentUserService`) and the "Switch user" flow in `tests/Taskify.Web.Tests/Pages/UserSelectTests.cs`

### Implementation for User Story 1

- [ ] T053 [P] [US1] Create the `Project` entity in `src/Taskify.Projects.Api/Domain/Project.cs`. Fields: `id` UUID ("Generated"), `name` text ("Required; 1–100 chars after trim (FR-006); duplicates allowed"), `description` text nullable ("0–1,000 chars after trim; empty string stored as null"), `createdByUserId` UUID ("Must be a seeded user (FR-004)"), `createdAt` timestamptz ("Set by the server")
- [ ] T054 [US1] Add `Projects` to `ProjectsDbContext`. Seed the three sample projects (Mobile App Launch, Website Redesign, Internal Tools) with `SeedIds` and fixed `createdAt` values, created by Maya Chen. Add migration `AddProjects` in `src/Taskify.Projects.Api/Data/Migrations/` (depends on T053)
- [ ] T055 [US1] Implement `GET /api/projects` (all projects, "List order: `createdAt` descending") and `GET /api/projects/{projectId}` (`404` when missing) per contracts/projects-api.yaml in `src/Taskify.Projects.Api/Endpoints/ProjectEndpoints.cs`. Validate `projectId` as a UUID route parameter
- [ ] T056 [P] [US1] Create the `TaskItem` entity in `src/Taskify.Tasks.Api/Domain/TaskItem.cs`. Fields: `id` UUID ("Generated"), `projectId` UUID ("Must exist in the Projects API (checked on create)"), `title` text ("Required; 1–200 chars after trim (FR-009)"), `description` text nullable ("0–5,000 chars after trim"), `status` enum `ToDo` | `InProgress` | `InReview` | `Done` ("New tasks start as `ToDo`"), `assigneeUserId` UUID nullable ("Null = unassigned; otherwise a seeded user (FR-010)"), `createdByUserId` UUID ("Acting user"), `createdAt` timestamptz ("Set by the server"), `updatedAt` timestamptz ("Updated on any change, including moves"). No delete operation
- [ ] T057 [US1] Add `Tasks` to `TasksDbContext`: status stored as a string, and index (`projectId`, `status`, `createdAt` desc). Seed sample tasks for each of the three projects with tasks in all four columns, assigned to a mix of the five users with some unassigned (FR-005). Add migration `AddTasks` in `src/Taskify.Tasks.Api/Data/Migrations/` (depends on T056)
- [ ] T058 [US1] Implement `GET /api/tasks?projectId=` (required UUID; returns `TaskSummary[]` sorted by status, then `createdAt` desc; `commentCount` is 0 until US4 adds comments) and `GET /api/tasks/{taskId}` (`TaskDetail`, `404` when missing) in `src/Taskify.Tasks.Api/Endpoints/TaskReadEndpoints.cs`, with DTOs in `src/Taskify.Tasks.Api/Endpoints/Dtos.cs`
- [ ] T059 [US1] Create the `UserSelect` page at route `/` in `src/Taskify.Web/Components/Pages/UserSelect.razor`. It lists the five users from `ProjectsClient` with display name and role ("Product Manager" or "Engineer"), has no password, and choosing a user sets `CurrentUserService` and goes to `/projects`
- [ ] T060 [US1] Create the `Projects` page at route `/projects` in `src/Taskify.Web/Components/Pages/Projects.razor`. It lists all projects newest first with name, description excerpt and creation date (which tells apart projects with the same name), and links to `/projects/{id}`
- [ ] T061 [P] [US1] Create the `TaskCard.razor` component in `src/Taskify.Web/Components/Board/TaskCard.razor`. It shows the title and the assignee display name, or "Unassigned". When the assignee is the current user it adds the `card--mine` class (a distinct border and background) plus a visible "Assigned to you" label, so the cue does not rely on color alone (FR-013, FR-014). It links to the task details
- [ ] T062 [P] [US1] Create the `BoardColumn.razor` component (heading, count, list of `TaskCard`, empty state) in `src/Taskify.Web/Components/Board/BoardColumn.razor`, and add styles in `src/Taskify.Web/wwwroot/app.css`
- [ ] T063 [US1] Create the `Board` page at route `/projects/{ProjectId:guid}` in `src/Taskify.Web/Components/Pages/Board.razor`. It loads the project and tasks, renders exactly four `BoardColumn`s in the order To Do, In Progress, In Review, Done, and shows "Add the first task" when there are no tasks. It shows `NotFound` when the project returns 404 (depends on T061, T062)
- [ ] T064 [US1] Create the `TaskDetails` page at route `/projects/{ProjectId:guid}/tasks/{TaskId:guid}` in `src/Taskify.Web/Components/Pages/TaskDetails.razor`. It shows the title, description, column, assignee, creator and dates as plain text, and has empty sections for history (US2) and comments (US4). It shows `NotFound` when either ID returns 404
- [ ] T065 [US1] Implement "Switch user" in `src/Taskify.Web/Components/Layout/MainLayout.razor`: clear `CurrentUserService` and the cookie, then go to `/`
- [ ] T066 [US1] Add the US1 audit and log calls: rejected reads are audited through the filter, and project or task 404s are logged at Information level with IDs only. Cover `src/Taskify.Projects.Api/Endpoints/ProjectEndpoints.cs` and `src/Taskify.Tasks.Api/Endpoints/TaskReadEndpoints.cs`

**Checkpoint**: US1 is fully usable on its own with the seed data. This is the MVP demo.

---

## Phase 4: User Story 2 - Move tasks across Kanban columns (Priority: P1)

**Goal**: Move a task to any other column by dragging it or with a keyboard "Move to…" menu.
The move is saved, recorded in a read-only status history, and visible after a reload.

**Independent Test**: Using the sample data, move a task to each of the other three columns in
turn, reload the board, and confirm the task stays in the last column it was moved to.

### Tests for User Story 2

- [ ] T067 [P] [US2] Unit tests for `MoveTaskValidator` in `tests/Taskify.UnitTests/Validation/MoveTaskValidatorTests.cs`. Accepted: each of the four `toStatus` values. Rejected: missing, unknown string, integer, wrong case, extra property
- [ ] T068 [P] [US2] Unit tests for the move domain rule in `tests/Taskify.UnitTests/Domain/TaskMoveTests.cs`: a move to a different status changes the status, creates a `StatusChange` with the correct from/to/user/time, and updates `updatedAt`. A move to the same status changes nothing and creates no history
- [ ] T069 [P] [US2] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/MoveContractTests.cs`:
  - `POST /api/tasks/{taskId}/moves` returns `MoveTaskResult` with `changed=true`, and the task stays moved after a re-fetch;
  - a backwards move (Done → InProgress) is allowed;
  - a same-column move returns `changed=false` and `statusChange: null`, and adds no history row;
  - an unknown task → `404`;
  - `GET /api/tasks/{taskId}/history` is newest first and has no `PUT`, `PATCH` or `DELETE` (→ `405`);
  - a direct `UPDATE` or `DELETE` on `status_changes` with the Tasks DB role fails.
- [ ] T070 [P] [US2] bUnit tests in `tests/Taskify.Web.Tests/Board/MoveTests.cs`:
  - the `MoveToMenu` lists the three other columns and calls `TasksClient.MoveAsync`;
  - when the API rejects a move, the card snaps back and `ErrorBanner` shows;
  - `StatusHistory` renders entries newest first with mover name, from → to, and time.

### Implementation for User Story 2

- [ ] T071 [P] [US2] Create the `StatusChange` entity in `src/Taskify.Tasks.Api/Domain/StatusChange.cs`. Fields: `id` UUID ("Generated"), `taskId` UUID ("FK → Task"), `fromStatus` status enum ("Must differ from `toStatus`"), `toStatus` status enum, `movedByUserId` UUID ("Acting user"), `movedAt` timestamptz ("Set by the server"). Read-only after insert, with no public setters
- [ ] T072 [US2] Add `TaskItem.MoveTo(TaskStatus to, Guid actingUserId, DateTimeOffset now)` returning `StatusChange?` in `src/Taskify.Tasks.Api/Domain/TaskItem.cs`. It returns null and changes nothing for a same-status move. Add a doc comment citing FR-012 and US2 scenario 4 (depends on T071)
- [ ] T073 [US2] Add `StatusChanges` to `TasksDbContext` (FK to Task, index (`taskId`, `movedAt` desc)) and add migration `AddStatusHistory` in `src/Taskify.Tasks.Api/Data/Migrations/`. The migration must `REVOKE UPDATE, DELETE ON status_changes FROM` the Tasks API role, with a comment citing FR-023 and the threat "Tampering with status history"
- [ ] T074 [P] [US2] Create `MoveTaskValidator` (`toStatus` required, must be one of `ToDo` | `InProgress` | `InReview` | `Done`) in `src/Taskify.Tasks.Api/Validation/MoveTaskValidator.cs`
- [ ] T075 [US2] Implement `POST /api/tasks/{taskId}/moves` in `src/Taskify.Tasks.Api/Endpoints/MoveEndpoints.cs`:
  - validates the body, returns `404` for an unknown task, and calls `MoveTo`;
  - when the task changed, saves the task, the `StatusChange` and an outbox `TaskMoved` event (`taskId`, `projectId`, `title`, `fromStatus`, `toStatus`, `assigneeUserId?`) in one transaction;
  - the last write wins, and a no-op writes nothing and sends no event;
  - returns `MoveTaskResult`, and audits `TaskMoved` (IDs and statuses only).
  (depends on T072–T074)
- [ ] T076 [US2] Implement `GET /api/tasks/{taskId}/history` (newest first, `404` for an unknown task, no write routes) in `src/Taskify.Tasks.Api/Endpoints/HistoryEndpoints.cs`
- [ ] T077 [P] [US2] Create the `MoveToMenu.razor` component (a keyboard-accessible button and menu listing the other three columns, with an ARIA menu role) in `src/Taskify.Web/Components/Board/MoveToMenu.razor`
- [ ] T078 [US2] Add native HTML5 drag-and-drop:
  - `draggable="true"` and `@ondragstart` on `TaskCard`;
  - `@ondragover:preventDefault` and `@ondrop` on `BoardColumn`;
  - add `MoveToMenu` to `TaskCard`.
  A drop in the same column does nothing. The UI moves the card straight away, calls `TasksClient.MoveAsync`, and on error snaps the card back and shows `ErrorBanner` (R6). Files: `src/Taskify.Web/Components/Board/TaskCard.razor`, `src/Taskify.Web/Components/Board/BoardColumn.razor` and `src/Taskify.Web/Components/Pages/Board.razor` (depends on T077)
- [ ] T079 [P] [US2] Create the `StatusHistory.razor` component (newest first: mover display name, from → to column names, and local time) in `src/Taskify.Web/Components/Shared/StatusHistory.razor`, and add it to `src/Taskify.Web/Components/Pages/TaskDetails.razor`

**Checkpoint**: US1 and US2 work. Moves are saved and the history is shown.

---

## Phase 5: User Story 3 - Create projects and tasks and assign them (Priority: P2)

**Goal**: Create projects and tasks, assign or unassign any predefined user, and edit a task's
title, description and assignee. Invalid input is rejected with a clear message and changes
nothing.

**Independent Test**: Create a new project, add three tasks, assign two of them to different users,
edit one task's assignee, and confirm everything shows up correctly on the board (new tasks start
in To Do).

### Tests for User Story 3

- [ ] T080 [P] [US3] Unit tests for `CreateProjectValidator` in `tests/Taskify.UnitTests/Validation/CreateProjectValidatorTests.cs`. Name: accepted at 1 and 100 characters after trim; rejected when empty, whitespace-only, or 101 characters. Description: accepted when null, empty or 1,000 characters; rejected at 1,001 characters. Extra property: rejected
- [ ] T081 [P] [US3] Unit tests for `CreateTaskValidator` and `UpdateTaskValidator` in `tests/Taskify.UnitTests/Validation/TaskValidatorTests.cs`. Title: accepted at 1 and 200 characters after trim; rejected when empty, whitespace-only, or 201 characters. Description: rejected at 5,001 characters. `projectId`: must be a non-empty UUID. `assigneeUserId`: null is accepted, an unknown user is rejected. `UpdateTaskRequest` requires `title` and `assigneeUserId` to be present
- [ ] T082 [P] [US3] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/CreateEditContractTests.cs`:
  - `POST /api/projects` → `201` with the creator set from `X-Taskify-User`;
  - `POST /api/tasks` → `201` with status `ToDo`;
  - an unknown `projectId` or `assigneeUserId` → `422`;
  - `PUT /api/tasks/{id}` updates the fields and unassigns when `assigneeUserId` is null;
  - invalid input → `400`, and the stored data is unchanged (re-fetch and compare);
  - a `<script>` title is stored and returned verbatim, not executed;
  - the outbox holds `ProjectCreated`, `TaskCreated`, `TaskUpdated` and `TaskAssigned` rows, and none of them contain a description.
- [ ] T083 [P] [US3] bUnit tests for `CreateProjectForm`, `TaskForm` (create and edit; assignee picker lists the five users plus "Unassigned"; server field errors shown) and the plain-text rendering of `<script>` titles in `tests/Taskify.Web.Tests/Forms/FormTests.cs`

### Implementation for User Story 3

- [ ] T084 [P] [US3] Create `CreateProjectValidator` (`name`: "Required; 1–100 chars after trim (FR-006)"; `description`: "0–1,000 chars after trim; empty string stored as null") in `src/Taskify.Projects.Api/Validation/CreateProjectValidator.cs`
- [ ] T085 [US3] Implement `POST /api/projects` in `src/Taskify.Projects.Api/Endpoints/ProjectEndpoints.cs`. It validates, trims the input, sets `createdByUserId` from the acting user and `createdAt` from the server, and saves the project with an outbox `ProjectCreated` event (`projectId`, `name`) in one transaction. It returns `201` with `Location`, uses the `writes` rate-limit policy, and audits the change (depends on T084)
- [ ] T086 [P] [US3] Create `CreateTaskValidator` and `UpdateTaskValidator` in `src/Taskify.Tasks.Api/Validation/TaskValidators.cs`. Rules: `title` "Required; 1–200 chars after trim (FR-009)"; `description` "0–5,000 chars after trim"; `assigneeUserId` "Null = unassigned; otherwise a seeded user (FR-010)", checked through `IUserDirectory`; `projectId` is a required UUID
- [ ] T087 [US3] Implement `POST /api/tasks` in `src/Taskify.Tasks.Api/Endpoints/TaskWriteEndpoints.cs`:
  - validates the body and checks `projectId` with `ProjectsApiClient.ProjectExistsAsync` (`422` when missing);
  - an unknown assignee → `422`;
  - creates the task with status `ToDo`, `createdByUserId` set to the acting user and server timestamps;
  - saves it with an outbox `TaskCreated` event (`taskId`, `projectId`, `title`, `status`, `assigneeUserId?`) in one transaction;
  - returns `201`, uses the `writes` policy, and audits the change.
  (depends on T086)
- [ ] T088 [US3] Implement `PUT /api/tasks/{taskId}` in `src/Taskify.Tasks.Api/Endpoints/TaskWriteEndpoints.cs`:
  - validates; an unknown task → `404`; an unknown assignee → `422`;
  - replaces the title, description and assignee and sets `updatedAt`;
  - stages outbox `TaskUpdated` when the text changed, and `TaskAssigned` (with `previousAssigneeUserId?` and `assigneeUserId?`) when the assignee changed, both in the same transaction;
  - works for tasks in any column, including Done (clarification Q5), and audits the change.
- [ ] T089 [P] [US3] Create the `CreateProjectForm.razor` component (name and description inputs with the 100 and 1,000 limits and a character counter; server errors shown through `ErrorBanner`; antiforgery) in `src/Taskify.Web/Components/Shared/CreateProjectForm.razor`, and add it to `src/Taskify.Web/Components/Pages/Projects.razor`
- [ ] T090 [P] [US3] Create the `TaskForm.razor` component (create and edit modes; title limited to 200 and description to 5,000; an `AssigneePicker` with the five users plus "Unassigned"; client-side limits only as a usability aid; server field errors shown) in `src/Taskify.Web/Components/Shared/TaskForm.razor`, and `AssigneePicker.razor` in `src/Taskify.Web/Components/Shared/AssigneePicker.razor`
- [ ] T091 [US3] Add an "Add task" action to `src/Taskify.Web/Components/Pages/Board.razor` (opens `TaskForm` in create mode; the new task appears in To Do) and an "Edit" action to `src/Taskify.Web/Components/Pages/TaskDetails.razor` (opens `TaskForm` in edit mode) (depends on T090)

**Checkpoint**: US1–US3 work. Users can create and assign real work.

---

## Phase 6: User Story 4 - Comment on tasks (Priority: P3)

**Goal**: Read and post comments on any task. Authors can edit their own comments, which then show
"edited", or delete them, which leaves a "Comment deleted by [author]" placeholder. Comments by
other users cannot be edited or deleted.

**Independent Test**: Open a task, add a comment as one user, switch to another user, confirm the
comment is visible but cannot be edited or deleted, switch back, and edit and delete the comment.

### Tests for User Story 4

- [ ] T092 [P] [US4] Unit tests for `CommentTextValidator` in `tests/Taskify.UnitTests/Validation/CommentTextValidatorTests.cs`. Accepted: 1 and 2,000 characters after trim. Rejected: empty, whitespace-only, 2,001 characters, extra property
- [ ] T093 [P] [US4] Unit tests for the comment lifecycle in `tests/Taskify.UnitTests/Domain/CommentTests.cs`:
  - an author edit sets `editedAt`;
  - a non-author edit or delete → forbidden;
  - delete sets `text = null` and `deletedAt`;
  - editing or deleting an already deleted comment → conflict, and the comment cannot be restored.
- [ ] T094 [P] [US4] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/CommentsContractTests.cs`:
  - `POST` → `201` with the author set to the acting user;
  - `GET` lists comments oldest first, including deleted placeholders (`isDeleted=true`, `text=null`);
  - an edit by a non-author → `403`;
  - an edit or delete of a deleted comment → `409`;
  - an unknown task or comment → `404`;
  - commenting on a Done task is allowed;
  - after delete, the comment text is gone from the database row and from the outbox payload;
  - `commentCount` on `TaskSummary` excludes deleted comments.
- [ ] T095 [P] [US4] bUnit tests for `CommentThread` in `tests/Taskify.Web.Tests/Comments/CommentThreadTests.cs`:
  - author name and time shown, oldest first;
  - an "edited" indicator;
  - the placeholder "Comment deleted by Priya Patel" with the deletion time;
  - no edit or delete buttons on other users' comments or on deleted ones;
  - `<script>` text shown as plain text.

### Implementation for User Story 4

- [ ] T096 [P] [US4] Create the `Comment` entity in `src/Taskify.Tasks.Api/Domain/Comment.cs`. Fields: `id` UUID ("Generated"), `taskId` UUID ("FK → Task"), `authorUserId` UUID ("Acting user at creation; never changes"), `text` text nullable ("1–2,000 chars after trim (FR-015); set to null on delete (FR-024)"), `createdAt` timestamptz ("Set by the server"), `editedAt` timestamptz nullable ("Set on each edit (FR-016)"), `deletedAt` timestamptz nullable ("Set on delete"). Add methods `Edit(text, actingUserId, now)` and `Delete(actingUserId, now)`, which return a result of `Ok`, `Forbidden` or `AlreadyDeleted` following the comment lifecycle in data-model.md
- [ ] T097 [US4] Add `Comments` to `TasksDbContext` (FK to Task, index (`taskId`, `createdAt`)) and add migration `AddComments` in `src/Taskify.Tasks.Api/Data/Migrations/`. Update the `GET /api/tasks` query in `src/Taskify.Tasks.Api/Endpoints/TaskReadEndpoints.cs` so `commentCount` counts only comments that are not deleted (depends on T096)
- [ ] T098 [P] [US4] Create `CommentTextValidator` (`text` "1–2,000 chars after trim (FR-015)") in `src/Taskify.Tasks.Api/Validation/CommentTextValidator.cs`
- [ ] T099 [US4] Implement the comment endpoints in `src/Taskify.Tasks.Api/Endpoints/CommentEndpoints.cs`:
  - `GET /api/tasks/{taskId}/comments`: oldest first, including deleted placeholders; `404` for an unknown task.
  - `POST /api/tasks/{taskId}/comments`: validated, with the acting user as author and the `writes` policy. Outbox `CommentAdded` with `taskId`, `projectId`, `title`, `commentId` and `assigneeUserId?`; no comment text.
  - `PUT /api/tasks/{taskId}/comments/{commentId}`: `403` for a non-author, `409` when deleted. Outbox `CommentEdited`.
  - `DELETE /api/tasks/{taskId}/comments/{commentId}`: `403` and `409` as above. Sets the text to null and returns the placeholder `Comment`. Outbox `CommentDeleted`.
  Audit every change and every `403` (IDs only, never the text) (depends on T096–T098)
- [ ] T100 [US4] Create the `CommentThread.razor` component in `src/Taskify.Web/Components/Shared/CommentThread.razor`:
  - lists comments oldest first with the author display name and posted time, plus "edited" when `editedAt` is set;
  - shows deleted comments as "Comment deleted by {author}" with the `deletedAt` time;
  - an add-comment box (limit 2,000, server errors shown);
  - Edit and Delete buttons only when `authorUserId` is the current user and the comment is not deleted.
  Add the thread to `src/Taskify.Web/Components/Pages/TaskDetails.razor`
- [ ] T101 [US4] Show the comment count on `src/Taskify.Web/Components/Board/TaskCard.razor` when `commentCount > 0`

**Checkpoint**: All four spec user stories work, and each can be tested on its own.

---

## Phase 7: User Story 5 - Real-time board updates (Plan-added; Priority: P3)

**Goal**: Changes by one user show on other open boards, task details and project lists within
2 seconds without a refresh (plan Performance Goals, research R5, contracts/realtime-hub.md).

**Independent Test**: Open the same board in two browsers as different users. Move, create, edit
and comment on a task in browser 1. Browser 2 updates within 2 seconds. Stop the Notifications
API, make a change, then restart it: browser 2 catches up after the reconnect, and a page
refresh always shows the saved state.

### Tests for User Story 5

- [ ] T102 [P] [US5] Unit tests for `EventEnvelopeValidator` and the per-type payload validators in `tests/Taskify.UnitTests/Validation/EventValidatorTests.cs`:
  - each of the 8 types: a valid payload is accepted;
  - rejected: unknown type, `version` other than 1, unknown or missing payload field, non-UUID IDs, a `title` over 200 characters, an unknown status value.
- [ ] T103 [P] [US5] Unit tests for `EventCallerPolicy` in `tests/Taskify.UnitTests/Security/EventCallerPolicyTests.cs`: `projects` may send only `ProjectCreated`; `tasks` may send only the other seven types; `web` may send nothing
- [ ] T104 [P] [US5] Integration tests in `tests/Taskify.IntegrationTests/Contracts/EventsContractTests.cs`:
  - `POST /internal/events` → `202`;
  - a repeated `eventId` → `202` with no second effect;
  - the wrong caller for the type → `403`;
  - the Web key → `403`;
  - a bad envelope → `400`;
  - a task move in the Tasks API reaches the hub as `BoardChanged` to group `project:{projectId}` within 2 s (assert with a test SignalR client using the web key).

### Implementation for User Story 5

- [ ] T105 [P] [US5] Create `ProcessedEvent` (`eventId` UUID primary key, `receivedAt`) for de-duplication in `src/Taskify.Notifications.Api/Domain/ProcessedEvent.cs`, add it to `NotificationsDbContext`, and add migration `AddProcessedEvents` in `src/Taskify.Notifications.Api/Data/Migrations/`
- [ ] T106 [P] [US5] Create `EventEnvelopeValidator` and one payload validator per event type in `src/Taskify.Notifications.Api/Validation/EventValidators.cs`. Fields must match contracts/events.md exactly, unknown fields are rejected, `version` must be 1, and `title` is limited to 1–200 characters. Comment that internal origin does not imply trust (Principle II)
- [ ] T107 [P] [US5] Create `EventCallerPolicy` in `src/Taskify.Notifications.Api/Security/EventCallerPolicy.cs`. It maps the caller from `ApiKeyMiddleware` to its allowed event types (the Projects key only for `ProjectCreated`; the Tasks key for the rest) and returns `403` otherwise
- [ ] T108 [US5] Create `BoardHub` at `/hubs/board` in `src/Taskify.Notifications.Api/Hubs/BoardHub.cs`, with methods `JoinProject` and `LeaveProject` (`projectId: Guid`), `JoinTask` and `LeaveTask` (`taskId: Guid`), and `JoinUser` and `LeaveUser` (`userId: Guid`, which must be a predefined user). Invalid IDs → `HubException("Invalid request")` with a generic message. The connection auto-joins `projects`. Only callers with the Web API key may connect (checked in `OnConnectedAsync` and by an auth filter)
- [ ] T109 [US5] Create `RealtimeBroadcaster` in `src/Taskify.Notifications.Api/Hubs/RealtimeBroadcaster.cs`. It maps each event type to its message: `BoardChanged` to `project:{projectId}`, `TaskChanged` to `task:{taskId}` for `TaskUpdated`, `TaskAssigned`, `TaskMoved` and the comment events, and `ProjectListChanged` to `projects`. Payloads contain IDs, the type, the actor and `occurredAt` only
- [ ] T110 [US5] Implement `POST /internal/events` in `src/Taskify.Notifications.Api/Endpoints/InternalEventEndpoints.cs`:
  - marked `SkipActingUser`;
  - validates the envelope and payload (T106) and checks the caller policy (T107);
  - in one transaction, inserts `ProcessedEvent` and runs the event handlers (US6 adds notification creation here); a duplicate `eventId` → `202` and nothing happens;
  - after the commit, calls `RealtimeBroadcaster`, then returns `202`;
  - audits rejections.
  (depends on T105–T109)
- [ ] T111 [US5] Create `RealtimeBoardService` (singleton) in `src/Taskify.Web/Services/RealtimeBoardService.cs`:
  - keeps one `HubConnection` to `https+http://notifications-api/hubs/board` with the Web API key header and automatic reconnect;
  - reference-counts group membership across circuits (`SubscribeProject`, `SubscribeTask`, `SubscribeUser` return `IDisposable`);
  - raises C# events per group;
  - on reconnect, rejoins all groups and raises a `Resync` event;
  - ignores `eventId`s it has already handled (bounded LRU).
- [ ] T112 [US5] Subscribe the pages to real-time signals, with every update re-fetched from the REST API rather than taken from the message contents (contracts/realtime-hub.md):
  - `src/Taskify.Web/Components/Pages/Board.razor` subscribes to `project:{id}` and re-fetches tasks on `BoardChanged` or `Resync`, using `InvokeAsync(StateHasChanged)`;
  - `src/Taskify.Web/Components/Pages/TaskDetails.razor` subscribes to `task:{id}` and re-fetches the task, history and comments;
  - `src/Taskify.Web/Components/Pages/Projects.razor` refreshes on `ProjectListChanged`;
  - subscriptions are disposed when the component is disposed.
- [ ] T113 [P] [US5] bUnit tests using a fake `RealtimeBoardService`: when a `BoardChanged` signal arrives the board re-fetches and re-renders, and the subscription is disposed when the component is disposed. In `tests/Taskify.Web.Tests/Realtime/RealtimeTests.cs`

**Checkpoint**: Boards update live. If the Notifications API is down, changes are still saved and
the board catches up after reconnect (outbox plus resync).

---

## Phase 8: User Story 6 - In-app notifications (Plan-added; Priority: P3)

**Goal**: A user gets an in-app notification when another user assigns a task to them, moves a
task assigned to them, or comments on a task assigned to them. Users can list notifications,
see the unread count, and mark one or all as read. Notifications are kept for 30 days
(research R10).

**Independent Test**: As Priya, assign a task to Jordan, move it, and comment on it. Switch to
Jordan: the bell shows 3 unread with server-generated summaries, opening a notification links to
the task, and "Mark all read" sets the count to 0. As Jordan, act on your own task: no
notification.

### Tests for User Story 6

- [ ] T114 [P] [US6] Unit tests for `NotificationTriggerRules` in `tests/Taskify.UnitTests/Notifications/TriggerRuleTests.cs`:
  - `TaskAssigned` notifies the new assignee when the assignee ≠ actor;
  - `TaskCreated` with an assignee ≠ actor creates a `TaskAssigned` notification;
  - `TaskMoved` notifies the current assignee when one exists and ≠ actor;
  - `CommentAdded` creates a `TaskCommented` notification for the assignee when one exists and ≠ actor;
  - other events and self-actions create nothing;
  - summaries are at most 300 characters and long titles are truncated.
- [ ] T115 [P] [US6] Contract and integration tests in `tests/Taskify.IntegrationTests/Contracts/NotificationsContractTests.cs`:
  - `GET /api/notifications` returns only the acting user's notifications, newest first;
  - `unreadOnly` works, and `limit` accepts 1–100 and rejects 0 and 101 with `400`;
  - `GET /api/notifications/unread-count` is correct;
  - `POST /api/notifications/{id}/read` is idempotent `204`, and another user's ID → `404`;
  - `POST /api/notifications/read-all` → `204`;
  - an event delivered twice creates one notification.
- [ ] T116 [P] [US6] bUnit tests for `NotificationBell` (shows the unread count, the list renders summaries as plain text, marking read updates the count, live `NotificationCreated` increments the count) in `tests/Taskify.Web.Tests/Notifications/NotificationBellTests.cs`

### Implementation for User Story 6

- [ ] T117 [P] [US6] Create the `Notification` entity in `src/Taskify.Notifications.Api/Domain/Notification.cs`. Fields: `id` UUID ("Generated"), `recipientUserId` UUID ("A seeded user, never the acting user"), `type` enum `TaskAssigned` | `TaskMoved` | `TaskCommented`, `taskId` UUID, `projectId` UUID, `actorUserId` UUID, `summary` text ("Generated by the server, max 300 chars"), `createdAt` timestamptz, `readAt` timestamptz nullable ("Null = unread"), `sourceEventId` UUID unique ("Event ID; makes event delivery idempotent")
- [ ] T118 [US6] Add `Notifications` to `NotificationsDbContext` (unique index on `sourceEventId`, plus index (`recipientUserId`, `readAt`, `createdAt` desc)) and add migration `AddNotifications` in `src/Taskify.Notifications.Api/Data/Migrations/` (depends on T117)
- [ ] T119 [US6] Create `NotificationTriggerRules` in `src/Taskify.Notifications.Api/Domain/NotificationTriggerRules.cs`. It follows the trigger table in data-model.md, plus `TaskCreated` with an assignee ≠ actor → `TaskAssigned` (contracts/events.md). Summaries are built from the actor display name (from the user directory), the task title and the column name, for example "Ana moved 'Login page' to In Review", truncated to 300 characters. The recipient must never be the actor
- [ ] T120 [US6] Add notification creation to the `/internal/events` handler in `src/Taskify.Notifications.Api/Endpoints/InternalEventEndpoints.cs`. It runs in the same transaction as `ProcessedEvent`, and after the commit it sends `NotificationCreated` to `user:{recipientUserId}` through `RealtimeBroadcaster` (depends on T110, T119)
- [ ] T121 [P] [US6] Create `ListNotificationsQueryValidator` (`limit` integer 1–100, default 50; `unreadOnly` boolean, default false) in `src/Taskify.Notifications.Api/Validation/ListNotificationsQueryValidator.cs`
- [ ] T122 [US6] Implement the endpoints in `src/Taskify.Notifications.Api/Endpoints/NotificationEndpoints.cs`:
  - `GET /api/notifications`: the acting user's notifications only, newest first;
  - `GET /api/notifications/unread-count`;
  - `POST /api/notifications/{notificationId}/read`: idempotent `204`; `404` when the notification is missing *or belongs to another user*, so it does not reveal that the notification exists;
  - `POST /api/notifications/read-all`: `204`.
  Audit the read-state changes (depends on T118, T121)
- [ ] T123 [P] [US6] Create `NotificationRetentionJob : BackgroundService`, which runs daily and deletes notifications whose `createdAt` is more than 30 days ago and `ProcessedEvent` rows older than 30 days, in `src/Taskify.Notifications.Api/Data/NotificationRetentionJob.cs`
- [ ] T124 [US6] Create the `NotificationBell.razor` component in `src/Taskify.Web/Components/Shared/NotificationBell.razor`:
  - the header badge shows the unread count;
  - a dropdown lists notifications (summary as plain text, relative time, a link to `/projects/{projectId}/tasks/{taskId}`, marked read on click) with a "Mark all read" button;
  - it subscribes to `user:{currentUserId}` through `RealtimeBoardService` and re-subscribes when the user switches.
  Add it to `src/Taskify.Web/Components/Layout/MainLayout.razor`

**Checkpoint**: All six stories work. Notifications follow the R10 rules.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, contract sync, end-to-end tests, performance, and keeping the spec
aligned with the plan.

- [ ] T125 [P] Write `src/Taskify.Projects.Api/README.md`. Cover its responsibility (projects and the user directory), endpoints, how to run and test it, configuration (DB connection, allowed API keys), dependencies and emitted events
- [ ] T126 [P] Write `src/Taskify.Tasks.Api/README.md`. Cover its responsibility (tasks, history, comments), endpoints, how to run and test it, configuration, its dependency on the Projects API, emitted events, and the DB permission rule on `status_changes`
- [ ] T127 [P] Write `src/Taskify.Notifications.Api/README.md`. Cover its responsibility (notifications and the board hub), endpoints, hub contract, event intake and caller policy, retention, and configuration
- [ ] T128 [P] Write `src/Taskify.Web/README.md`. Cover its responsibility, pages, how the acting user is stored (cookie and circuit), real-time wiring, security headers and CSP, and the `MarkupString` ban
- [ ] T129 [P] Write ADRs in `docs/adr/`: `0001-service-decomposition.md` (R2), `0002-outbox-http-dispatch.md` (R4), and `0003-phase1-identity-and-api-keys.md` (R8, deviations D1 and D2 with their expiry in phase 2)
- [ ] T130 Copy the contracts into the repo-level folder `contracts/` (or point CI at `specs/001-taskify-kanban-board/contracts/`). Add a CI step to `.github/workflows/ci.yml` that builds each API with `Microsoft.Extensions.ApiDescription.Server`, generates the OpenAPI documents and diffs them against the committed `*.yaml`, failing on drift (R12)
- [ ] T131 [P] End-to-end Playwright tests in `tests/Taskify.E2ETests/SmokeTests.cs`:
  - pick a user;
  - drag a card from To Do to In Progress and see it in under 1 s;
  - a second browser context sees the move in under 2 s;
  - the keyboard "Move to…" menu gives the same result;
  - a `<script>alert(1)</script>` title triggers no dialog;
  - the CSP header is present.
- [ ] T132 [P] Performance test: seed a project with 200 tasks and assert that the `Board` page loads and is interactive within 2 s (SC-007) in `tests/Taskify.E2ETests/PerformanceTests.cs`
- [ ] T133 [P] Injection test set (SC-006) in `tests/Taskify.IntegrationTests/Security/InjectionTests.cs`. Use common XSS payloads in titles, descriptions, comments and project names (script tags, `onerror` attributes, `javascript:` URLs, SVG). Each payload must be stored verbatim, returned verbatim, and HTML-encoded in rendered Web output. Also send SQL-injection strings and assert no 500 errors and no data change
- [ ] T134 [P] Audit-log test in `tests/Taskify.IntegrationTests/Security/AuditLogTests.cs`. Capture logs with a test logger provider. Assert that data changes and rejections produce audit events with the acting user, action, entity and outcome, and that no title, description or comment text appears in any log line (FR-022)
- [ ] T135 Persistence test (SC-004) in `tests/Taskify.IntegrationTests/PersistenceTests.cs`: create a project, task, move and comment; stop and restart the AppHost with the same data volume; confirm all of them are still there
- [ ] T136 Review every `.razor` file under `src/Taskify.Web/Components/`: there must be no `MarkupString` or `(MarkupString)` used with user data and no `@((MarkupString)`. Add a CI grep step that fails on `MarkupString` under `src/Taskify.Web` unless the line is allow-listed, in `.github/workflows/ci.yml`
- [ ] T137 Update `specs/001-taskify-kanban-board/spec.md` to match the plan (plan Summary, R10):
  - add user stories for real-time board updates (≤2 s) and in-app notifications, with acceptance scenarios that mirror the US5 and US6 phases above;
  - replace the assumption "live updates are not required";
  - remove "notifications" from the out-of-scope list;
  - add FRs for the notification triggers and 30-day retention.
  Then resolve the related items in `specs/001-taskify-kanban-board/checklists/security.md` (CHK018, CHK019)
- [ ] T138 Run every scenario in `specs/001-taskify-kanban-board/quickstart.md` (automated validation, manual scenarios 1–15 and the API smoke checks) and record the results in `specs/001-taskify-kanban-board/quickstart-results.md`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. **Blocks every user story.**
- **US1 (Phase 3)**: depends on Foundational only. This is the MVP.
- **US2 (Phase 4)**: depends on Foundational. It needs the `TaskItem` entity, seed tasks and
  board UI from US1 (T056–T058, T061–T064), so in practice it starts after US1.
- **US3 (Phase 5)**: depends on Foundational plus the `Project` and `TaskItem` entities and the
  `Board` and `TaskDetails` pages from US1. It is independent of US2.
- **US4 (Phase 6)**: depends on Foundational plus `TaskItem` and the `TaskDetails` page from US1.
  It is independent of US2 and US3.
- **US5 (Phase 7)**: depends on Foundational (outbox) and on at least one event-emitting story
  (US2, US3 or US4) to have anything to broadcast. Events written before US5 is deployed are
  delivered once the Notifications API accepts them.
- **US6 (Phase 8)**: depends on US5 (the `/internal/events` intake and the hub).
- **Polish (Phase 9)**: depends on the stories it covers. T137 (spec update) can run at any time.

### Story dependency graph

```text
Setup ─► Foundational ─► US1 (MVP) ─┬─► US2 ─┐
                                    ├─► US3 ─┼─► US5 ─► US6 ─► Polish
                                    └─► US4 ─┘
```

### Within each story

- Tests first; they should fail before you implement.
- Entities → DbContext and migration → validators → endpoints → Web components → page wiring.
- The outbox event and audit call are written in the same task as the endpoint that changes data.

---

## Parallel Opportunities

- **Setup**: T006–T012 run in parallel once T001–T005 exist.
- **Foundational**: T013–T015 (contracts) and T016–T023 (security pieces) all run in parallel.
  T035, T036 and T037 (service wiring) run in parallel after T025. T045–T048 run in parallel.
- **After US1**: US2, US3 and US4 can be built by different developers at the same time, because
  they touch different endpoint files, entities and components. Shared files that need care:
  `TasksDbContext.cs` (one migration per story; merge in order), `TaskDetails.razor` and
  `TaskCard.razor`.
- **Inside each story**: every test task marked [P] and every entity or validator marked [P] can
  start together.
- **Polish**: READMEs, ADRs and the E2E, performance, injection and audit tests (T125–T134) all run
  in parallel.

### Parallel example: User Story 1

```text
# Tests together:
Task: "T049 Contract tests for GET /api/projects… in tests/Taskify.IntegrationTests/Contracts/ProjectsReadContractTests.cs"
Task: "T050 Contract tests for GET /api/tasks… in tests/Taskify.IntegrationTests/Contracts/TasksReadContractTests.cs"
Task: "T051 bUnit tests for the board in tests/Taskify.Web.Tests/Board/BoardTests.cs"
Task: "T052 bUnit tests for UserSelect in tests/Taskify.Web.Tests/Pages/UserSelectTests.cs"

# Entities and components together:
Task: "T053 Project entity in src/Taskify.Projects.Api/Domain/Project.cs"
Task: "T056 TaskItem entity in src/Taskify.Tasks.Api/Domain/TaskItem.cs"
Task: "T061 TaskCard.razor in src/Taskify.Web/Components/Board/TaskCard.razor"
Task: "T062 BoardColumn.razor in src/Taskify.Web/Components/Board/BoardColumn.razor"
```

### Parallel example: User Story 3

```text
Task: "T080 CreateProjectValidator tests"   Task: "T081 Task validator tests"
Task: "T084 CreateProjectValidator"         Task: "T086 Task validators"
Task: "T089 CreateProjectForm.razor"        Task: "T090 TaskForm.razor + AssigneePicker.razor"
```

### Parallel example: User Story 4

```text
Task: "T092 CommentTextValidator tests"     Task: "T093 Comment lifecycle tests"
Task: "T096 Comment entity"                 Task: "T098 CommentTextValidator"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1: Setup.
2. Phase 2: Foundational. This is critical: security plumbing, the user directory and the AppHost.
3. Phase 3: US1.
4. **Stop and validate**: quickstart scenarios 1–2, with the T049–T052 tests passing.
5. Demo on the trusted internal network only (spec assumption, deviation D1).

### Incremental delivery

1. Setup + Foundational → the foundation is ready.
2. + US1 → the board can be viewed (MVP).
3. + US2 → the Kanban workflow and history (both P1 stories done).
4. + US3 → real projects and tasks.
5. + US4 → comments.
6. + US5 → live updates. + US6 → notifications.
7. Polish → docs, contract-drift check, E2E and security test sets, spec sync, quickstart run.

### Parallel team strategy

After Foundational and US1: developer A takes US2, developer B takes US3 and developer C takes
US4. Then one developer takes US5 and US6 (Notifications API and Web real-time) while the others
start on Polish.

---

## Notes

- [P] tasks touch different files and have no dependency on an unfinished task.
- Constraints from data-model.md are quoted in the entity and validator tasks. Do not loosen them
  while implementing.
- Commit after each task or logical group. Every commit must pass `dotnet build -warnaserror`
  (the XML-doc rule).
- Stop at any checkpoint to check that story on its own.
- `[x]` in this file tracks implementation progress. It is separate from the reviewer-owned
  requirement checklists in `checklists/`.
