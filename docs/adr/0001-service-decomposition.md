# ADR 0001: Service decomposition

- Status: accepted (implemented)
- Source: research R2, R3

## Context

Taskify needs REST surfaces for projects, tasks and notifications, plus a Blazor UI. The
constitution (Principle III) requires one capability and one database per service and discourages
services that exist only for organisation.

## Decision

Three backend services and a web frontend:

1. **Projects API**: projects and the read-only user directory (five predefined users).
2. **Tasks API**: tasks, status history and comments. A task owns its history and comments, so they
   are one aggregate.
3. **Notifications API**: per-user notifications and the real-time `BoardHub`.
4. **Web**: Blazor Server UI, the only externally reachable resource.

One PostgreSQL server hosts three databases (`projectsdb`, `tasksdb`, `notificationsdb`). Each service
connects only to its own database.

## Alternatives considered

- A separate Users service: rejected. Five fixed records with no behaviour would be an
  organisational-only service.
- A separate Comments service: rejected. Comments are always read and authorised together with a
  task, so splitting them adds cross-service calls with no scaling benefit.
- A single monolith: rejected by Principle III.
- One shared database with a schema per service: weaker isolation and invites cross-schema joins.

## Consequences

- Data ownership is enforced by the database boundary. Cross-service data goes through HTTP calls
  (for example Tasks calls Projects to check a project or user) or events (ADR 0002).
- The user list is cached in memory in the callers because it never changes in phase 1.
- Three services mean three sets of migrations, keys, rate limits and READMEs to keep in sync.
