# ADR 0006: Per-service database roles

- Status: accepted (implemented)
- Source: task T038 "Implemented as"; code in `Taskify.Security/Data/DatabaseRoles.cs`; test `DatabaseRoleTests`

## Context

Principle I requires least privilege. If every service used the PostgreSQL administrator login, a
compromised service could read or change other databases and rewrite history.

## Decision

- Each service migrates its schema as the administrator, then creates (or updates) its own
  login role and runs as that role. The roles are `projects_app`, `tasks_app` and `notifications_app`.
  Passwords come from the secret parameters `projects-db-password`, `tasks-db-password` and
  `notifications-db-password` (environment `Taskify__Database__AppRole` and `AppPassword`).
  `scripts/init-dev-secrets.ps1` creates them. Passwords must be alphanumeric and long enough,
  because the role is created with generated SQL.
- The role is `NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION`, gets `CONNECT` on its own database
  only (`REVOKE ALL ... FROM PUBLIC`) and `SELECT, INSERT, UPDATE, DELETE` on the tables in `public`.
- `__EFMigrationsHistory` is revoked from the role.
- Per-service tightening through `DatabaseRolePlan`: `users` in Projects is read-only (`REVOKE INSERT,
  UPDATE, DELETE`), and `status_changes` in Tasks is append-only (`REVOKE UPDATE, DELETE`), so the
  service cannot alter the move history.
- The statements are conditional on the table existing, so they are safe to re-run at every start.

## Alternatives considered

- One administrator login for all services: no isolation.
- A separate migration job and roles created out of band: more moving parts for phase 1.
- Application-level checks only: no protection against a compromised or buggy service.

## Consequences

- Startup needs the administrator credentials as well as the role password.
- Rotating a password takes effect at the next start (`ALTER ROLE`).
- A new table that needs different privileges must be added to the service `DatabaseRolePlan`.
