# Implementation Plan: Taskify Kanban Board

**Branch**: `001-taskify-kanban-board` | **Date**: 2026-10-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-taskify-kanban-board/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Taskify phase 1 is a Kanban board for five predefined users (no login). Users can:
- create projects and tasks;
- assign tasks;
- move tasks between four columns, with a read-only status history;
- comment on tasks, editing and deleting only their own comments.

It is built as a .NET Aspire solution with three microservices and a Blazor Server frontend:
- a **Projects API** (projects and the user directory);
- a **Tasks API** (tasks, history, comments);
- a **Notifications API** (in-app notifications and a SignalR hub);
- a **Blazor Server** web app with native drag-and-drop and real-time board updates.

Each service owns its own PostgreSQL database. Services publish domain events through a
transactional outbox to the Notifications API, which turns them into notifications and real-time
signals ([research.md](research.md) R2–R5).

**Spec changes from the planning input** (the spec should be updated to match):
1. **Real-time updates**: the spec's assumption "live updates are not required" is replaced by
   live board updates within 2 seconds.
2. **Notifications**: a new in-app notifications capability with a REST API (R10). The spec
   currently lists notifications as out of scope.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (LTS)

**Primary Dependencies**:
- .NET Aspire 13.x: AppHost, ServiceDefaults, `Aspire.Hosting.PostgreSQL`,
  `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`
- ASP.NET Core minimal APIs and `Microsoft.AspNetCore.OpenApi`
- EF Core 10 with Npgsql
- FluentValidation
- ASP.NET Core SignalR (hub and .NET client)
- Blazor Server (Interactive Server render mode)

**Storage**: PostgreSQL. One Aspire-managed server with three databases: `projectsdb`,
`tasksdb`, `notificationsdb`.

**Testing**: xUnit v3, bUnit, `Aspire.Hosting.Testing` (integration and contract tests),
Playwright for .NET (end-to-end smoke)

**Target Platform**: Linux containers (deployed with Aspire); developed on Windows/macOS with
Docker; desktop web browsers

**Project Type**: Web application, built as microservices (3 REST APIs + Blazor Server frontend)

**Performance Goals**:
- A board with ≤200 tasks loads and is usable in ≤2 s (SC-007).
- A move shows in the acting user's UI in ≤1 s (SC-002).
- Other viewers see the change in ≤2 s.

**Constraints**:
- Every input is validated on the server (Principle II).
- No direct access to another service's database.
- Only the Web app is reachable from outside; the APIs are internal.
- Phase 1 runs only in a trusted internal environment (spec assumption).

