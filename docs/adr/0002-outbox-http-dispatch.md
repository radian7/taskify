# ADR 0002: Transactional outbox with HTTP dispatch

- Status: accepted (implemented)
- Source: research R4; code in `Taskify.Security/Outbox`

## Context

Tasks and Projects must tell the Notifications API about every saved change (`TaskCreated`,
`TaskMoved`, `CommentAdded`, `ProjectCreated` and so on), even when Notifications is briefly down.
Phase 1 should not add a message broker.

## Decision

- The publishing service writes the event to an `OutboxMessage` table in the same database
  transaction as the change.
- A background `OutboxDispatcher` posts pending rows to the Notifications API `/internal/events`
  endpoint over HTTPS, with the service's `X-Api-Key`.
- `202` marks the event dispatched.
- `400` or `403` means the receiver will never accept the event (invalid payload, or the key is not
  allowed to publish that event type). The event is **dead-lettered**: `DeadLetteredAt` is set, an
  `EventDeadLettered` audit entry is written and the `OutboxDeadLettered` counter is incremented. It is
  not retried.
- Everything else (5xx, `401`, `404`, `429`, timeouts) is retried, so a restart or an outage loses
  nothing.

## Alternatives considered

- RabbitMQ or Azure Service Bus: more infrastructure for three services. The HTTP dispatcher can be
  replaced later without changing the event contracts (ADR 0005).
- Fire-and-forget HTTP calls: events are lost on failure.
- PostgreSQL LISTEN/NOTIFY: couples services to one database server.
- Retrying `400` and `403` forever: a rejected event would be resent for ever.

## Consequences

- Delivery is at-least-once, so receivers must tolerate duplicates.
- Dead-lettered events need an operator: they are visible in the audit log and the metric, and stay in
  the table.
- Real-time updates can lag while Notifications is down. The Web app re-fetches on reconnect
  (FR-026).
