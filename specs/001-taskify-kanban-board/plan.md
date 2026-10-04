# Implementation Plan: Taskify Kanban Board

**Branch**: `001-taskify-kanban-board` | **Date**: 2026-10-04 (revised after the second clarification session) | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-taskify-kanban-board/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Taskify phase 1 is a Kanban board for five predefined users (no login). Users can:
- create projects and tasks;
- assign tasks;
- move tasks between four columns, with a read-only status history;
- comment on tasks, editing and deleting only their own comments;
- see other users' changes live, within 2 seconds (User Story 5);
- receive in-app notifications about their tasks (User Story 6).

It is built as a .NET Aspire solution with three microservices and a Blazor Server frontend:
- a **Projects API** (projects and the user directory);
- a **Tasks API** (tasks, history, comments);
- a **Notifications API** (in-app notifications and a SignalR hub);
- a **Blazor Server** web app with native drag-and-drop and real-time board updates.

Each service owns its own PostgreSQL database. Services publish domain events through a
transactional outbox to the Notifications API, which turns them into notifications and real-time
signals ([research.md](research.md) R2–R5).

**Revision 2026-10-04**: the spec now includes live updates and notifications (FR-025–FR-030),
the last-save-wins rule for edits (FR-011), character counting (FR-019), rate limits (FR-031)
and user-selection auditing (FR-032). This revision brings the plan into line with them and
fixes the `/speckit-analyze` findings that belong in the plan:
- HTTPS only between services (R16);
- the API-key matrix and the exemption from the acting-user header (R8);
- a machine-readable message contract (R15);
- source IP in audit logs (R8, R13);
- an owner and review date for each accepted risk (Complexity Tracking).

## Technical Context

**Language/Version**: C# 14 on .NET 10 (LTS)

**Primary Dependencies**:
- .NET Aspire 13.x: AppHost, ServiceDefaults, `Aspire.Hosting.PostgreSQL`,
  `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`
- ASP.NET Core minimal APIs and `Microsoft.AspNetCore.OpenApi`
- EF Core 10 with Npgsql
- FluentValidation
- ASP.NET Core SignalR (hub and .NET client)
- ASP.NET Core rate limiting (`SlidingWindowRateLimiter`)
- Blazor Server (Interactive Server render mode)
- Contract tooling: AsyncAPI CLI (`npx @asyncapi/cli`, CI only) and JsonSchema.Net (contract tests)

**Storage**: PostgreSQL. One Aspire-managed server with three databases: `projectsdb`,
`tasksdb`, `notificationsdb`. Connections use TLS (R16).

**Testing**: xUnit v3, bUnit, `Aspire.Hosting.Testing` (integration and contract tests),
Playwright for .NET (end-to-end smoke)

**Target Platform**: Linux containers (deployed with Aspire); developed on Windows/macOS with
Docker; desktop web browsers

**Project Type**: Web application, built as microservices (3 REST APIs + Blazor Server frontend)

**Performance Goals**:
- A board with ≤200 tasks loads and is usable in ≤2 s (SC-007).
- A move shows in the acting user's UI in ≤1 s (SC-002).
- Other viewers see any change in ≤2 s (FR-025, SC-008).

**Constraints**:
- Every input is validated on the server, with lengths counted in user-perceived characters
  (Principle II, FR-019, R7).
- Rate limits: 60 writes and 300 reads per minute per user (FR-031, R9).
- No direct access to another service's database.
- HTTPS or TLS on every hop: browser, service to service, hub, database (Principle I, R16).
- Only the Web app is reachable from outside; the APIs are internal.
- Phase 1 runs only in a trusted internal environment (spec assumption).

