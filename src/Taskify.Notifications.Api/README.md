# Taskify.Notifications.Api

Will own **per-user in-app notifications** and the **real-time board hub** (SignalR `BoardHub`), plus the internal
event intake that feeds them (research R2, R5, R10). **Current state: foundation only.** The host, security pipeline,
database context and user-directory client exist; there are no endpoints, entities, hub or event handlers yet.
`Domain/`, `Endpoints/`, `Hubs/`, `Validation/` and `Clients/` are empty until US5/US6. Contract:
`specs/001-taskify-kanban-board/contracts/notifications-api.yaml`.

## What exists today

- `Program.cs`: service defaults, `AddTaskifySecurity` (API keys, acting user, audit, rate limits, Problem Details),
  `NotificationsDbContext` (`notificationsdb`, no tables yet), `AddRemoteUserDirectory`, FluentValidation, OpenAPI
  and `AddSignalR()` (registered, no hub mapped yet).
- The OpenAPI document (Web key only) and the health endpoints.

## Accepted callers

| Route | Accepted keys |
|---|---|
| `/api/*` and `/hubs/board` (added by US5/US6) | Web |
| `/internal/events` (added by US5/US6) | Projects (`ProjectCreated` only), Tasks (task and comment events only); the Web key gets `403` |

The AppHost configures `ApiKeys__Accepted__web`, `__projects` and `__tasks` for this service (R8 matrix).

## Configuration and database role

`ConnectionStrings__notificationsdb` (administrator, TLS, migrations in Development),
`Taskify__Database__AppRole` / `AppPassword` (role `notifications_app`), `ApiKeys__OwnKey`, `ApiKeys__Accepted__*`.
Secrets come from `scripts/init-dev-secrets.ps1`. The role gets read/write on its own tables and nothing on
`__EFMigrationsHistory` ([ADR 0006](../../docs/adr/0006-per-service-database-roles.md)). No table is append-only or
read-only yet.

## User-directory client

`AddRemoteUserDirectory` reads `GET /api/users` from the Projects API over HTTPS with this service's own key and caches
the result, to validate the acting user and notification recipients.

## Event intake (added by US5/US6)

To be filled in by T152: `/internal/events`, idempotency, the per-service rate limit, dead-lettering of rejected events.

## Board hub (added by US5/US6)

To be filled in by T153: `/hubs/board`, groups, key check on connect, the 2-second live update (FR-025).

## Notifications (added by US5/US6)

To be filled in by T152: the notification model, endpoints, read state and ownership rules.

## Run and test

```powershell
./scripts/verify.ps1 -Tests unit,web
./scripts/verify.ps1 -Tests integration -Class '<class>'   # classes appear with US5/US6
```
