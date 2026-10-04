# Data Model: Taskify Kanban Board

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-04

Each service owns its own database (see [research.md](research.md) R3). Data owned by another
service is referenced by ID only, with no foreign key across databases. All IDs are UUIDs. All
times are stored in UTC (`timestamptz`). Strings are trimmed before validation and storage.

**Text lengths** ("chars" below) are counted in user-perceived characters (grapheme clusters,
spec FR-019, research R7). Every text field is also rejected above 16 UTF-16 code units per
allowed character (an abuse guard). Text columns are PostgreSQL `text`, so the database does
not enforce a length; the API validators do.

**Concurrent writes**: the last save wins for task edits and moves (spec FR-011, research R14).
No entity has a concurrency token.

## Projects service (`projectsdb`)

### User (read-only, seeded)

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Fixed seed value |
| `displayName` | text | 1–100 chars, unique |
| `role` | enum `ProductManager` \| `Engineer` | Exactly one `ProductManager`, four `Engineer` |

- No create, update, or delete operations exist (FR-003).
- Seed: five users (see [quickstart.md](quickstart.md) for the sample names).

### Project

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Generated |
| `name` | text | Required; 1–100 chars after trim (FR-006); duplicates allowed |
| `description` | text, nullable | 0–1,000 chars after trim; empty string stored as null |
| `createdByUserId` | UUID | Must be a seeded user (FR-004) |
| `createdAt` | timestamptz | Set by the server |

- No update or delete in phase 1 (spec assumption).
- Seed: three sample projects with fixed IDs (FR-005).
- List order: `createdAt` descending.

### OutboxMessage (in every service that publishes events)

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Generated; also the event ID used for de-duplication |
| `type` | text | Event type name (see [contracts/events.md](contracts/events.md)) |
| `payload` | jsonb | Event body |
| `occurredAt` | timestamptz | Same transaction as the change |
| `dispatchedAt` | timestamptz, nullable | Set after delivery succeeds |
| `attempts` | int | Retry count; backoff capped at 1 minute |

## Tasks service (`tasksdb`)

### Task

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Generated |
| `projectId` | UUID | Must exist in the Projects API (checked on create) |
| `title` | text | Required; 1–200 chars after trim (FR-009) |
| `description` | text, nullable | 0–5,000 chars after trim |
| `status` | enum `ToDo` \| `InProgress` \| `InReview` \| `Done` | New tasks start as `ToDo` |
| `assigneeUserId` | UUID, nullable | Null = unassigned; otherwise a seeded user (FR-010) |
| `createdByUserId` | UUID | Acting user |
| `createdAt` | timestamptz | Set by the server |
| `updatedAt` | timestamptz | Updated on any change, including moves |

- Index: (`projectId`, `status`, `createdAt` desc) for board loading.
- Board order within a column: `createdAt` descending (spec assumption).
- No delete in phase 1.

### StatusChange (read-only after insert)

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Generated |
| `taskId` | UUID | FK → Task |
| `fromStatus` | status enum | Must differ from `toStatus` |
| `toStatus` | status enum | |
| `movedByUserId` | UUID | Acting user |
| `movedAt` | timestamptz | Set by the server |

- Inserted in the same transaction as the task's status change (FR-023).
- No update or delete endpoint; the database user for the Tasks API has no `UPDATE` or `DELETE`
  permission on this table.
- Listed newest first.

### Comment

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Generated |
| `taskId` | UUID | FK → Task |
| `authorUserId` | UUID | Acting user at creation; never changes |
| `text` | text, nullable | 1–2,000 chars after trim (FR-015); set to null on delete (FR-024) |
| `createdAt` | timestamptz | Set by the server |
| `editedAt` | timestamptz, nullable | Set on each edit (FR-016) |
| `deletedAt` | timestamptz, nullable | Set on delete |

- Listed oldest first (FR-016).
- Edit and delete are allowed only when acting user = `authorUserId` (FR-017), otherwise `403`.
- Comments are allowed on tasks in any column, including Done (clarification Q5).

### Comment lifecycle

