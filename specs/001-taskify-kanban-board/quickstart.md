# Quickstart & Validation: Taskify Kanban Board

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This guide checks the feature end to end once it is implemented. API shapes are in
[contracts/](contracts/), and entities and rules are in [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK (`dotnet --version` → `10.0.x`)
- Aspire CLI (`dotnet tool install -g Aspire.Cli`) or the Aspire workload templates
- Docker Desktop or Podman running (PostgreSQL runs in a container)
- Trusted HTTPS dev certificate: `dotnet dev-certs https --trust`

## Run

```powershell
# from the repository root
dotnet restore
aspire run          # or: dotnet run --project src/Taskify.AppHost
```

On first run Aspire asks for the secret parameters (API keys, Postgres password) or reads them
from user secrets. Open the Aspire dashboard URL shown in the console. Check that
`postgres`, `projects-api`, `tasks-api`, `notifications-api`, and `web` are all **Running** and
healthy, then open the `web` endpoint.

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
```

Expected: all tests pass, no vulnerable packages, no build warnings.

## Manual scenarios

| # | Steps | Expected | Spec ref |
|---|-------|----------|----------|
| 1 | Open the web app | Five users listed with roles; no password asked | US1, FR-001/002 |
| 2 | Pick Priya, open each sample project | Four columns in order; Priya's cards highlighted; cards show title + assignee | US1, FR-008/013/014 |
| 3 | Drag a card from To Do to In Progress | Card moves within 1 s; still there after refresh | US2, SC-002, FR-018 |
| 4 | Open the same board in a second browser as Liam; move a card in browser 1 | Browser 2 updates within 2 s without refresh | Real-time (plan) |
| 5 | Use the card's "Move to…" menu with the keyboard | Same result as dragging | R6 |
| 6 | Drop a card back in its own column | No change; no new history entry | US2 #4 |
| 7 | Open task details | Status history newest first: who, from → to, when | US2 #5, FR-023 |
| 8 | Create project "QA Demo", add a task, assign it to Jordan | Task appears in To Do assigned to Jordan | US3 |
| 9 | Switch to Jordan | Notification badge shows 1 ("Priya assigned you …") | R10 |
| 10 | As Priya, comment on Jordan's task; switch to Jordan | Comment visible; no edit/delete buttons; new notification | US4, FR-017 |
| 11 | As Priya, edit then delete the comment | "edited" shown; then "Comment deleted by Priya Patel" with time | US4, FR-024 |
| 12 | Enter `<script>alert(1)</script>` as a task title | Shown as plain text; no alert | FR-020, SC-006 |
| 13 | Try a 201-char title, a whitespace-only comment, and an empty project name | Clear error message; nothing saved | FR-019, SC-005 |
| 14 | Edit and comment on a task in Done | Allowed, like any other column | Clarification Q5 |
| 15 | Stop and restart the AppHost | All changes are still there | FR-018, SC-004 |

## API smoke checks (optional)

APIs are internal, so run these from the integration test project or through the Aspire
dashboard's endpoint links. Every request needs `X-Api-Key` and `X-Taskify-User` (see contracts).

- `PUT /api/tasks/{id}/comments/{commentId}` (edit) by a non-author → `403`
- `POST /api/tasks` with an unknown `assigneeUserId` → `422`
- Any request without `X-Api-Key` → `401`; with an unknown `X-Taskify-User` → `400`
- `POST /internal/events` with the Web app's key → `401`/`403`
