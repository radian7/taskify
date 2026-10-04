# Research: Taskify Kanban Board

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-04

Each section records a decision, why it was chosen, and the alternatives considered. Inputs fixed
by the user: .NET Aspire, PostgreSQL, Blazor Server frontend with drag-and-drop and real-time
updates, REST APIs for projects, tasks, and notifications.

**Revision 2026-10-04 (after the second clarification session)**: R3, R4, R5, R7, R8, R9, R10 and
R13 are updated and R14–R16 are added. The updates cover the clarified spec (FR-011 last save
wins, FR-019 character counting, FR-025–FR-032) and the analysis findings: HTTPS only between
services, the API-key matrix, the exemption from the acting-user header, a machine-readable
message contract, and source IP in audit logs.

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
  in development and by a migration step in deployment. Database connections use TLS (see R16).
  Text columns are PostgreSQL `text` with no length limit in the database, because limits are
  counted in user-perceived characters and enforced by the API validators (R7).
- **Rationale**: Separate databases enforce data ownership (Principle III) while keeping one
  server cheap for phase 1. EF Core always uses parameterized queries (Principle II).
- **Alternatives considered**: One shared database with schemas per service (weaker isolation,
  tempts cross-schema joins); Dapper (more hand-written SQL, no migrations).

## R4. Inter-service communication and events

- **Decision**:
  - **Synchronous reads**: the Tasks API calls the Projects API over HTTPS (Aspire service
    discovery with `https://` service URIs, never `https+http://`, see R16) to check that a
    project exists and that a user ID is one of the predefined users.
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
- **Target**: changes appear on other open screens within 2 seconds (spec FR-025, SC-008).
- **Catch-up (FR-026)**: the outbox keeps events while the Notifications API is down. On
  reconnect, the Web server rejoins its groups and re-fetches every open screen, so nothing is
  missed. A page refresh always shows the saved state.
- **Read budget**: re-fetches triggered by signals are coalesced to at most one per second per
  open screen. Each re-fetch is 1–2 API reads, so a viewer stays far below the 300 reads per
  minute limit (FR-031) even while four other users each make 60 changes per minute.

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
- **Character counting (FR-019)**: lengths are counted in grapheme clusters (user-perceived
  characters) with `System.Globalization.StringInfo.LengthInTextElements`, which follows Unicode
  UAX #29 in .NET 5 and later. An emoji, a flag, or a letter plus combining accent counts as 1.
  The shared `TextLength` helper in `Taskify.Security` is used by every validator **and** by
  the Blazor character counters. Blazor Server runs the same .NET code, so what the user sees
  and what the API enforces always match.
- **Abuse guard**: one grapheme cluster can contain any number of combining marks ("Zalgo"
  text). So each text field is also rejected when it is longer than **16 UTF-16 code units per
  allowed character** (for example 3,200 for a 200-character title). The guard runs *before*
  grapheme counting so counting cost stays bounded. It is above every common real character:
  a family emoji is 11 code units, and a kiss emoji with two skin tones or a subdivision flag
  is about 14–15, each counting as 1 character. OpenAPI and AsyncAPI `maxLength` values match
  this guard (JSON Schema counts code points, which are never more than UTF-16 code units),
  and the real limit is in each field's description.
