# Taskify

Kanban board built with .NET 10 and Aspire: three APIs, a Blazor Server web app and PostgreSQL.
The feature is specified in `specs/001-taskify-kanban-board/` (spec, plan, research R1–R16,
data-model, contracts, tasks). The governance rules are in `.specify/memory/constitution.md`.

## Layout

| Project | Role |
|---|---|
| `src/Taskify.AppHost` | Aspire orchestration (PostgreSQL, the 3 APIs and Web; HTTPS only, R16) |
| `src/Taskify.ServiceDefaults` | OpenTelemetry, health and resilience defaults |
| `src/Taskify.Contracts` | Shared event and hub contracts, enums, seed IDs |
| `src/Taskify.Security` | API keys (R8), acting user, validation filter, audit, outbox, rate limiting, Problem Details |
| `src/Taskify.Projects.Api` | Projects and the user directory |
| `src/Taskify.Tasks.Api` | Tasks, move history, comments |
| `src/Taskify.Notifications.Api` | `/internal/events`, notifications, the SignalR `BoardHub` |
| `src/Taskify.Web` | Blazor Server UI |
| `tests/Taskify.UnitTests` | Validators, domain rules, security policies |
| `tests/Taskify.Web.Tests` | bUnit component tests |
| `tests/Taskify.IntegrationTests` | Contract and integration tests against the running AppHost |
| `tests/Taskify.TestSupport` | Shared `TaskifyAppFixture` (starts the AppHost once per assembly) |
| `tests/Taskify.E2ETests` | Playwright smoke tests |

Each API follows the same folder layout: `Domain/`, `Data/` (DbContext and migrations),
`Validation/`, `Endpoints/`, `Clients/`. Before adding a new type, look at a finished sibling in
the same folder and follow its pattern.

## Build and test

Always use `scripts/verify.ps1`. It prints a compact summary instead of the full MSBuild and test
output, which is very long.

```powershell
./scripts/verify.ps1                                   # build (-warnaserror) + unit + bUnit
./scripts/verify.ps1 -BuildOnly
./scripts/verify.ps1 -NoBuild -Tests unit -Class '*CommentTextValidatorTests'
./scripts/verify.ps1 -Tests integration -Class '*CommentsContractTests'
./scripts/verify.ps1 -Tests all                        # what CI runs
```

- The build treats warnings as errors. A missing XML doc comment on a public member (CS1591)
  fails it.
- The test runner is Microsoft.Testing.Platform with xUnit v3 (`dotnet test --project <dir>`).
- Integration and E2E tests need a container runtime. On this machine that is Podman, not Docker;
  `verify.ps1` sets `ASPIRE_CONTAINER_RUNTIME=podman` and starts the Podman machine. A whole
  integration run takes minutes, so filter by class while you work.
- If you call `dotnet` directly anyway, use `-v q --nologo -clp:ErrorsOnly` and pipe the output
  through a filter. Never dump a full test log into the conversation.

## Rules every change follows

The "Conventions that apply to every task" section at the top of
`specs/001-taskify-kanban-board/tasks.md` is binding: XML docs, validation, R8 keys, HTTPS, outbox,
audit and last-write-wins. Read it before writing code. The other hard rules:

- Never render user text with `MarkupString` (R9; CI bans it).
- Never log titles, descriptions or comment text.
- Never put secrets in code or config; dev secrets come from `scripts/init-dev-secrets.ps1`.

## Known issues

- The integration tests are flaky under the per-user write rate limit (SC-010, 60 writes/min).
  Tests share one AppHost and one seeded user, so a full run can get `429` in `MoveContractTests`
  or `CreateEditContractTests`. Re-run the class on its own before treating such a failure as a
  regression.
