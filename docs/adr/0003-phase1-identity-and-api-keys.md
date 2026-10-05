# ADR 0003: Phase 1 identity and service API keys (deviations D1 and D2)

- Status: accepted for phase 1, time-limited
- Source: research R8; plan Complexity Tracking D1 and D2

## Context

The spec has no end-user login in phase 1: users are picked from a list of five. Services still must
not accept calls from unknown callers (Principle III). The constitution asks for standard
authentication (OAuth/OIDC), which phase 1 does not provide.

## Decision

**Acting user.** The Web app keeps the selected user in the circuit and a per-browser cookie
(protected by a Data Protection key ring encrypted with a certificate) and sends it as the
`X-Taskify-User` header. Every API checks that the header names one of the five users and uses it
for authorship and authorisation. Exempt: `GET /api/users*`, `/internal/events` (the actor is in the
envelope), `/health` and `/alive`. The Web server forwards the end user's IP as `X-Taskify-Client-Ip`;
APIs trust it only from the Web key. Every user selection, switch and restore is audited.

**Service keys.** Every service-to-service call carries `X-Api-Key`, a per-caller secret from an Aspire
secret parameter (`web-api-key`, `projects-api-key`, `tasks-api-key`, `notifications-api-key`). A
missing or unknown key gets `401`. A known key on a route the matrix does not allow gets `403`.

| Service (receiver) / caller key | Web | Projects | Tasks | Notifications |
|---|---|---|---|---|
| Projects API | all routes | - | read routes only | `GET /api/users*` only |
| Tasks API | all routes | - | - | - |
| Notifications API `/api/*`, `/hubs/board` | all routes | - | - | - |
| Notifications API `/internal/events` | `403` | `ProjectCreated` only | task and comment events only | - |

Each resource receives only its own key and the keys of the callers it accepts.

### Accepted deviations

| | D1: no end-user authentication | D2: API keys instead of OAuth 2.0 client credentials |
|---|---|---|
| Why | Phase 1 scope has no login | No identity provider for three internal services in phase 1 |
| Mitigation | Trusted internal network only; server-side validation of the acting user; rate limits; audit of every selection and change with source IP | Keys are secrets, limited by the matrix and per event type, sent only over TLS (ADR 0004), rotatable |
| Owner | Adrian Rogalczyk (project maintainer) | Adrian Rogalczyk (project maintainer) |
| Review date | 2027-01-04, then each quarterly constitution review | 2027-01-04, then each quarterly constitution review |
| Expires | When phase 2 adds OIDC sign-in and `X-Taskify-User` is replaced by token claims | When phase 2 introduces client credentials from the same identity provider as D1 |

## Alternatives considered

- OIDC now: out of scope for phase 1.
- No inter-service authentication: violates Principle III.
- Service-mesh mTLS: too much infrastructure for phase 1.

## Consequences

- Anyone who can reach the Web app can act as any user (D1). Deploy on a trusted network only.
- Phase 2 replaces the header and the keys. The API contracts keep the same shape.
- A leaked key is limited to the routes and event types in the matrix.