**Scale/Scope**: 5 users, a handful of projects, ≤200 tasks per board, ≤10 concurrent browser
sessions. 4 user stories, 24 functional requirements.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Section | Requirement | Plan compliance | Status |
|---|---|---|---|
| I. Security-First | Threat assessment | See "Threat assessment" below | ✅ |
| I. Security-First | Least privilege | One DB user per service with access to its own database only; Tasks DB user cannot UPDATE/DELETE status history; API keys are per caller | ✅ |
| I. Security-First | No secrets in source | API keys and Postgres password are Aspire secret parameters (user secrets locally, secret store when deployed) | ✅ |
| I. Security-First | TLS everywhere | HTTPS endpoints for all resources; HSTS on Web | ✅ |
| I. Security-First | No sensitive data in logs or responses | Audit logs leave out text content; Problem Details hide internal details (R13, R7) | ✅ |
| I. Security-First | Dependency vulnerabilities resolved | `dotnet list package --vulnerable` in CI fails on high or critical | ✅ |
| II. Validate All Inputs | Schema-based, server-side, allow-list | FluentValidation on every endpoint, unknown JSON fields rejected, enums allow-listed (R7) | ✅ |
| II. Validate All Inputs | Context-aware encoding, parameterized queries | Razor encoding with `MarkupString` banned for user content; EF Core only (R9, R3) | ✅ |
| II. Validate All Inputs | Internal messages validated | `/internal/events` validates envelope, payload, and caller key per event type ([events.md](contracts/events.md)) | ✅ |
| II. Validate All Inputs | Tests for every rule | Unit tests per validator, both accepted and rejected cases (R11) | ✅ |
| III. Microservices | One capability and one database per service | Projects / Tasks / Notifications, each with its own DB (R2, R3) | ✅ |
| III. Microservices | Versioned, documented contracts | OpenAPI 1.0.0 for each API; events have `version: 1` ([contracts/](contracts/)) | ✅ |
| III. Microservices | Independently deployable | Separate projects and containers; outbox makes Notifications downtime harmless | ✅ |
| III. Microservices | Inter-service authentication and authorization | Per-caller API keys, with event types limited per caller (R8) | ⚠️ Deviation D2 |
| III. Microservices | Health checks, structured logs, correlation IDs | Aspire ServiceDefaults (`/health`, `/alive`, OpenTelemetry traces and logs) | ✅ |
| III. Microservices | New services justified | See R2; no organizational-only services | ✅ |
| IV. Documentation | Doc comments on public code | `GenerateDocumentationFile` with CS1591 as an error (R12) | ✅ |
| IV. Documentation | README per service; ADRs | README in each `src/*` project; `docs/adr/` with ADRs for R2, R4, R8 | ✅ |
| IV. Documentation | Machine-readable contracts kept in sync | CI compares generated OpenAPI with `contracts/*.yaml` | ✅ |
| Security Req. | Standard authentication (OAuth/OIDC) | No end-user login in phase 1 | ⚠️ Deviation D1 |
| Security Req. | Authorization in every service | Each API checks the acting user and API key; comment ownership checked in the Tasks API | ✅ |
| Security Req. | Audit logging of security events | Rejected requests and data changes logged (R13, FR-022) | ✅ |
| Security Req. | Rate limiting on public endpoints | ASP.NET Core rate limiter on Web and on every API | ✅ |
| Security Req. | Static analysis and dependency scan in CI | CodeQL, .NET analyzers, vulnerable-package check | ✅ |
| Quality Gates | Review, tests, security, docs, contract compatibility | Listed in CI pipeline (R12); contract tests in the integration project | ✅ |

**Gate result (pre-research)**: PASS with two justified, time-limited deviations (D1, D2), listed
in Complexity Tracking.

**Gate result (post-design re-check)**: PASS. The contracts, data model and quickstart introduce
no new violations. Status history is protected by database permissions, comment ownership is
enforced server-side (403), the event intake checks caller keys, and only the Web resource is
reachable from outside.

### Threat assessment (phase 1)

| Threat | Impact | Mitigation |
|---|---|---|
| Impersonation: anyone can act as any user (no login) | Fake authorship, comment tampering | Accepted for phase 1 (D1); trusted internal network only; audit log of acting user and source IP |
| Direct calls to internal APIs | Bypassing Web checks | APIs not exposed externally; per-caller API keys; every API re-validates input and acting user |
| Stored XSS in titles, descriptions or comments | Script runs in other users' sessions | Razor encoding, `MarkupString` banned, CSP header, SC-006 tests |
| SQL injection | Data theft or corruption | EF Core parameterized queries only; no raw SQL with user input |
| Forged events to Notifications | Fake notifications | `/internal/events` limited to Projects and Tasks keys, with event types limited per key; schema validation |
| Tampering with status history | Loss of accountability | No update/delete endpoints; DB permissions deny UPDATE/DELETE |
| Flooding / abuse | Degraded service | Rate limiting; length limits on every field |
| Secrets leakage | Full compromise | Aspire secret parameters; no secrets in repo; secret scanning in CI |

## Project Structure

### Documentation (this feature)

