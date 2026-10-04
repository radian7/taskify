# Research: Taskify Kanban Board

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-04

Each section records a decision, why it was chosen, and the alternatives considered. Inputs fixed
by the user: .NET Aspire, PostgreSQL, Blazor Server frontend with drag-and-drop and real-time
updates, REST APIs for projects, tasks, and notifications.

## R1. Runtime and orchestration

- **Decision**: .NET 10 (LTS, SDK 10.0.1xx is installed) with the latest stable .NET Aspire
  (13.x) AppHost for local orchestration, service discovery, configuration, and telemetry.
- **Rationale**: .NET 10 is the current LTS. Aspire wires services, PostgreSQL, secrets and
  OpenTelemetry together and gives a dashboard for logs and traces, which covers the
  constitution's observability requirements (Principle III) without custom plumbing.
- **Alternatives considered**: Docker Compose only (no service discovery, no telemetry
  dashboard, more hand-written config); .NET 8 (older LTS, shorter remaining support).

## R2. Service decomposition

- **Decision**: Three backend services plus the web frontend:
  1. **Projects API**: projects and the read-only user directory (five predefined users).
  2. **Tasks API**: tasks, status history, and comments (one aggregate: a task owns its history
     and comments).
  3. **Notifications API**: per-user notifications and the real-time event hub.
  4. **Web**: Blazor Server UI; the only externally reachable resource.
- **Rationale**: Matches the requested REST surfaces (projects, tasks, notifications) and the
  constitution's rule of one capability and one database per service. Comments and history stay
  with tasks because they are always read and authorized together with a task; splitting them
  out would add cross-service calls with no independent scaling benefit.
- **Alternatives considered**: A separate Users service (rejected: five fixed records, no
  behavior; it would be an organizational-only service, which the constitution discourages);
  a separate Comments service (rejected for the reason above); a single monolith (rejected by
  Principle III).

## R3. Data storage

- **Decision**: One Aspire-managed PostgreSQL server resource with three databases:
  `projectsdb`, `tasksdb`, `notificationsdb`. Each service connects only to its own database
  using its own credentials. Data access uses EF Core with the Npgsql provider through the Aspire
  integration (`AddNpgsqlDbContext`). Schema changes are EF Core migrations applied at startup
  in development and by a migration step in deployment.
- **Rationale**: Separate databases enforce data ownership (Principle III) while keeping one
  server cheap for phase 1. EF Core always uses parameterized queries (Principle II).
- **Alternatives considered**: One shared database with schemas per service (weaker isolation,
  tempts cross-schema joins); Dapper (more hand-written SQL, no migrations).

## R4. Inter-service communication and events

- **Decision**:
  - **Synchronous reads**: the Tasks API calls the Projects API over HTTP (Aspire service
    discovery) to check that a project exists and that a user ID is one of the predefined users.
    The user list is cached in memory because it never changes in phase 1.
  - **Events**: the Tasks API writes domain events (`TaskCreated`, `TaskUpdated`,
    `TaskAssigned`, `TaskMoved`, `CommentAdded`, `CommentEdited`, `CommentDeleted`) to an
    **outbox table** in the same transaction as the change. A background dispatcher delivers
    them to the Notifications API's internal events endpoint with retries. The Projects API
    does the same for `ProjectCreated`.
- **Rationale**: The outbox guarantees that every saved change produces an event, even if the
  Notifications API is briefly down, without adding a message broker to phase 1.
- **Alternatives considered**: RabbitMQ or Azure Service Bus (more infrastructure for three
  services; can replace the HTTP dispatcher later without changing event contracts); direct
  fire-and-forget HTTP calls (events lost on failure); PostgreSQL LISTEN/NOTIFY (couples
  services to one database server).

## R5. Real-time updates

- **Decision**: The Notifications API hosts an ASP.NET Core SignalR hub (`/hubs/board`). On each
  event it broadcasts to groups `project:{projectId}` (board changes) and `user:{userId}`
  (personal notifications). The Web server keeps one SignalR client connection to the hub,
  joins groups for the boards and users that open circuits are viewing, and pushes updates into
  the Blazor Server components, which re-render over their existing circuits.
- **Rationale**: Blazor Server already uses SignalR to the browser, so the browser needs no
  extra connection. Keeping the hub in the Notifications service means the Tasks service stays
  a plain REST API.
- **Alternatives considered**: Polling every few seconds (slower, more load); a hub inside the
  Web app fed by the Tasks API directly (couples Tasks to the UI).
- **Target**: changes appear on other open boards within 2 seconds (see plan Performance Goals).

## R6. Drag-and-drop

- **Decision**: Native HTML5 drag-and-drop using Blazor's `@ondragstart`, `@ondragover`, and
  `@ondrop` events, with no JavaScript library. Each card also has a keyboard-accessible
  "Move to…" menu that calls the same move action.
- **Rationale**: Moves are between four fixed columns with no in-column ordering (spec
  assumption), so native events are enough. The menu gives a non-mouse path and an easy
  target for tests.
- **Alternatives considered**: MudBlazor `MudDropContainer` or SortableJS interop (extra
  dependency, in-column reordering not needed).