- **Request size**: Kestrel `MaxRequestBodySize` is 1 MB on every API. The largest valid body
  is a task with a 5,000-character description at the guard limit: 80,000 UTF-16 code units,
  at most about 240 KB in UTF-8, or more if the client escapes characters as `\uXXXX` in JSON.
  Larger bodies get `413`, which is audited.
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
  - **Key matrix** (which caller keys each service accepts on which routes). A missing key, or a
    key that is not one of the service's configured callers, gets `401`. A configured caller's
    key on a route the matrix does not allow gets `403` (the `CallerNotAllowed` response in the
    OpenAPI contracts):

    | Service ← caller | Web key | Projects key | Tasks key | Notifications key |
    |---|---|---|---|---|
    | Projects API | ✅ all routes | — | ✅ read routes only | ✅ `GET /api/users*` only |
    | Tasks API | ✅ all routes | — | — | — |
    | Notifications API `/api/*` and `/hubs/board` | ✅ | — | — | — |
    | Notifications API `/internal/events` | ❌ `403` | ✅ `ProjectCreated` only | ✅ task and comment events only | — |

    AppHost secret parameters: `web-api-key`, `projects-api-key`, `tasks-api-key`,
    `notifications-api-key`, plus `dataprotection-cert` and `dataprotection-cert-password`
    (Web only). The certificate encrypts the Web app's Data Protection key ring at rest; that
    key ring protects the selected-user cookie. Each resource gets only its own key, plus the keys of the callers
    it accepts.
  - **Exempt from the acting-user header**: `GET /api/users` and `GET /api/users/{userId}`.
    They are needed before anyone is selected (the user selection screen) and by services
    building their user directory. The same applies to `/internal/events` (the actor is in the
    envelope) and `/health` and `/alive` (which also need no key).
  - **Source IP (FR-032, audit)**: the Web server reads the end user's IP when the circuit
    starts (`HttpContext.Connection.RemoteIpAddress` via `IHttpContextAccessor` during the
    initial request; there is no reverse proxy in phase 1). It forwards the IP to the APIs as
    `X-Taskify-Client-Ip`. APIs trust this header only when the caller is the Web key, ignore
    it otherwise, and record it in audit events.
  - **User selection audit (FR-032)**: the Web server writes a `UserSelected` audit event (chosen
    user, previous user or null, time, source IP, correlation ID) on every selection and
    switch. This includes a selection restored from the cookie on a new circuit, which is
    logged as `UserSelectionRestored`.
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
- **Rate limits (FR-031)**: built-in `SlidingWindowRateLimiter` (1-minute window, 6 segments,
  no queueing):

  | Policy | Key | Limit | Applies to |
  |---|---|---|---|
  | `writes` | acting user | 60 per minute | every POST, PUT and DELETE under `/api/*`, including mark-read |
  | `reads` | acting user | 300 per minute | every GET under `/api/*` |
  | `web-ip` | client IP | 1,200 per minute | Web app HTTP requests and new circuits (outer guard only) |
  | `internal-events` | calling service | 3,000 per minute | `/internal/events` |

  Writes and reads are enforced in the API that owns the data, so they apply even if the Web app
  is bypassed. A rejection returns `429` Problem Details with `Retry-After`, and changes nothing.
  The Web app shows "Too many requests, please wait a moment", and the rejection is audited
  (R13). The user directory requests that services make for themselves have no acting user and
  are counted under the calling service's key instead (a `reads` partition per caller).
  Distributed limiting is not needed: each API runs a single instance in phase 1, which is
  recorded in the README so that a later scale-out adds a shared store.
- **Rationale**: Covers FR-020, SC-006, and the constitution's Security Requirements.

## R10. Notifications (spec User Story 6, FR-027–FR-030)

- **Decision**: In-app notifications only. A notification is created for a user when another
  user:
  - assigns a task to them, including assigning it when the task is created (a `TaskCreated`
    event with an assignee produces a `TaskAssigned` notification);
  - moves a task assigned to them;
  - comments on a task assigned to them.
  Users never get notifications for their own actions (FR-027). Notifications can be listed,
  counted as unread, marked read singly, or all marked read (FR-028). A user can only see and
  change their own notifications. Someone else's notification ID returns `404`, so its
  existence is not revealed (FR-029). Notifications are deleted 30 days after creation by a
  daily job (FR-030).
- **Rationale**: The planning input asked for a notifications REST API, and the spec has
  included it since the 2026-10-04 clarification. These triggers are the smallest useful set
  based on the clarified card behavior.
- **Limitation (D1)**: "only your own notifications" is enforced against the acting user, which
  phase 1 cannot prove (anyone can select any user). This is covered by accepted risk D1 and
  becomes a real guarantee in phase 2 with sign-in.
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
  - **Required test sets added by the revision**:
    - Hub argument validation, accepted and rejected (Principle II).
    - Grapheme-count boundaries per field: a title of exactly 200 emoji or accented letters is
      accepted, 201 is rejected, and a Zalgo string above the guard is rejected.
    - Rate limits: the 61st write and the 301st read are rejected with no data change, and a
      sustained 1 write per second is accepted (SC-010).
    - Message payloads against the AsyncAPI schemas (R15).
    - TLS checks (R16).
    - Audit events for user selection with source IP (FR-032).
    - A last-save-wins edit race (R14).
    - Notification isolation between users (FR-029).
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
  - CI also runs `npx @asyncapi/cli validate contracts/events.asyncapi.yaml` (R15). It uses the
    feature's `specs/001-taskify-kanban-board/contracts/` folder directly; there is no copy, so
    there is a single source of truth.
- **Rationale**: Enforces Principle IV and the quality gates automatically instead of relying on
  review alone.

## R13. Audit logging

- **Decision**: Structured logs through the Aspire ServiceDefaults OpenTelemetry pipeline. Each
  data change and each rejected request logs an audit event with acting user ID, action, entity
  type and ID, outcome, correlation ID, calling service, and end-user source IP (from
  `X-Taskify-Client-Ip`, R8). Titles, descriptions and comment text are never logged.
- **Audited actions**:
  - Every data change.
  - Every rejection: `400`, `401`, `403`, `404` on a write, `409`, `413`, `422`, `429`.
  - Every user selection and switch (`UserSelected` and `UserSelectionRestored`, FR-032).
  - Every hub connection refused for a bad key.