```text
          post                 edit (author only)
 (none) ───────► Active ◄────────────────┐
                   │ └───────────────────┘
                   │ delete (author only)
                   ▼
                Deleted   (text = null, deletedAt set; no further edit/delete/restore → 409)
```

### Task status transitions

Any status can move to any other status, and any user can make the move (FR-012, clarification
Q1). A move to the current status is a no-op: nothing is saved, no history entry is written and
no event is sent.

```text
ToDo ⇄ InProgress ⇄ InReview ⇄ Done   (plus every other pair: fully connected)
```

## Notifications service (`notificationsdb`)

### Notification

| Field | Type | Rules |
|-------|------|-------|
| `id` | UUID | Generated |
| `recipientUserId` | UUID | A seeded user, never the acting user |
| `type` | enum `TaskAssigned` \| `TaskMoved` \| `TaskCommented` | See trigger rules |
| `taskId` | UUID | Task that triggered it |
| `projectId` | UUID | For linking to the board |
| `actorUserId` | UUID | Who made the change |
| `summary` | text | Generated by the server, max 300 chars, e.g. "Ana moved 'Login page' to In Review" |
| `createdAt` | timestamptz | |
| `readAt` | timestamptz, nullable | Null = unread |
| `sourceEventId` | UUID, unique | Event ID; makes event delivery idempotent |

- Retention: deleted 30 days after `createdAt` by a daily cleanup job (FR-030).
- Visibility: read and changed only by the recipient. Any other user gets `404` (FR-029).
- List order: `createdAt` descending.

### ProcessedEvent (de-duplication of incoming events)

| Field | Type | Rules |
|-------|------|-------|
| `eventId` | UUID, primary key | `eventId` from the envelope |
| `receivedAt` | timestamptz | Set by the server |

- Inserted in the same transaction as any notifications the event creates. A repeated
  `eventId` is accepted (`202`) and ignored, even for events that create no notification.
- Deleted 30 days after `receivedAt` by the same daily job as notifications. The outbox stops
  retrying long before then.

### Trigger rules (from events, spec FR-027)

| Event | Recipient | Condition |
|-------|-----------|-----------|
| `TaskCreated` (with an assignee) | assignee, as a `TaskAssigned` notification | assignee ≠ actor |
| `TaskAssigned` | new assignee | assignee ≠ actor |
| `TaskMoved` | current assignee | assignee exists and ≠ actor |
| `CommentAdded` | current assignee | assignee exists and ≠ actor |

The other events (`TaskCreated` without an assignee or assigned to the actor, `TaskUpdated`,
`CommentEdited`, `CommentDeleted`, `ProjectCreated`) create no notifications. They are only
broadcast for real-time updates (spec FR-025).

## Cross-service references

| Reference | Owner of target | Check |
|-----------|-----------------|-------|
| Task.projectId | Projects | `GET /api/projects/{id}` on task create; `404` → reject with `422` |
| Task.assigneeUserId, *UserId | Projects | Cached user list from `GET /api/users` |
| Notification.taskId/projectId | Tasks/Projects | Taken from the event payload after it is validated against its AsyncAPI schema and the caller policy. Internal origin does not imply trust (Principle II) |
| Notification.recipientUserId, actorUserId | Projects | Must be in the cached user list; otherwise the event is rejected with `400` |

## Audit event (log record, not a table)

Written through the OpenTelemetry log pipeline (research R13), never to a service database.

| Field | Notes |
|-------|-------|
| `timestamp` | UTC |
| `action` | For example `TaskMoved`, `CommentDeleted`, `UserSelected`, `UserSelectionRestored`, `RequestRejected` |
| `actingUserId` | Null when there is none (for example a rejected request with an unknown user) |
| `previousUserId` | `UserSelected` only |
| `entityType`, `entityId` | When the action targets an entity |
| `outcome` | `Succeeded` or a rejection reason (`Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `TooLarge`, `UnknownReference`, `RateLimited`) |
| `callerService` | From the API key |
| `sourceIp` | End-user IP, from `X-Taskify-Client-Ip` when the caller is Web (research R8) |
| `correlationId` | Trace ID |

Never logged: titles, names typed by users, descriptions, comment text, API keys, cookie values.