- **Behavior**: the card moves straight away in the UI; if the API rejects the move, it snaps
  back and an error is shown.

## R7. Input validation

- **Decision**: FluentValidation validators in each API for every request body and query/route
  parameter, run by a shared endpoint filter. Strings are trimmed before length checks. Enums
  (column) accept only the four defined values. Unknown JSON properties are rejected. Errors
  are returned as RFC 9457 Problem Details (`400`) listing field errors without internal detail.
  The Blazor forms use the same limits for instant feedback, but the API is the authority.
- **Rationale**: Principle II requires explicit, allow-list, server-side validation with tests.
  FluentValidation supports trim-aware and cross-field rules that DataAnnotations do not handle
  cleanly, and each validator is easy to unit-test.
- **Alternatives considered**: DataAnnotations with .NET 10 built-in minimal API validation
  (cannot express "length after trim" without custom attributes).

## R8. Acting user and inter-service authentication (phase 1)

- **Decision**:
  - The Web app stores the selected user in the circuit (and a per-browser cookie so it survives
    refresh) and sends it to APIs as the `X-Taskify-User` header (a user UUID).
  - Every API checks that the header is present and names one of the five users, and uses it for
    authorship and authorization checks (for example, comment ownership in the Tasks API).
  - Every service-to-service call carries an `X-Api-Key` header holding a per-caller secret
    from an Aspire secret parameter. Each API accepts only the keys of its allowed callers.
  - Only the Web resource has an external endpoint; APIs are internal to the Aspire network.
- **Rationale**: The spec has no login in phase 1. API keys still stop calls from unknown
  callers between services, and the deviation is recorded in plan Complexity Tracking.
- **Phase 2 path**: replace the user header with OpenID Connect sign-in, and API keys with
  OAuth 2.0 client credentials from an identity provider (for example Keycloak via its Aspire
  integration). Contracts keep the same shape.
- **Alternatives considered**: Adding OIDC now (out of scope per spec); no inter-service auth
  (violates Principle III).

## R9. Output encoding and web security

- **Decision**: All user text is rendered through normal Razor binding, which HTML-encodes it.
  `MarkupString` is banned for user content (enforced in code review). HTTPS/HSTS everywhere,
  antiforgery on Blazor forms, security headers (CSP, `X-Content-Type-Options`, frame
  denial) on the Web app, and the built-in ASP.NET Core rate limiter on the Web app and all APIs.
- **Rationale**: Covers FR-020, SC-006, and the constitution's Security Requirements.

## R10. Notifications (scope added by planning input)

- **Decision**: In-app notifications only. A notification is created for a user when another
  user:
  - assigns a task to them;
  - moves a task assigned to them;
  - comments on a task assigned to them.
  Users never get notifications for their own actions. Notifications can be listed, counted as
  unread, marked read singly, or all marked read. They are stored for 30 days.
- **Rationale**: The planning input asks for a notifications REST API, which the spec does not
  describe. These triggers are the smallest useful set based on the clarified card behavior.
  **Spec follow-up**: the spec should be updated to add a notifications story, replace the
  "live updates are not required" assumption, and remove "notifications" from its out-of-scope
  list (see plan Summary).
- **Alternatives considered**: Email or push notifications (out of scope; need delivery
  infrastructure); notifying every project member of every change (too noisy for five users).

## R11. Testing

- **Decision**:
  - **Unit**: xUnit v3 for validators, domain rules (comment ownership, history on move, no-op
    moves), and notification trigger rules.
  - **Component**: bUnit for Blazor components (board columns, card highlighting, comment
    actions hidden for non-authors).
  - **Integration and contract**: `Aspire.Hosting.Testing` starts the real AppHost with
    PostgreSQL in a container. Tests call each API and check responses against the OpenAPI
    contracts in `contracts/`.
  - **End-to-end smoke**: Playwright for .NET covering pick user, drag card, and see update in
    a second browser.
- **Rationale**: Principle II requires accepted and rejected cases for every validation rule.
  The quality gates require contract tests for changed interfaces.

## R12. Documentation and quality gates

- **Decision**:
  - `GenerateDocumentationFile` is on in every project, with missing XML docs (CS1591) treated as
    an error.
  - OpenAPI documents are generated from the code (`Microsoft.AspNetCore.OpenApi`) and compared
    in CI against the committed contracts.
  - Each service has a README, and ADRs live in `docs/adr/`.
  - CI runs `dotnet build -warnaserror`, the tests, the .NET analyzers, CodeQL (static
    analysis), and `dotnet list package --vulnerable` (dependency scan).
- **Rationale**: Enforces Principle IV and the quality gates automatically instead of relying on
  review alone.

## R13. Audit logging

- **Decision**: Structured logs through the Aspire ServiceDefaults OpenTelemetry pipeline. Each
  data change and each rejected request logs an audit event with acting user ID, action, entity
  type and ID, outcome, and correlation ID. Titles, descriptions and comment text are never
  logged.
- **Rationale**: FR-022 and the constitution's audit logging requirement.
