# Taskify.Notifications.Api

Owns **per-user in-app notifications** and the **real-time board hub** (SignalR `BoardHub`), plus the internal
event intake that feeds them (research R2, R5, R10). **Current state: US5 and US6 done** (event intake, board hub,
notifications). Contracts: `specs/001-taskify-kanban-board/contracts/notifications-api.yaml`
and `realtime-hub.md`. Design background: [ADR 0008](../../docs/adr/0008-realtime-signals-and-refetch.md).

## What exists today

- `Program.cs`: service defaults, `AddTaskifySecurity` (API keys, acting user, audit, rate limits, Problem Details),
  `NotificationsDbContext` (`notificationsdb`, table `ProcessedEvents`), `AddRemoteUserDirectory`, FluentValidation,
  OpenAPI, SignalR, `POST /internal/events` and `BoardHub` at `/hubs/board`.
- The OpenAPI document (Web key only) and the health endpoints.

## Accepted callers

| Route | Accepted keys |
|---|---|
| `/api/*` and `/hubs/board` | Web |
| `/internal/events` | Projects (`ProjectCreated` only), Tasks (the other seven event types); the Web key gets `403` |

The AppHost configures `ApiKeys__Accepted__web`, `__projects` and `__tasks` for this service (R8 matrix).

## Configuration and database role

`ConnectionStrings__notificationsdb` (administrator, TLS, migrations in Development),
`Taskify__Database__AppRole` / `AppPassword` (role `notifications_app`), `ApiKeys__OwnKey`, `ApiKeys__Accepted__*`.
Secrets come from `scripts/init-dev-secrets.ps1`. The role gets read/write on its own tables and nothing on
`__EFMigrationsHistory` ([ADR 0006](../../docs/adr/0006-per-service-database-roles.md)). `Notifications` and `ProcessedEvents` are read/write.

## User-directory client

`AddRemoteUserDirectory` reads `GET /api/users` from the Projects API over HTTPS with this service's own key and caches
the result, to validate the acting user and notification recipients.

## Event intake: `POST /internal/events`

Receives the events written to the outbox by the Projects and Tasks APIs ([ADR 0002](../../docs/adr/0002-outbox-http-dispatch.md)).
Checks run in this order:

1. **Key**: only the Projects and Tasks keys are accepted (`403` for Web); no acting user is needed.
2. **Rate limit**: policy `internal-events`, 3,000/min per calling service ([ADR 0007](../../docs/adr/0007-per-service-rate-limits.md)).
3. **Size**: at most 64 KB, else `413` (audited `TooLarge`).
4. **Envelope and payload validation** (`EventEnvelopeValidator`, on the raw JSON): `eventId` non-empty, `type` one of
   the eight known types, `version` = 1, `occurredAt` a timestamp, `actorUserId` a predefined user, and the payload
   fields of that type (IDs, title/name length, assignees known). A failure is `400` Problem Details that never
   repeats the submitted value, so the sender dead-letters the event.
5. **Caller policy** (`EventCallerPolicy`, exact allow-list): Projects may send `ProjectCreated` only; Tasks may send
   `TaskCreated`, `TaskUpdated`, `TaskAssigned`, `TaskMoved`, `CommentAdded`, `CommentEdited`, `CommentDeleted`. A valid
   key sending another service's type gets `403`, so one service cannot forge another's events.
6. **De-duplication**: the `eventId` is stored in `ProcessedEvents` in one transaction with the handling. A known
   `eventId` answers `202` and does nothing (delivery is at-least-once). A race between two deliveries is resolved by
   the primary key.
7. **After the commit**: audit `EventReceived`, then `RealtimeBroadcaster` sends the hub signals. A failure to signal
   never undoes the saved state.

Responses: `202` accepted (also for duplicates), `400`, `403`, `413`, `429`. Dead-lettering is done by the sender.

## Board hub: `/hubs/board`

