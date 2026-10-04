# Event Contracts (v1)

Domain events published by the Projects and Tasks APIs through their outbox tables and delivered
to `POST /internal/events` on the Notifications API (see [notifications-api.yaml](notifications-api.yaml)).
The event types are defined once in `src/Taskify.Contracts` and shared by publishers and the consumer.

## Envelope

| Field | Type | Notes |
|-------|------|-------|
| `eventId` | UUID | Outbox message ID; the consumer de-duplicates on this |
| `type` | string | One of the types below |
| `version` | int | `1`; a breaking change creates a new version (constitution, Principle III) |
| `occurredAt` | date-time (UTC) | Time of the change |
| `actorUserId` | UUID | Acting user |
| `payload` | object | Type-specific, below |

Payloads contain IDs, column values, and titles. Titles are needed for notification summaries
and board cards. Descriptions and comment text are **never** included.

## Types

| Type | Publisher | Payload fields | Notification | Board broadcast |
|------|-----------|----------------|--------------|-----------------|
| `ProjectCreated` | Projects | `projectId`, `name` | — | all clients (`projects`) |
| `TaskCreated` | Tasks | `taskId`, `projectId`, `title`, `status`, `assigneeUserId?` | `TaskAssigned` if assignee ≠ actor | `project:{projectId}` |
| `TaskUpdated` | Tasks | `taskId`, `projectId`, `title` | — | `project:{projectId}` |
| `TaskAssigned` | Tasks | `taskId`, `projectId`, `title`, `previousAssigneeUserId?`, `assigneeUserId?` | to new assignee if ≠ actor | `project:{projectId}` |
| `TaskMoved` | Tasks | `taskId`, `projectId`, `title`, `fromStatus`, `toStatus`, `assigneeUserId?` | to assignee if ≠ actor | `project:{projectId}` |
| `CommentAdded` | Tasks | `taskId`, `projectId`, `title`, `commentId`, `assigneeUserId?` | `TaskCommented` to assignee if ≠ actor | `project:{projectId}` (count) |
| `CommentEdited` | Tasks | `taskId`, `projectId`, `commentId` | — | `task:{taskId}` |
| `CommentDeleted` | Tasks | `taskId`, `projectId`, `commentId` | — | `project:{projectId}` (count) |

An update that changes both the text fields and the assignee emits `TaskUpdated` and
`TaskAssigned`. A no-op move emits nothing.

## Validation and authorization at the consumer

- The envelope and payload are validated against the schema for their type. Unknown types,
  unknown fields, or an unsupported `version` → `400`.
- Only the Projects API key may send `ProjectCreated`; only the Tasks API key may send the
  other types → otherwise `403`.
- Delivery is at-least-once. A repeated `eventId` is accepted and ignored.
