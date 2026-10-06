# Taskify.Web

The **Blazor Server** UI and the only resource reachable from outside the Aspire network (research R2). It holds no
data: it calls the three APIs with typed clients, over HTTPS, using its own API key.

## Pages

| Route | Page |
|---|---|
| `/` | `UserSelect`: choose one of the five predefined users |
| `/projects` | `Projects`: list and create projects |
| `/projects/{ProjectId}` | `Board`: columns, cards, "move to" menu |
| `/projects/{ProjectId}/tasks/{TaskId}` | `TaskDetails`: edit, assignee, history, comment thread |
| `/not-found` | `NotFound`: shown for unknown addresses (status code re-execute) |

Shared parts are in `Components/Board` and `Components/Shared`. Component tests: `tests/Taskify.Web.Tests` (bUnit).

## Acting user (phase 1, no login; deviation D1, [ADR 0003](../../docs/adr/0003-phase1-identity-and-api-keys.md))

1. `POST /session/select` (`SessionEndpoints`, an antiforgery-validated form post) checks the user exists in the
   directory, sets the `taskify.user` cookie and redirects to `/projects`. An empty or unknown user sets no cookie and
   is audited as `RequestRejected`.
2. The cookie value is encrypted and signed with Data Protection (`SelectedUserCookie`). The cookie is `HttpOnly`,
   `Secure`, `SameSite=Strict`, and the value expires on the server after 8 hours.
3. `SelectedUserMiddleware` runs on every page request, validates the cookie and the user, and puts the user ID and the
   source IP on the request principal. A tampered, expired or unknown cookie is rejected, audited and deleted. If the
   Projects API is unreachable the request is treated as "nobody selected" but the cookie is kept.
4. `CircuitIdentity` reads that principal once per circuit (a circuit has no `HttpContext`). Switching user is a full
   page navigation, so a new circuit starts.
5. Audit (FR-032): `UserSelected` records the user, the previous user and the source IP.

## Client IP forwarding

`ClientIpCapture` returns the browser IP captured from the principal. `ApiClientBase` sends it as
`X-Taskify-Client-Ip`, so API audit entries carry the end user's address.

## Typed API clients

`Services/ApiClients`: `ProjectsClient`, `TasksClient` and `NotificationsClient`, built on `ApiClientBase` and registered
in `WebClientSetup` with the base addresses `https://projects-api`, `https://tasks-api` and
`https://notifications-api` (Aspire service discovery). Every request carries `X-Api-Key` (the Web key,
`ApiKeys__OwnKey`), `X-Taskify-User` (the selected user) and `X-Taskify-Client-Ip`. Responses become `ApiResult<T>`
and Problem Details errors are turned into user-facing messages. User-visible text lives in `UiText`.

## Security headers and CSP

`SecurityHeadersMiddleware` adds to every response:

- `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`

The app has no inline scripts or styles. Antiforgery's own `X-Frame-Options` is suppressed and Blazor's frame-ancestors
policy is set to `'none'`, so all three agree. HTTPS redirection is on, HSTS is enabled outside Development, and a
per-IP rate limit (1,200 requests/min) guards the app.

## Data Protection key encryption

`DataProtectionSetup` persists the key ring to `DataProtection:KeysPath` (default under LocalApplicationData) and
encrypts it at rest with the PFX in `DataProtection:Certificate` (+ `CertificatePassword`), from the AppHost secrets
`dataprotection-cert` and `dataprotection-cert-password` (create them with `scripts/init-dev-secrets.ps1`). Outside
Development the app refuses to start without a certificate.

## No `MarkupString` (R9)

User text (names, titles, descriptions, comments) is always rendered through Razor's encoding, never as `MarkupString`.
CI (`.github/workflows/ci.yml`, step "Ban MarkupString in the Web app") greps `src/Taskify.Web` and fails on any use
unless the line carries a `markup-allowed:` comment.

## Real-time updates (US5, [ADR 0008](../../docs/adr/0008-realtime-signals-and-refetch.md))

- **`IRealtimeBoard` / `RealtimeBoardService`**: one SignalR connection from the Web server to the Notifications API hub,
  shared by all circuits and opened by the first subscription. The hub address comes from the configuration key
  `services:notifications-api:https:0` (SignalR WebSockets bypass `HttpClient` service discovery), falls back to
  `https://notifications-api/hubs/board`, and must be HTTPS or the service refuses to start (R16). The connection sends
  the Web key (`ApiKeys__OwnKey`), the only key the hub accepts. The first start is retried every 5 seconds; later drops
  use automatic reconnect.
- **Group reference counting**: `SubscribeProject`, `SubscribeTask`, `SubscribeUser` and `SubscribeProjectList` return an
  `IDisposable`. The hub group is joined by the first subscriber and left by the last, so two circuits on one board use
  one membership.
- **Resync after reconnect (FR-026)**: groups are lost on reconnect and signals sent meanwhile are gone, so the service
  rejoins every group and calls every subscriber, which makes each screen re-fetch.
- **De-duplication**: a bounded LRU set of recent `(signal kind, eventId)` pairs drops repeated signals.
- **`CoalescingRefresher`**: per open screen, the first signal re-fetches at once and signals inside a 1-second window
  merge into one trailing re-fetch. A board therefore costs at most about 60 re-fetches a minute, inside the viewer's
  300 reads/min budget (FR-031). A failed re-fetch is reported and does not take the circuit down.
- **Pages** (`Board`, `Projects`) re-fetch over REST on a signal. Signals carry IDs only and nothing is rendered from
  them. Log messages contain connection state only.

## Notifications (US6, FR-027 to FR-029)

- **`NotificationBell`** (in `MainLayout`, parameter `UserId`): shows the unread count as a badge. Its dropdown lists the
  latest 50 notifications, newest first, with a relative time and a link to `/projects/{projectId}/tasks/{taskId}`.
  Clicking one marks it read; "Mark all read" marks all. Summaries are rendered as encoded text, never `MarkupString` (R9).
- **`NotificationsClient`**: `GET /api/notifications/unread-count`, `GET /api/notifications?limit=`, and the two `POST`
  read calls. After every action the bell re-fetches the count (and the list when open); the API is the truth.
- **Realtime**: the bell subscribes to `user:{id}` through `IRealtimeBoard.SubscribeUser`. `RealtimeBoardService` handles
  `NotificationCreated` (binding only the ID, because the server sends `type` as a name and the bell never shows the
  content) and, as the payload has no recipient, tells every `user:` subscriber; each re-fetches its own user's data over
  REST. A resync after reconnect does the same. Signals are de-duplicated by notification ID.
- **User switch**: when `UserId` changes, the bell leaves the old group, joins the new one, clears its list and re-fetches.
  Disposing the bell leaves the group.

## Run and test

```powershell
./scripts/verify.ps1 -Tests unit,web
./scripts/verify.ps1 -NoBuild -Tests web
```

Run the whole system through `src/Taskify.AppHost`; the Web resource is the only external endpoint (HTTPS).