```text
specs/001-taskify-kanban-board/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── projects-api.yaml
│   ├── tasks-api.yaml
│   ├── notifications-api.yaml
│   ├── events.md
│   └── realtime-hub.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks - not created here)
```

### Source Code (repository root)

```text
Taskify.slnx
Directory.Build.props              # nullable, warnings-as-errors, XML docs, analyzers
Directory.Packages.props           # central package versions
docs/
└── adr/                           # Architecture Decision Records
src/
├── Taskify.AppHost/               # Aspire orchestration: postgres + 3 DBs, services, secrets
├── Taskify.ServiceDefaults/       # OpenTelemetry, health checks, resilience, service discovery
├── Taskify.Contracts/             # Shared event types (v1), status enum, seed IDs
├── Taskify.Security/              # API key + acting-user middleware, validation endpoint filter,
│                                  #   Problem Details, audit logging, rate-limit policies
├── Taskify.Projects.Api/
│   ├── Endpoints/                 # /api/users, /api/projects
│   ├── Domain/                    # User, Project
│   ├── Validation/                # FluentValidation validators
│   ├── Data/                      # ProjectsDbContext, migrations, seed, outbox
│   └── README.md
├── Taskify.Tasks.Api/
│   ├── Endpoints/                 # /api/tasks, moves, history, comments
│   ├── Domain/                    # TaskItem, StatusChange, Comment
│   ├── Validation/
│   ├── Clients/                   # Projects API client (+ cached user directory)
│   ├── Data/                      # TasksDbContext, migrations, seed, outbox
│   └── README.md
├── Taskify.Notifications.Api/
│   ├── Endpoints/                 # /api/notifications, /internal/events
│   ├── Hubs/                      # BoardHub (/hubs/board)
│   ├── Domain/                    # Notification, trigger rules
│   ├── Validation/
│   ├── Data/                      # NotificationsDbContext, migrations, retention job
│   └── README.md
└── Taskify.Web/
    ├── Components/
    │   ├── Pages/                 # UserSelect, Projects, Board, TaskDetails
    │   ├── Board/                 # BoardColumn, TaskCard, MoveToMenu
    │   └── Shared/                # NotificationBell, CommentThread, StatusHistory
    ├── Services/                  # typed API clients, CurrentUser, RealtimeBoardService
    └── README.md
tests/
├── Taskify.UnitTests/             # validators, domain rules, notification triggers
├── Taskify.Web.Tests/             # bUnit component tests
├── Taskify.IntegrationTests/      # Aspire.Hosting.Testing: API + contract + event flow tests
└── Taskify.E2ETests/              # Playwright smoke (drag, real-time, XSS)
```

**Structure Decision**:
- A single Aspire solution with one project per microservice under `src/`, so each can be
  built, tested and deployed on its own.
- `Taskify.Contracts` holds only versioned message types and seed IDs (no business logic).
- `Taskify.Security` holds cross-cutting security plumbing, so every service enforces the same
  rules in the same way.
- Tests are grouped by level under `tests/`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| **D1**: No end-user authentication (Security Requirements: OAuth/OIDC) | Spec phase 1 explicitly has no login, and users are picked from a list | Adding OIDC now goes against the agreed phase 1 scope. Mitigated by trusted-network-only deployment, server-side validation of the acting user, and audit logs. **Expires**: phase 2 adds OIDC sign-in, and the `X-Taskify-User` header is replaced by token claims |
| **D2**: Service-to-service auth uses per-caller API keys, not OAuth 2.0 client credentials | Phase 1 has no identity provider | Running an identity provider only for three internal services is out of proportion for phase 1. Keys are secrets stored per caller, limited per event type, and rotatable. **Expires**: phase 2 introduces client credentials from the same identity provider as D1 |
| Shared `Taskify.Contracts` library across services | Publishers and the consumer need identical event definitions | Copying event types into each service risks contract drift. The library holds versioned types only, and Principle III's versioning rules apply to it |
