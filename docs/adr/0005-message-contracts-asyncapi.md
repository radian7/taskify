# ADR 0005: Machine-readable message contracts with AsyncAPI

- Status: accepted (implemented)
- Source: research R15; `specs/001-taskify-kanban-board/contracts/events.asyncapi.yaml`

## Context

Principle IV requires machine-readable contracts. OpenAPI describes the event endpoint `payload`
only as `object`, so the real event shapes and the SignalR hub messages were undocumented.

## Decision

- All messages between services are defined in `events.asyncapi.yaml` (AsyncAPI 3.1): the domain
  events on the `/internal/events` channel and the hub messages on `/hubs/board`, with JSON Schema
  payloads and `additionalProperties: false`.
- `events.md` and `realtime-hub.md` remain readable guides. The AsyncAPI file wins on conflict.
- CI runs `npx @asyncapi/cli validate` on the file.
- Contract tests serialise every C# event record and hub payload and validate them against the
  schemas (JsonSchema.Net). The FluentValidation rules of the receiver are tested against the same
  samples, so publisher, document and consumer cannot drift apart.

## Alternatives considered

- Plain JSON Schema files: no channels, security or hub description.
- Protobuf: does not fit JSON over HTTP and SignalR, and adds a code-generation step.

## Consequences

- A change to an event record must update the AsyncAPI file in the same change, or tests fail.
- A later broker (ADR 0002) can reuse the same schemas.
