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

## Run and test

```powershell
./scripts/verify.ps1 -Tests unit,web
./scripts/verify.ps1 -NoBuild -Tests web
```

Run the whole system through `src/Taskify.AppHost`; the Web resource is the only external endpoint (HTTPS).