The SignalR hub for the Web server (contract: `contracts/realtime-hub.md`).

- **Web key only.** The route requires the Web key, and `OnConnectedAsync` re-checks it (defence in depth): another
  caller is aborted and audited as `HubRejected`. Browsers never connect; only the Web server does, over HTTPS.
- **Groups**: every connection is put in `projects` on connect. It may call `JoinProject`/`LeaveProject` (`project:{id}`),
  `JoinTask`/`LeaveTask` (`task:{id}`) and `JoinUser`/`LeaveUser` (`user:{id}`).
- **Argument validation** (`HubArgumentValidator`): project and task IDs must not be empty; user IDs must be a
  predefined user. A failure throws `HubException("Invalid request")` and audits `HubRejected`; the value is never
  echoed or logged.
- **Signals** (`RealtimeBroadcaster`, IDs only, never titles or text): `ProjectListChanged` to `projects` for
  `ProjectCreated`; `BoardChanged` to `project:{projectId}` for task created/updated/assigned/moved and comment
  added/deleted; `TaskChanged` to `task:{taskId}` for task updated/assigned/moved and all three comment events. The Web
  app re-fetches the data over REST.
- Signals sent while the Web server is disconnected are lost; the Web app resyncs on reconnect (FR-026).

## Notifications (US6, FR-027 to FR-030)

### Trigger rules (`NotificationTriggerRules`)

After an event passes validation and the caller policy, the intake evaluates it in the same transaction that records the
`eventId`. At most one notification is created, for the task's assignee, and **never for the actor** (nobody is notified
of their own action, and an unassigned task notifies nobody). Other event types create none.

| Event | Notification type | Summary (`{actor}` is the display name) |
|---|---|---|
| `TaskCreated` (with an assignee) | `TaskAssigned` | `{actor} assigned you '{title}'` |
| `TaskAssigned` | `TaskAssigned` | `{actor} assigned you '{title}'` |
| `TaskMoved` | `TaskMoved` | `{actor} moved '{title}' to {column}` (To Do, In Progress, In Review, Done) |
| `CommentAdded` | `TaskCommented` | `{actor} commented on '{title}'` |

Summaries are cut to 300 user-perceived characters. They contain a task title, so they are user text: they are never
logged and the Web app shows them as encoded text only (R9).

### Endpoints (Web key only)

| Route | Purpose |
|---|---|
| `GET /api/notifications?limit=&unreadOnly=` | The acting user's notifications, newest first (`limit` 1 to 100, default 50) |
| `GET /api/notifications/unread-count` | `{"count": n}` |
| `POST /api/notifications/{id}/read` | Marks one read (`204`; idempotent) |
| `POST /api/notifications/read-all` | Marks all of the acting user's notifications read (`204`) |

**Ownership (FR-029):** every query is scoped to the acting user. Reading someone else's notification and reading a
missing one both answer `404`, so existence is not revealed. Reads use the read rate limit, writes the write limit;
marking read is audited (`NotificationRead`, `NotificationsReadAll`).

### Live signal: `NotificationCreated`

After a notification is saved, `RealtimeBroadcaster.NotifyAsync` sends `NotificationCreated` to the recipient's
`user:{id}` group. The payload follows the `Notification` schema of `notifications-api.yaml`, with `type` as a name
such as `"TaskMoved"` and no recipient field. The Web bell treats it only as "re-fetch the count"; the REST API stays the
truth, and a lost signal is repaired by the resync on reconnect.

### Retention (FR-030)

`NotificationRetentionJob` (a hosted service) runs once a day and deletes in batches of 500 the notifications created more
than 30 days ago and the `ProcessedEvents` received more than 30 days ago. Its log line holds counts only.

## Run and test

```powershell
./scripts/verify.ps1 -Tests unit,web
./scripts/verify.ps1 -Tests integration -Class '<class>'   # for example '*NotificationsContractTests'
```
