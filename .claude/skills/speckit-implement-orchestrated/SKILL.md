---
name: speckit-implement-orchestrated
description: "Implement the open tasks in tasks.md through an orchestrator → worker loop: each small group of tasks goes to a speckit-worker subagent, the orchestrator verifies the build and tests, ticks the tasks and commits. Use instead of speckit-implement when the remaining work would not fit one context window."
argument-hint: "Optional scope, e.g. 'US4', 'Phase 7', 'T110-T126', or 'next' (default: everything open)"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

Use the input to limit the scope (a story, a phase or a task range). If it is empty, work through
all open tasks in order.

## Your role

You are the **orchestrator**. You plan work units, dispatch them to `speckit-worker` subagents,
verify their results and keep the books (tasks.md and git). **You do not write feature code, and
you do not read source files**, except to check a worker's claim when verification fails. Your
context should hold only tasks.md headings, task IDs, worker reports and verify summaries. That
is what lets this loop finish where a single-session implement ran out of context.

## 0. Pre-flight

1. Run `.specify/scripts/powershell/check-prerequisites.ps1 -Json -RequireTasks -IncludeTasks`
   and take `FEATURE_DIR` from it.
2. If `.specify/extensions.yml` exists, run its `hooks.before_implement` entries with the same
   rules as `/speckit-implement` (mandatory hooks run, optional ones are listed).
3. Make sure `git status --porcelain` is clean, apart from the files this skill is about to
   change. If it is not, ask the user whether to commit or stash first. Never discard their
   changes.
4. Establish the baseline: `./scripts/verify.ps1 -Tests unit,web`. A red baseline is not a reason
   to stop. Record which tests fail and give each failure to the work unit whose tasks own it
   (for example, failing `CommentsContractTests` belong to the unit containing T102). A red build
   is a reason to stop: report it to the user before dispatching anything.

## 1. Plan the work units (without reading tasks.md in full)

- List the open tasks with
  `grep -nE '^\s*- \[ \] T[0-9]+' FEATURE_DIR/tasks.md | cut -c1-140`, and list the headings with
  `grep -nE '^#{2,3} ' FEATURE_DIR/tasks.md`.
- Read the "Dependencies & Execution Order" section of tasks.md once.
- Group the open tasks into **work units** of about 3–8 related tasks, following the
  `### Tests for …` and `### Implementation for …` subsections and the order
  entities → DbContext and migration → validators → endpoints → Web components. Good units:
  - "US4 tests" (T100–T103), "US4 API" (T104–T107), "US4 Web" (T108–T109);
  - for a large story, split the backend from the Web side and the tests from the implementation.
  - Polish: each README or ADR task is small, so group 3–5 of them into one docs unit.
- Never put tasks from two different stories in one unit.
- Show the user the plan as a short table (unit, task IDs, one-line goal) and continue without
  waiting, unless the scope is unclear.

## 2. Dispatch loop

For each unit, in dependency order:

1. **Dispatch** a `speckit-worker` with the Agent tool (`subagent_type: "speckit-worker"`,
   `run_in_background: false`). The prompt contains only:
   - `FEATURE_DIR`, the task IDs and a one-line goal;
   - the baseline failures that belong to this unit, if any;
   - decisions or blockers from earlier units that matter to this one (1–3 lines).
   Do not paste task text, spec excerpts or code; the worker reads those itself.
2. **Parallelism**: run units in parallel (several Agent calls in one message) only when every
   task in them is marked `[P]`, they create files in different folders, and none touches shared
   registration files (`Program.cs`, a `DbContext`, migrations, `_Imports.razor`, AppHost). When
   in doubt, run them one after another. Concurrent writes to one working tree are the main risk
   here.
3. **Verify independently.** Do not trust the report. Run
   `./scripts/verify.ps1 -Tests unit,web`, plus `-NoBuild -Tests integration -Class '<class>'`
   for any integration test class this unit created or should have turned green.
4. **Decide:**
   - Green and the report says `done`: tick those tasks (step 3).
   - Red, or `partial`: dispatch **one** follow-up worker for the same unit, with the failing test
     names and error lines from verify as the notes. If it is still red, tick only the tasks the
     verify proves done, record the rest as blocked, and continue with the units that do not
     depend on them.
   - `blocked` on a design decision: collect it for the user. Do not guess on security, contracts
     or data loss.
5. **Context hygiene**: after each unit, keep one line about it in your running summary
   (`US4 API: T104–T107 ✓, 3 files`) and nothing more.

## 3. Bookkeeping

- Tick verified tasks one ID at a time with an exact edit of `- [ ] T104 ` → `- [X] T104 ` in
  tasks.md (a `sed -i -E 's/^(\s*- )\[ \] (T104 )/\1[X] \2/'` is fine).
- **Commit after every unit that ends green**, so a stopped run can resume from tasks.md and git
  alone:
  `git add -A && git commit -m "feat(US4): T104–T107 comment API"` with a body listing the task
  IDs and ending with the attribution trailer required by the session. Do not push.
- At the end of each **phase**, run `./scripts/verify.ps1 -Tests all` once. Report any
  integration failures, and re-run a failing class on its own before you blame the phase (the
  rate-limit flakiness is described in CLAUDE.md).

## 4. Stop conditions

Stop and report when any of these happens:
- every task in scope is ticked;
- the build is red and a follow-up worker could not fix it;
- the only remaining units depend on blocked tasks;
- your own context is getting large (more than about 15 units dispatched in this session). Say
  "resume with `/speckit-implement-orchestrated next`". Resuming is safe because tasks.md and git
  hold the whole state.

## 5. Final report

- A table of the units: tasks, status, commit hash.
- Tasks still open, with each blocker and the decision needed from the user.
- Test totals from the last verify run.
- Then run `hooks.after_implement` from `.specify/extensions.yml` if it exists. When everything is
  ticked, suggest `/speckit-converge` or `/speckit-analyze` as a final consistency check against
  spec.md.
