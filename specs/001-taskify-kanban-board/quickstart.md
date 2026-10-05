# Quickstart & Validation: Taskify Kanban Board

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This guide checks the feature end to end once it is implemented. API shapes are in
[contracts/](contracts/), and entities and rules are in [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (`dotnet --version` → `10.0.x`)
- Aspire CLI (`dotnet tool install -g Aspire.Cli`) or the Aspire workload templates
- Docker Desktop or Podman running (PostgreSQL runs in a container)
- Trusted HTTPS dev certificate: `dotnet dev-certs https --trust` (all service-to-service traffic
  is HTTPS only; research R16)
- Node.js 20+ (only for validating the AsyncAPI contract with `npx @asyncapi/cli`)

## Run

Create the development secrets once per machine (API keys, the PostgreSQL password and the Data Protection
certificate; nothing secret is committed to the repository). The script is safe to re-run and keeps existing values:

```powershell
# from the repository root
./scripts/init-dev-secrets.ps1     # stores the secrets in .NET user secrets
dotnet restore
aspire run          # or: dotnet run --project src/Taskify.AppHost
```

Open the Aspire dashboard URL shown in the console. Check that `postgres`, `projects-api`, `tasks-api`,
`notifications-api`, and `web` are all **Running** and healthy, then open the `web` endpoint.

## Seed data

| User | Role |
|------|------|
| Maya Chen | Product Manager |
| Liam Novak | Engineer |
| Priya Patel | Engineer |
| Tomasz Wiśniewski | Engineer |
| Jordan Lee | Engineer |

There are three sample projects (Mobile App Launch, Website Redesign, Internal Tools). Each has
tasks in all four columns, assigned to a mix of users, with some unassigned.

## Automated validation

```powershell
dotnet build -warnaserror           # fails on missing XML docs (Principle IV)
dotnet test tests/Taskify.UnitTests
dotnet test tests/Taskify.Web.Tests          # bUnit component tests
dotnet test tests/Taskify.IntegrationTests   # starts AppHost + Postgres; contract tests
dotnet list package --vulnerable --include-transitive   # expect: no vulnerable packages
npx @asyncapi/cli validate specs/001-taskify-kanban-board/contracts/events.asyncapi.yaml   # expect: valid
```

Expected: all tests pass, no vulnerable packages, no build warnings.

## Manual scenarios

| # | Steps | Expected | Spec ref |
|---|-------|----------|----------|
| 1 | Open the web app | Five users listed with roles; no password asked | US1, FR-001/002 |
| 2 | Pick Priya, open each sample project | Four columns in order; Priya's cards highlighted; cards show title + assignee | US1, FR-008/013/014 |
| 3 | Drag a card from To Do to In Progress | Card moves within 1 s; still there after refresh | US2, SC-002, FR-018 |
| 4 | Open the same board in a second browser as Liam; move a card in browser 1 | Browser 2 updates within 2 s without refresh | US5, FR-025, SC-008 |
| 5 | Use the card's "Move to…" menu with the keyboard | Same result as dragging | R6 |
| 6 | Drop a card back in its own column | No change; no new history entry | US2 #4 |
| 7 | Open task details | Status history newest first: who, from → to, when | US2 #5, FR-023 |
| 8 | Create project "QA Demo", add a task, assign it to Jordan | Task appears in To Do assigned to Jordan | US3 |
| 9 | Switch to Jordan | Notification badge shows 1 ("Priya assigned you …") | US6, FR-027 |
| 10 | As Priya, comment on Jordan's task; switch to Jordan | Comment visible; no edit/delete buttons; new notification | US4, FR-017 |
| 11 | As Priya, edit then delete the comment | "edited" shown; then "Comment deleted by Priya Patel" with time | US4, FR-024 |
| 12 | Enter `<script>alert(1)</script>` as a task title | Shown as plain text; no alert | FR-020, SC-006 |
| 13 | Try a 201-char title, a whitespace-only comment, and an empty project name | Clear error message; nothing saved | FR-019, SC-005 |
| 14 | Edit and comment on a task in Done | Allowed, like any other column | Clarification Q5 |
| 15 | Stop and restart the AppHost | All changes are still there | FR-018, SC-004 |
| 16 | In two browsers (Priya, Liam), edit the same task's title at nearly the same time | The later save wins; both browsers show the final title within 2 s | FR-011, R14 |
| 17 | Create a task titled with exactly 200 emoji (e.g. 👨‍👩‍👧‍👦 repeated); then try 201 | The counter shows 200/200 and the save works; 201 is rejected with a clear message | FR-019, R7 |
| 18 | Switch from Priya to Jordan, then open the Aspire dashboard's structured logs for `web` | `UserSelected` audit entries with the chosen user, the previous user, the time and the source IP; no task text in any log | FR-032, FR-022 |
| 19 | As Jordan, open the notification list; as Liam, check the notification count | Jordan sees only his own notifications; Liam's count is unaffected | FR-029 |
| 20 | Stop the `notifications-api` resource in the dashboard, move a card as Priya, then start it again | The move is saved straight away; Liam's open board catches up within a few seconds of the restart | FR-026 |

## API smoke checks (optional)

APIs are internal, so run these from the integration test project or through the Aspire
dashboard's endpoint links. Every request needs `X-Api-Key` and `X-Taskify-User` (see contracts).

- `PUT /api/tasks/{id}/comments/{commentId}` (edit) by a non-author → `403`
- `POST /api/tasks` with an unknown `assigneeUserId` → `422`
- Any request without `X-Api-Key` → `401`; with an unknown `X-Taskify-User` → `400`
- `POST /internal/events` with the Web app's key → `403`
- `GET /api/users` with a valid key and **no** `X-Taskify-User` → `200` (exempt route)
- Tasks API calling `GET /api/projects/{id}` on the Projects API with the Tasks key → `200`
- `POST /api/notifications/{id}/read` for another user's notification → `404`
- 61 writes within one minute by the same acting user → the 61st returns `429` with `Retry-After`; nothing saved
- A plain `http://` request to any API → connection refused (no HTTP listener)
- A title over the abuse guard (more than 3,200 UTF-16 code units) → `400`; a request body over 1 MB → `413`
