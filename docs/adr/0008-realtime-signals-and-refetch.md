# ADR 0008: Real-time signals carry IDs only; the Web app re-fetches over REST

- Status: accepted (implemented)
- Source: research R5, FR-025, FR-026; code in `Taskify.Notifications.Api/Hubs`, `Taskify.Web/Services`

## Context

Boards must show other users' changes within 2 seconds (FR-025) and catch up after a lost connection (FR-026).
The Web app is the only client of the APIs, and each API owns its data and enforces its own rules.

## Decision

- A SignalR hub (`/hubs/board`) lives in the Notifications API, which already receives every domain event on
  `/internal/events` ([ADR 0002](0002-outbox-http-dispatch.md)). After an event is committed and de-duplicated by
  `eventId`, `RealtimeBroadcaster` sends a signal to the groups `projects`, `project:{id}` or `task:{id}`.
- Signals carry IDs, the event type, the actor and the time only, never titles, descriptions or comment text.
- Only the Web server connects (Web key, HTTPS). It subscribes per open screen and, on a signal, re-fetches the data
  from the owning API over REST with the viewer's own identity. A `CoalescingRefresher` limits this to one re-fetch per
  second per screen, within the read limit ([ADR 0007](0007-per-service-rate-limits.md)).
- Signals are hints and are not stored. After a reconnect the Web app rejoins its groups and re-fetches everything
  (resync).

## Alternatives considered

- Signals with the full data: copies content into a second place, bypasses the owning API's checks and risks leaking text.
- Browsers connecting to the hub directly: exposes an internal service and puts a key in the browser (R8, R2).
- Polling: misses the 2-second target or exceeds the read limit.
- Replaying missed signals: needs a signal store; a re-fetch is simpler and always correct.

## Consequences

- The saved state is always the truth; a lost or duplicated signal is harmless.
- Each signal costs one REST read per open screen (bounded by coalescing).
- While the Notifications API is down, changes are saved and queued in the outbox, and screens stay stale until it is
  back and the Web app has resynced.
- The Web app needs the hub address in its configuration, because WebSockets bypass `HttpClient` service discovery.
- With several Notifications instances a backplane would be needed (phase 1 runs one).