- **Abnormal rejection rates (constitution Security Requirements)**: each service also emits an
  OpenTelemetry counter `taskify.rejections`, tagged with the reason (validation, auth, rate
  limit). The Aspire dashboard shows it in development. A deployment alert fires when there are
  more than 50 rejections in 5 minutes for one source IP or calling service. The threshold is
  set in configuration, not code. The rule lives in `deploy/alerts/rejections.yaml`.
- **Dropped events**: an outbox event the receiver rejects permanently (`400` or `403`) is
  dead-lettered (`deadLetteredAt`), audited as `EventDeadLettered` (event ID and type only) and
  counted in `taskify.outbox.deadlettered`, which has its own alert in the same file. A dropped
  event means a live update or notification was missed, so it must never be silent.
- **Rationale**: FR-022, FR-032 and the constitution's audit logging requirement.

## R14. Concurrent edits (spec FR-011, clarification 2026-10-04)

- **Decision**: The last save wins for task edits, the same as for moves. `PUT /api/tasks/{id}`
  replaces the title, description and assignee as one unit. Moves change only the status.
  There is no concurrency token or ETag. The overwritten user sees the final values within
  2 seconds through the live update (FR-025), so the overwrite is visible, not silent. Each
  write runs in one database transaction with its outbox row and, for moves, its history row,
  so a half-applied change is never visible.
- **Rationale**: This was chosen in the clarification session. It keeps one rule for every
  change and needs no conflict screens for a five-person team.
- **Alternatives considered**: Optimistic concurrency with `409` and a reload, which was
  rejected in clarification. Field-level merge was also rejected: it is more complex, and a
  `PUT` that replaces all fields would have to become a `PATCH`.

## R15. Machine-readable message contracts (constitution Principle IV)

- **Decision**:
  - Every message between services is defined in
    [contracts/events.asyncapi.yaml](contracts/events.asyncapi.yaml) (AsyncAPI 3.1). It covers
    the 8 domain events on the `/internal/events` channel and the 4 hub messages on
    `/hubs/board`, with JSON Schema payloads (`additionalProperties: false`).
  - `events.md` and `realtime-hub.md` stay as readable guides. They point to the AsyncAPI file,
    which wins if they differ.
  - CI runs `npx @asyncapi/cli validate` on the file.
  - Contract tests serialize every C# event record and hub payload and validate them against the
    schemas in the AsyncAPI file (JsonSchema.Net). The receiver's FluentValidation rules are
    tested against the same samples, so publisher, document and consumer cannot drift apart.
- **Rationale**: Principle IV requires machine-readable contracts. The OpenAPI file alone
  described `payload` only as `object`.
- **Alternatives considered**: JSON Schema files without AsyncAPI (they would not describe
  channels, security or the hub). Protobuf (it does not fit JSON over HTTP and SignalR, and
  would add a code-generation step).

## R16. Encryption in transit everywhere (constitution Principle I)

- **Decision**:
  - **Between services**: every service URI is `https://<resource>`. The `https+http://` form
    is never used, because it falls back to plain HTTP. APIs define only HTTPS endpoints in the
    AppHost, so no HTTP listener exists to fall back to. Locally they use the trusted ASP.NET
    Core dev certificate. When deployed, they use certificates from the hosting platform
    (internal HTTPS ingress).
  - **Hub**: the SignalR client connects to `https://notifications-api/hubs/board` over WSS.
  - **Database**: Npgsql connections use `SSL Mode=Require` in development and
    `SSL Mode=VerifyFull` in deployment, against a managed PostgreSQL that serves TLS. For the
    local container, the AppHost generates a self-signed server certificate at startup and
    places it into the container with `WithContainerFiles`, owned by `postgres` with mode
    `0600`. It then starts PostgreSQL with `-c ssl=on`. No key material is stored in the
    repository. `Require` encrypts traffic but does not verify the certificate, which is
    acceptable for a container on the local Aspire network.
  - **Browser**: HTTPS with HSTS (R9).
- **Verification**: an integration test checks that each API refuses a plain-HTTP connection,
  and that a database session reports `ssl = on` (`SELECT ssl FROM pg_stat_ssl WHERE pid =
  pg_backend_pid()`).
- **Fallback if the container TLS setup proves unworkable on a developer OS**: record a
  development-only deviation (local container on the private Aspire network, no real data) in
  plan Complexity Tracking. Deployment keeps `VerifyFull`. This needs reviewer approval.
- **Alternatives considered**: `https+http://` (rejected because of the fallback); service-mesh
  mTLS (too much infrastructure for phase 1; it may replace API keys together with D2 in
  phase 2).