**Scale/Scope**: 5 users, a handful of projects, ≤200 tasks per board, ≤10 concurrent browser
sessions, one instance per service. 6 user stories, 32 functional requirements, 10 success
criteria.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Section | Requirement | Plan compliance | Status |
|---|---|---|---|
| I. Security-First | Threat assessment in the plan | See "Threat assessment" below | ✅ |
| I. Security-First | Threat assessment in the **spec** | spec.md "Security & Threat Assessment" covers authentication, authorization, data exposure and abuse, plus residual risk | ✅ |
| I. Security-First | Least privilege | One DB user per service with access to its own database only; Tasks DB user cannot UPDATE or DELETE status history; each service accepts only the keys in the R8 key matrix, and only for the routes listed there | ✅ |
| I. Security-First | No secrets in source | Four API keys and the Postgres password are Aspire secret parameters (user secrets locally, secret store when deployed); the dev Postgres TLS key is generated at startup and never stored | ✅ |
| I. Security-First | TLS everywhere | `https://` service URIs only (never `https+http://`); APIs have no HTTP listener; hub over WSS; Npgsql `SSL Mode=Require` (dev) or `VerifyFull` (deployed); HSTS on Web (R16) | ✅ |
| I. Security-First | No sensitive data in logs or responses | Audit logs leave out text content, keys and cookies; Problem Details hide internal details (R13, R7) | ✅ |
| I. Security-First | Dependency vulnerabilities resolved | `dotnet list package --vulnerable` in CI fails on high or critical | ✅ |
| II. Validate All Inputs | Schema-based, server-side, allow-list | FluentValidation on every endpoint, unknown JSON fields rejected, enums allow-listed, grapheme-counted limits plus a code-unit abuse guard, 1 MB body limit (R7) | ✅ |
| II. Validate All Inputs | Context-aware encoding, parameterized queries | Razor encoding with `MarkupString` banned for user content; EF Core only (R9, R3) | ✅ |
| II. Validate All Inputs | Internal messages validated | `/internal/events` validates envelope and payload against the AsyncAPI schemas plus the caller policy; the hub validates every method argument; the Web app re-fetches instead of trusting signal content | ✅ |
| II. Validate All Inputs | Tests for every rule | Unit tests per validator and per hub method, accepted and rejected cases, including grapheme boundaries (R11) | ✅ |
| III. Microservices | One capability and one database per service | Projects / Tasks / Notifications, each with its own DB (R2, R3) | ✅ |
| III. Microservices | Versioned, documented contracts | OpenAPI 1.0.0 per API; AsyncAPI 3.1 document v1.0.0 for all messages ([contracts/](contracts/)) | ✅ |
| III. Microservices | Independently deployable | Separate projects and containers; outbox makes Notifications downtime harmless (FR-026) | ✅ |
| III. Microservices | Inter-service authentication and authorization | Per-caller API keys with a per-route key matrix and event types limited per caller (R8) | ⚠️ Deviation D2 |
| III. Microservices | Health checks, structured logs, correlation IDs | Aspire ServiceDefaults (`/health`, `/alive`, OpenTelemetry traces and logs) | ✅ |
| III. Microservices | New services justified | See R2; no organizational-only services | ✅ |
| IV. Documentation | Doc comments on public code | `GenerateDocumentationFile` with CS1591 as an error (R12) | ✅ |
| IV. Documentation | README per service; ADRs | README in each `src/*` project; `docs/adr/` with ADRs for R2, R4, R8 (including D1 and D2) and R16 | ✅ |
| IV. Documentation | Machine-readable contracts kept in sync | OpenAPI for REST and AsyncAPI for events and the hub; CI diffs generated OpenAPI against `contracts/*.yaml` and validates the AsyncAPI file; contract tests check payloads against the schemas (R12, R15) | ✅ |
| Security Req. | Standard authentication (OAuth/OIDC) | No end-user login in phase 1 | ⚠️ Deviation D1 |
| Security Req. | Authorization in every service | Each API checks the caller key against the key matrix and the acting user; comment ownership is checked in the Tasks API and notification ownership in the Notifications API | ✅ |
| Security Req. | Audit logging of security events | Data changes, every rejection, user selection and switch with source IP (FR-032), refused hub connections; `taskify.rejections` metric with an abnormal-rate alert (R13) | ✅ |
| Security Req. | Rate limiting on public endpoints | 60 writes and 300 reads per minute per user in each API, per-IP limit on Web, per-caller limit on `/internal/events` (FR-031, R9) | ✅ |
| Security Req. | Static analysis and dependency scan in CI | CodeQL, .NET analyzers, vulnerable-package check, secret scanning | ✅ |
| Security Req. | Accepted risks documented with an owner and review date | D1 and D2 each have an owner, review date and expiry (Complexity Tracking) | ✅ |
| Quality Gates | Review, tests, security, docs, contract compatibility | Listed in the CI pipeline (R12); contract tests in the integration project | ✅ |

