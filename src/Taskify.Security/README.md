# Taskify.Security

The shared library every API (and, for audit and metrics, the Web app) uses. It is not a service: it has no endpoints and
no database of its own. Each API calls `AddTaskifySecurity` at startup and `UseTaskifySecurity` in its pipeline.

## Request pipeline (`UseTaskifySecurity`)

In this order:

1. `UnauditedRejectionMiddleware`: first, so it sees the final status of every response. It audits the refusals nothing
   else audits: `405`, `413` and `415` that the framework answers itself (a bare status from minimal-API body binding,
   a wrong method). It never changes the status or the body. `AuditLogger` sets an `HttpContext.Items` marker
   (`taskify.rejection-audited`) once it has audited a rejection, and the middleware skips marked requests, so no
   refusal is audited twice.
2. Exception handler and status-code pages (Problem Details, `TaskifyExceptionHandler`).
3. `ApiKeyMiddleware`: `X-Api-Key` against the accepted callers of the route (`AllowCallers`, R8). Missing or unknown
   `401`, known but not allowed `403`.
4. `ClientIpMiddleware`: the source IP (`X-Taskify-Client-Ip` from the Web app).
5. `ActingUserMiddleware`: `X-Taskify-User` must be a predefined user (`400` otherwise), unless the route is exempt.
6. Rate limiter (`RateLimitPolicies`, [ADR 0007](../../docs/adr/0007-per-service-rate-limits.md)).

## Other parts

| Folder | Purpose |
|---|---|
| `Audit` | `AuditLogger` (identifiers only, never user text), the `taskify.rejections` and `taskify.outbox.deadlettered` counters |
| `Outbox` | `OutboxWriter` and `OutboxDispatcher` ([ADR 0002](../../docs/adr/0002-outbox-http-dispatch.md)) |
| `Data` | Database setup and per-service roles ([ADR 0006](../../docs/adr/0006-per-service-database-roles.md)) |
| `Validation` | `ValidationEndpointFilter`, text normalisation and length rules (grapheme counting, R7) |
| `Users` | The acting user and the remote user directory client |
| `Hosting`, `Json`, `Errors` | Request size limits, strict JSON, Problem Details |

Alert rules built on the counters: see the "Alerts" section of [`Taskify.Web/README.md`](../Taskify.Web/README.md).
