# Real-time Hub Contract (v1)

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

Arguments are validated; invalid IDs raise a `HubException` with a generic message.
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

## Reliability

- The Web server's hub client reconnects automatically. On reconnect it rejoins its groups
  and re-fetches the boards that circuits are viewing, so any missed changes are picked up.
- No message is required for correctness: refreshing the page always shows saved state
  (FR-018).