**Gate result (pre-research)**: PASS, with two justified, time-limited deviations (D1 and D2)
that have owners and review dates. The spec now includes its own threat assessment
(Principle I).

**Gate result (post-design re-check)**: PASS for all design artifacts.
- The new contracts (AsyncAPI, the 429 responses, the key matrix, the exempt user routes)
  introduce no violations.
- They close the earlier gaps: TLS fallback, the machine-readable message contract, untested
  hub validation, and auditing of user selection.
- Status history is protected by database permissions, comment and notification ownership are
  enforced server-side, the event intake checks caller keys, and only the Web resource is
  reachable from outside.
- The spec's threat assessment matches the plan's threat table.

### Threat assessment (phase 1)

| Threat | Impact | Mitigation |
|---|---|---|
| Impersonation: anyone can act as any user (no login) | Fake authorship, comment tampering, reading another user's notifications | Accepted for phase 1 (D1); trusted internal network only; every selection and switch, and every change, is audited with the source IP (FR-032, R13) |
| Direct calls to internal APIs | Bypassing Web checks | APIs not exposed externally; key matrix per route (R8); every API re-validates input, the acting user and the rate limits |
| A compromised service key used against other routes | Wider access than the service needs | The key matrix limits each key to specific routes and event types; keys are rotatable secrets (D2) |
| Stored XSS in titles, descriptions, comments or notification summaries | Script runs in other users' sessions | Razor encoding, `MarkupString` banned, CSP header, SC-006 tests; summaries are rendered as plain text |
| SQL injection | Data theft or corruption | EF Core parameterized queries only; no raw SQL with user input |
| Forged or malformed events to Notifications | Fake notifications, crashes | `/internal/events` limited to the Projects and Tasks keys, event types limited per key, AsyncAPI schema validation, de-duplication by `eventId` |
| Hub group snooping | Receiving signals for other users' notifications | Only the Web key may connect; browsers never connect to the hub; the Web app joins `user:{id}` only for the user selected in that circuit |
| Tampering with status history | Loss of accountability | No update or delete endpoints; DB permissions deny UPDATE and DELETE |
| Flooding, oversized or Zalgo input | Degraded service | Rate limits (FR-031); grapheme limits plus a code-unit guard; 1 MB body limit; rejection-rate alert |
| Plaintext interception inside the network | Data and key disclosure | HTTPS only between services, WSS for the hub, TLS to PostgreSQL (R16) |
| Secrets leakage | Full compromise | Aspire secret parameters; no secrets in the repo; secret scanning in CI; the dev TLS key is generated at runtime |

## Project Structure

### Documentation (this feature)

```text
specs/001-taskify-kanban-board/
├── plan.md              # This file
├── research.md          # Phase 0 output (R1–R16)
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── projects-api.yaml        # OpenAPI 3.1
│   ├── tasks-api.yaml           # OpenAPI 3.1
│   ├── notifications-api.yaml   # OpenAPI 3.1
│   ├── events.asyncapi.yaml     # AsyncAPI 3.1: domain events + hub messages (authoritative)
│   ├── events.md                # readable guide to the events
│   └── realtime-hub.md          # readable guide to the hub
├── checklists/
│   ├── requirements.md
│   └── security.md
└── tasks.md             # Phase 2 output (/speckit-tasks); must be regenerated after this revision
```

### Source Code (repository root)

