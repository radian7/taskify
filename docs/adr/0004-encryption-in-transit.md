# ADR 0004: Encryption in transit everywhere

- Status: accepted (implemented)
- Source: research R16; AppHost tasks T037 and T038

## Context

Constitution Principle I requires encryption in transit between every pair of components,
including to the database and for the real-time hub.

## Decision

- **Between services:** every service URI is `https://<resource>`. `https+http://` is never used
  because it falls back to plain HTTP. The AppHost defines HTTPS endpoints only for the APIs, so no
  HTTP listener exists. Locally the ASP.NET Core dev certificate is used. Deployment uses platform
  certificates.
- **Hub:** the SignalR client connects to `https://notifications-api/hubs/board` (WSS).
- **Browser:** HTTPS with HSTS.
- **Database, deployment:** Npgsql `SSL Mode=VerifyFull` against a managed PostgreSQL that serves TLS.
- **Database, development:** `SSL Mode=Require`. The AppHost generates a self-signed server
  certificate at startup, places it in the container with `WithContainerFiles` (owner `postgres`, key
  mode `0600`) and starts PostgreSQL with `-c ssl=on`, `ssl_cert_file` and `ssl_key_file`. No key
  material is stored in the repository. `Require` encrypts but does not verify the certificate, which
  is acceptable for a container on the private local Aspire network.
- **Verification:** an integration test checks that each API refuses plain HTTP and that a database
  session reports `ssl = on` (`pg_stat_ssl`).

### Fallback

If the container TLS setup proves unworkable on a developer OS, record a development-only deviation
(local container on a private network, no real data) in plan Complexity Tracking, with reviewer
approval. Deployment keeps `VerifyFull`.

## Alternatives considered

- `https+http://` service URIs: rejected because of the plain-HTTP fallback.
- Service-mesh mTLS: too much infrastructure for phase 1. It may replace API keys together with D2 in
  phase 2.
- Committing a dev certificate: rejected, no key material in the repository.

## Consequences

- Developers need a trusted dev certificate and a container runtime that honours file ownership and mode.
- Dev traffic to PostgreSQL is encrypted but not protected against a man-in-the-middle.
