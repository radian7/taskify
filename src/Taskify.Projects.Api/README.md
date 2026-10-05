# Taskify.Projects.Api

Owns **projects** and the **read-only directory of the five predefined users** (research R2). It has its own
PostgreSQL database (`projectsdb`) and is internal to the Aspire network; only the Web app is reachable from outside.
Contract: `specs/001-taskify-kanban-board/contracts/projects-api.yaml`. Design background:
[ADR 0001](../../docs/adr/0001-service-decomposition.md), [ADR 0003](../../docs/adr/0003-phase1-identity-and-api-keys.md).

## Endpoints and accepted callers (R8 key matrix)

| Route | Accepted callers | Notes |
|---|---|---|
| `GET /api/projects` | Web | read limit |
| `GET /api/projects/{projectId}` | Web, Tasks | Tasks uses it to check a project exists before creating a task |
| `POST /api/projects` | Web | validated (`CreateProjectRequest`), write limit, emits `ProjectCreated` |
| `GET /api/users`, `GET /api/users/{userId}` | Web, Tasks, Notifications | the user directory |
| OpenAPI document (`MapOpenApi`) | Web | |
| `/health`, `/alive` | none required | health checks from `ServiceDefaults` |

A missing or unknown `X-Api-Key` gets `401`. A configured caller on a route it may not use gets `403`.
Every `/api` route needs `X-Taskify-User` (a predefined user's UUID) **except** the user directory routes and
OpenAPI, which are exempt (`AllowAnonymousActingUser`), because the Web app needs the list before anyone is selected.

## Run and test

```powershell
./scripts/verify.ps1 -Tests unit,web                                # build + fast tests
./scripts/verify.ps1 -NoBuild -Tests unit -Class '*CreateProjectValidatorTests'
./scripts/verify.ps1 -Tests integration -Class '*ProjectsReadContractTests'
```

Run the whole system through `src/Taskify.AppHost` (it starts PostgreSQL and every service over HTTPS).
Integration tests need Podman (see the root `CLAUDE.md`). Other test classes for this API:
`grep -rl Project tests/`.

## Configuration

| Setting | Meaning |
|---|---|
| `ConnectionStrings__projectsdb` | Administrator connection (TLS), used only for migrations in Development |
| `Taskify__Database__AppRole`, `Taskify__Database__AppPassword` | The least-privilege role `projects_app` the service runs as |
| `ApiKeys__OwnKey` | This service's key (used when it calls others) |
| `ApiKeys__Accepted__web`, `__tasks`, `__notifications` | The keys of the allowed callers |

The AppHost passes these from secret parameters. Create the dev secrets with `scripts/init-dev-secrets.ps1`; never put
secrets in code or config files. In Development the service migrates as administrator at startup, then
(re)creates `projects_app`: it may read and write its tables, is **read-only on `users`**, and has no access to
`__EFMigrationsHistory` (`DatabaseRoles`, [ADR 0006](../../docs/adr/0006-per-service-database-roles.md)).

## Rate limits (FR-031)

60 writes/min and 300 reads/min per acting user (per calling service when there is none). Counters live in this
service's memory, so limits are **per service and per instance**; phase 1 runs one instance
([ADR 0007](../../docs/adr/0007-per-service-rate-limits.md)).

## Dependencies

PostgreSQL only. It calls no other service; the Tasks and Notifications APIs and the Web app call it.

## Emitted events

`ProjectCreated` (`ProjectId`, `Name`) is written to the transactional outbox in the same transaction as the project
and dispatched over HTTPS to the Notifications API `/internal/events` by the outbox dispatcher
([ADR 0002](../../docs/adr/0002-outbox-http-dispatch.md)). The receiving intake arrives with US5.
