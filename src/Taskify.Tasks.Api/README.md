# Taskify.Tasks.Api

Owns **tasks, status-change history and comments** (one aggregate: a task owns its history and comments; research R2)
in its own database (`tasksdb`). Reachable **only from the Web app**. Contract:
`specs/001-taskify-kanban-board/contracts/tasks-api.yaml`.

## Endpoints and accepted callers

Every route accepts the **Web** key only and requires `X-Taskify-User`. The OpenAPI document is also Web-only.

| Route | Purpose |
|---|---|
| `GET /api/tasks?projectId=` | List a project's tasks |
| `GET /api/tasks/{taskId}` | One task |
| `POST /api/tasks` | Create a task (checks the project exists, see Dependencies) |
| `PUT /api/tasks/{taskId}` | Replace title, description and assignee as one unit |
| `POST /api/tasks/{taskId}/moves` | Move to another status; writes a history row |
| `GET /api/tasks/{taskId}/history` | Status change history |
| `GET /api/tasks/{taskId}/comments` | List comments |
| `POST /api/tasks/{taskId}/comments` | Add a comment |
| `PUT /api/tasks/{taskId}/comments/{commentId}` | Edit own comment |
| `DELETE /api/tasks/{taskId}/comments/{commentId}` | Delete own comment (soft delete) |

Writes are validated (`RequireValidation`), rate limited and audited. A missing or unknown key gets `401`; a
configured caller other than Web gets `403` (only the Web key is configured here).

## Run and test

```powershell
./scripts/verify.ps1 -Tests unit,web
./scripts/verify.ps1 -NoBuild -Tests unit -Class '*CommentTextValidatorTests'
./scripts/verify.ps1 -Tests integration -Class '*CommentsContractTests'
```

Integration classes share one seeded user and the 60 writes/min limit, so a `429` in `MoveContractTests` or
`CreateEditContractTests` is the known flakiness; re-run the class on its own.

## Configuration and database role

`ConnectionStrings__tasksdb` (administrator, TLS, migrations in Development), `Taskify__Database__AppRole` /
`AppPassword` (role `tasks_app`), `ApiKeys__OwnKey`, `ApiKeys__Accepted__web`. Secrets come from
`scripts/init-dev-secrets.ps1` via the AppHost. See [ADR 0006](../../docs/adr/0006-per-service-database-roles.md).

**`status_changes` is append-only for the running service.** `DatabaseRoles` (T081) revokes `UPDATE` and `DELETE` on
`status_changes` from `tasks_app`, so history can be added but never altered or removed, even by a bug in this service.

## Dependency on the Projects API

- **Project check**: `ProjectsApiClient` calls `GET /api/projects/{id}` over HTTPS with the Tasks key, so a task is
  never created in a missing project (this service never reads the Projects database).
- **User directory**: `AddRemoteUserDirectory` reads `GET /api/users` from the Projects API and caches it, to validate
  the acting user and assignees.

## Rules

- **Last save wins (R14)**: no ETag or concurrency token. `PUT` replaces the editable fields; a move changes only the
  status. Each write, its outbox row and (for moves) its history row commit in one transaction.
- **Comment ownership (FR-017)**: only the author may edit or delete a comment; anyone else gets `403`. This is checked
  on the server; hiding the buttons in the UI is not the control. Deleting is a soft delete (`DeletedAt`): the comment
  stays as a placeholder, and editing or deleting an already deleted comment gets `409`.

## Emitted events

`TaskCreated`, `TaskUpdated`, `TaskAssigned`, `TaskMoved`, `CommentAdded`, `CommentEdited`, `CommentDeleted`, written to
the outbox and dispatched to `POST /internal/events` on the Notifications API ([ADR 0002](../../docs/adr/0002-outbox-http-dispatch.md)),
which accepts all seven from this service and turns them into `BoardChanged` and `TaskChanged` signals for the open
boards and task pages ([ADR 0008](../../docs/adr/0008-realtime-signals-and-refetch.md)). If the Notifications API is
down, writes still succeed (the outbox row commits with the change), delivery is retried and open screens re-fetch
after the Web app reconnects (FR-026).
Payloads carry IDs and status values and never the task description or comment text. Logs and audit entries never
contain titles, descriptions or comment text either.
