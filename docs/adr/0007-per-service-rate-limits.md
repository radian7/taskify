# ADR 0007: Per-service, per-instance rate limits

- Status: accepted (implemented)
- Source: research R9, FR-031, spec clarification 2026-10-05; code in `Taskify.Security/RateLimiting`

## Context

FR-031 limits each user to 60 changes and 300 reads per minute, and normal use (1 change per second
for a minute, SC-010) must never be rejected. The question was whether the count is one total across
Taskify or separate per service.

## Decision

The built-in ASP.NET Core `SlidingWindowRateLimiter` (1-minute window, 6 segments, no queueing) runs
in each API, so limits are **counted per service and per instance**:

| Policy | Partition | Limit |
|---|---|---|
| `writes` | acting user | 60/min on POST, PUT, DELETE under `/api/*` |
| `reads` | acting user | 300/min on GET under `/api/*` |
| `web-ip` | client IP | 1,200/min on Web requests and new circuits (outer guard) |
| `internal-events` | calling service | 3,000/min on `/internal/events` |

The partition key is the acting user, else the calling service, else the client IP, so limits are per
person, not per shared Web key. Requests without a user (service directory lookups) are counted under
the caller. A rejection is `429` Problem Details with `Retry-After`, changes nothing and is audited.
The clarification of 2026-10-05 confirms that each of projects, tasks and notifications allows each
user 60 changes and 300 reads on its own.

## Alternatives considered

- One total per user across Taskify: needs a shared store or a gateway, which phase 1 does not have.
- A distributed limiter (for example Redis): extra infrastructure for single-instance services.
- Limiting only in the Web app: can be bypassed by calling an API directly.

## Consequences

- A user can make more than 60 writes per minute in total if they write to several services.
- Scaling a service to several instances multiplies its limit. A shared store must be added then
  (noted in the README).
- Tests that share one seeded user can hit `429` (known flakiness, see CLAUDE.md).