```text
Taskify.slnx
Directory.Build.props              # nullable, warnings-as-errors, XML docs, analyzers
Directory.Packages.props           # central package versions
docs/
└── adr/                           # Architecture Decision Records
src/
├── Taskify.AppHost/               # Aspire orchestration: postgres (TLS) + 3 DBs, services, 4 API keys,
│                                  #   HTTPS-only endpoints, service references
├── Taskify.ServiceDefaults/       # OpenTelemetry, health checks, resilience, service discovery
├── Taskify.Contracts/             # Shared event types (v1), status enum, seed IDs
├── Taskify.Security/              # API key matrix + acting-user middleware, client-IP handling,
│                                  #   TextLength (grapheme) helper, validation endpoint filter,
│                                  #   Problem Details, audit logging + rejection metric,
│                                  #   rate-limit policies, outbox writer/dispatcher
├── Taskify.Projects.Api/
│   ├── Endpoints/                 # /api/users (no acting user), /api/projects
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
│   ├── Hubs/                      # BoardHub (/hubs/board), RealtimeBroadcaster
│   ├── Domain/                    # Notification, ProcessedEvent, trigger rules
│   ├── Validation/                # event + hub-argument + query validators
│   ├── Clients/                   # Projects API client (user directory)
│   ├── Data/                      # NotificationsDbContext, migrations, retention job
│   └── README.md
└── Taskify.Web/
    ├── Components/
    │   ├── Pages/                 # UserSelect, Projects, Board, TaskDetails, NotFound
    │   ├── Board/                 # BoardColumn, TaskCard, MoveToMenu
    │   └── Shared/                # NotificationBell, CommentThread, StatusHistory, forms
    ├── Services/                  # typed API clients, CurrentUser (+ selection audit),
    │                              #   ClientIp, RealtimeBoardService (coalesced re-fetch)
    └── README.md
tests/
├── Taskify.UnitTests/             # validators (incl. grapheme boundaries), hub arguments,
│                                  #   domain rules, notification triggers, middleware, rate limits
├── Taskify.Web.Tests/             # bUnit component tests
├── Taskify.IntegrationTests/      # Aspire.Hosting.Testing: API, OpenAPI + AsyncAPI contract,
│                                  #   event flow, TLS, key matrix, audit, rate-limit tests
└── Taskify.E2ETests/              # Playwright smoke (drag, real-time, XSS, performance)
```

**Structure Decision**:
- A single Aspire solution with one project per microservice under `src/`, so each can be
  built, tested and deployed on its own.
- `Taskify.Contracts` holds only versioned message types and seed IDs (no business logic).
- `Taskify.Security` holds cross-cutting security plumbing, so every service enforces the same
  rules in the same way. That includes the grapheme `TextLength` helper, which the Web
  counters reuse so the UI and the APIs always agree.
- Tests are grouped by level under `tests/`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| **D1**: No end-user authentication (Security Requirements: OAuth/OIDC) | Spec phase 1 explicitly has no login, and users are picked from a list | Adding OIDC now goes against the agreed phase 1 scope. Mitigated by trusted-network-only deployment, server-side validation of the acting user, rate limits, and audit logs of every selection and change with the source IP. **Owner**: Adrian Rogalczyk (project maintainer). **Review date**: 2027-01-04, then each quarterly constitution review. **Expires**: when phase 2 adds OIDC sign-in and the `X-Taskify-User` header is replaced by token claims |
| **D2**: Service-to-service auth uses per-caller API keys, not OAuth 2.0 client credentials | Phase 1 has no identity provider | Running an identity provider only for three internal services is out of proportion for phase 1. Keys are secrets stored per caller, limited by the R8 key matrix and per event type, sent only over TLS, and rotatable. **Owner**: Adrian Rogalczyk (project maintainer). **Review date**: 2027-01-04, then each quarterly constitution review. **Expires**: when phase 2 introduces client credentials from the same identity provider as D1 |
| Shared `Taskify.Contracts` library across services | Publishers and the consumer need identical event definitions | Copying event types into each service risks contract drift. The library holds versioned types only, and Principle III's versioning rules apply to it. Contract tests check it against the AsyncAPI document |
