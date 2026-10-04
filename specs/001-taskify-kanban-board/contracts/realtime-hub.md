# Real-time Hub Contract (v1)

> **Authoritative schema**: the `boardHub` channel in [events.asyncapi.yaml](events.asyncapi.yaml).
> This page is a readable summary. The hub is served over HTTPS/WSS only. It implements spec
> FR-025 and FR-026 (live updates within 2 seconds, catch-up after interruption) and delivers
> FR-027 notifications to the recipient's open screens.

SignalR hub hosted by the Notifications API at `/hubs/board`. **Only the Web server connects**,
authenticated with its `X-Api-Key` (sent as a header in the connection request). Browsers
never connect to it; they get updates through their Blazor Server circuit.

## Client → server methods

| Method | Arguments | Effect |
|--------|-----------|--------|
| `JoinProject` | `projectId: Guid` | Add connection to group `project:{projectId}` |
| `LeaveProject` | `projectId: Guid` | Remove from that group |
| `JoinTask` | `taskId: Guid` | Add to `task:{taskId}` (open task details) |
| `LeaveTask` | `taskId: Guid` | Remove from that group |
| `JoinUser` | `userId: Guid` | Add to `user:{userId}`; must be a predefined user |
| `LeaveUser` | `userId: Guid` | Remove from that group |

Arguments are validated; invalid IDs raise a `HubException` with a generic message. These checks
are validation rules under constitution Principle II and need automated tests for accepted and
rejected arguments (malformed GUID, empty GUID, unknown user for `JoinUser`).
The connection also joins group `projects` automatically.

## Server → client messages

| Message | Payload | Sent to |
|---------|---------|---------|
| `BoardChanged` | `{ eventId, type, projectId, taskId?, actorUserId, occurredAt }` | `project:{projectId}` |
| `TaskChanged` | `{ eventId, type, taskId, actorUserId, occurredAt }` | `task:{taskId}` |
| `ProjectListChanged` | `{ eventId, projectId }` | `projects` |
| `NotificationCreated` | `Notification` (see notifications-api.yaml) | `user:{recipientUserId}` |

Messages are **change signals**: the Web server re-fetches the affected data from the REST APIs
instead of trusting message content for display. This keeps validation and authorization in
the owning service. The Web server fans out each message to every circuit viewing the affected
board, task, or user. Clients ignore an `eventId` they have already handled.

Re-fetches triggered by signals are coalesced to at most one per second per open screen, so a
busy board stays well inside each viewer's read limit (spec FR-031: 300 reads per minute) while
still meeting the 2-second target.

## Reliability

- The Web server's hub client reconnects automatically. On reconnect it rejoins its groups
  and re-fetches the boards that circuits are viewing, so any missed changes are picked up.
- No message is required for correctness: refreshing the page always shows saved state
  (FR-018).
