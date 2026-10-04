# Feature Specification: Taskify Kanban Board

**Feature Branch**: `001-taskify-kanban-board`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "Develop Taskify, a team productivity platform where predefined users create projects, assign tasks, comment, and move tasks across Kanban columns (To Do, In Progress, In Review, Done). Five users (one product manager, four engineers), three sample projects, no login for this first phase."

## Clarifications

### Session 2026-10-04

- Q: Who should be allowed to move a task card to a different column? → A: Any user can move
  any task.
- Q: Should a task's status changes be kept as a visible history on the task? → A: Yes; task
  details show a status history with who moved it, from/to column, and when.
- Q: What should happen when a comment's author deletes it? → A: The text is removed and replaced
  with a "Comment deleted by [author]" placeholder showing when it was deleted.
- Q: Who should be allowed to assign or reassign a task to a user? → A: Any user can assign,
  reassign, or unassign any task to any predefined user.
- Q: Should a task in the Done column still be editable and open for comments? → A: Yes; Done
  tasks behave like tasks in any other column.
- Q: Should phase 1 include live board updates and in-app notifications? → A: Yes, both. Other
  users' changes appear within 2 seconds without a refresh, and users get in-app notifications
  when someone else assigns them a task, moves a task assigned to them, or comments on a task
  assigned to them. Notifications are kept for 30 days.
- Q: What happens when two users edit the same task's title, description, or assignee at nearly
  the same time? → A: The last save wins (same rule as moves); the other user's screen updates
  live to show the final values.
- Q: How many changes may one person make per minute before Taskify asks them to slow down?
  → A: 60 changes per minute per user; reads are limited separately at 300 per minute per user.
- Q: What counts as one character in length limits? → A: Each user-perceived (visible) character
  counts as 1; an emoji, a combined accented letter, or a flag is one character.
- Q: Should choosing or switching the acting user be recorded in the audit log? → A: Yes; log
  every selection and switch with the user chosen, the previous user (if any), the time, and the
  source IP address.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pick a user and view a project board (Priority: P1)

A team member opens Taskify and sees the five predefined users. They select themselves (no
password), see the list of projects, open one, and see its tasks arranged in four Kanban columns:
To Do, In Progress, In Review, and Done. Tasks assigned to the selected user are visually
distinguished from other tasks.

**Why this priority**: Viewing the board is the foundation for every other interaction. On its
own, with the sample data, it already lets the team see the status of work.

**Independent Test**: Start Taskify with the sample data, select any user, open each of the three
sample projects, and confirm every task appears in the correct column and that the selected user's
tasks are highlighted.

**Acceptance Scenarios**:

1. **Given** Taskify is opened for the first time, **When** the start screen loads, **Then** the
   five predefined users are listed with their name and role (one Product Manager, four
   Engineers), and no password is requested.
2. **Given** a user has been selected, **When** the project list loads, **Then** the three sample
   projects are shown.
3. **Given** a user opens a project, **When** the board loads, **Then** four columns are shown in
   the order To Do, In Progress, In Review, Done, and each task appears in exactly one column.
4. **Given** the selected user is assigned to some tasks on the board, **When** the board loads,
   **Then** those tasks are visually distinguished from tasks assigned to others or unassigned.
5. **Given** a user is selected, **When** they choose to switch user, **Then** they return to the
   user selection screen and can pick a different user.

---

### User Story 2 - Move tasks across Kanban columns (Priority: P1)

A team member moves a task from one column to another (for example, from To Do to In Progress)
to reflect its current status. The change is saved and visible to anyone who opens the board
afterwards.

**Why this priority**: Moving tasks through the workflow is the core purpose of a Kanban board.

**Independent Test**: Using the sample data, move a task to each of the other three columns in
turn, reload the board, and confirm the task stays in the last column it was moved to.

**Acceptance Scenarios**:

1. **Given** a task in To Do, **When** the user moves it to In Progress, **Then** it appears in
   In Progress and no longer in To Do.
2. **Given** a task has been moved, **When** the board is reopened (by the same or another user),
   **Then** the task is shown in its new column.
3. **Given** a task in any column, **When** the user moves it to any other column (including
   backwards, such as Done to In Progress), **Then** the move is allowed.
4. **Given** a task is moved, **When** the user drops it in the same column it came from, **Then**
   nothing changes and no history entry is added.
5. **Given** a task has been moved one or more times, **When** any user opens the task details,
   **Then** they see its status history, newest first, with who moved it, the from and to
   columns, and when.

---

### User Story 3 - Create projects and tasks and assign them (Priority: P2)

A team member creates a new project, adds tasks to it with a title and optional description, and
assigns each task to one of the five users or leaves it unassigned. They can later edit a task's
title, description, or assignee.

**Why this priority**: The sample projects allow a demo, but real use requires creating new work
and deciding who owns it.

**Independent Test**: Create a new project, add three tasks, assign two of them to different
users, edit one task's assignee, and confirm everything shows up correctly on the board (new tasks
start in To Do).

**Acceptance Scenarios**:

1. **Given** a user is on the project list, **When** they create a project with a valid name,
   **Then** the project appears in the list for all users with an empty board.
2. **Given** a user is on a project board, **When** they create a task with a valid title,
   **Then** the task appears in the To Do column.
3. **Given** a user is creating or editing a task, **When** they choose an assignee, **Then** they
   can pick any of the five predefined users or leave the task unassigned.
4. **Given** a task exists, **When** a user edits its title, description, or assignee and saves,
   **Then** the changes are shown on the board and in the task details.
5. **Given** a user submits a project name or task title that is empty or too long, **When** they
   try to save, **Then** the save is rejected, a clear message explains the problem, and no data
   is changed.

---

### User Story 4 - Comment on tasks (Priority: P3)

A team member opens a task and reads its comments, then adds their own comment. They can edit or
delete comments they wrote but not comments written by other users.

**Why this priority**: Comments support collaboration but the board is usable without them.

**Independent Test**: Open a task, add a comment as one user, switch to another user, confirm the
comment is visible but cannot be edited or deleted, switch back, and edit and delete the comment.

**Acceptance Scenarios**:

1. **Given** a user opens a task, **When** the task details load, **Then** all comments on the task
   are shown in chronological order with author name and time posted.
2. **Given** a user writes a valid comment, **When** they post it, **Then** it appears in the list
   attributed to the selected user.
3. **Given** a comment written by the selected user, **When** they edit it, **Then** the change is
   saved and shown with an "edited" indicator.
4. **Given** a comment written by the selected user, **When** they delete it, **Then** its text is
   removed and it is replaced in the thread by a "Comment deleted by [author]" placeholder showing
   when it was deleted.
5. **Given** a comment written by a different user, **When** the selected user views it, **Then**
   no edit or delete option is available, and any attempt to edit or delete it is rejected.
6. **Given** a user submits an empty or too-long comment, **When** they post it, **Then** it is
   rejected with a clear message.

---

### User Story 5 - See other users' changes live (Priority: P3)

While a team member has a board, a task, or the project list open, changes made by other users
(new projects, new or edited tasks, moves, assignments, comments) appear on their screen without
a refresh.

**Why this priority**: The team can work on the same board at the same time without reloading,
but every change is still visible after a refresh without this story.

**Independent Test**: Open the same board in two browsers as different users, move, create, edit,
and comment on a task in one, and confirm the other shows each change within 2 seconds without a
refresh.

**Acceptance Scenarios**:

1. **Given** two users are viewing the same board, **When** one moves, creates, edits, or
   reassigns a task, **Then** the other user's board shows the change within 2 seconds without a
   refresh.
2. **Given** a user has a task's details open, **When** another user comments on, edits, or moves
   that task, **Then** the details (comments, fields, status history) update within 2 seconds.
3. **Given** a user is on the project list, **When** another user creates a project, **Then** it
   appears in the list within 2 seconds.
4. **Given** live updates are temporarily unavailable, **When** a user makes a change, **Then** the
   change is still saved, and other users see it once live updates resume or when they refresh.

---

### User Story 6 - Receive in-app notifications (Priority: P3)

A team member is told inside Taskify when another user assigns them a task, moves a task that is
assigned to them, or comments on a task that is assigned to them. They see how many notifications
are unread, can open the list, follow a notification to its task, and mark notifications read.

**Why this priority**: Notifications help people notice work that concerns them, but the board
and task details already show the same information.

**Independent Test**: As one user, assign a task to a second user, move it, and comment on it;
switch to the second user and confirm three unread notifications that each link to the task; mark
all read and confirm the unread count is zero. Then act on a task assigned to yourself and confirm
no notification is created.

**Acceptance Scenarios**:

1. **Given** user A assigns a task to user B, **When** B views Taskify, **Then** B has an unread
   notification naming A, the task, and the action.
2. **Given** a task is assigned to user B, **When** another user moves it or comments on it,
   **Then** B receives a notification for each such action.
3. **Given** a user acts on a task assigned to themselves, **When** the change is saved, **Then**
   no notification is created for them.
4. **Given** a user has unread notifications, **When** they are viewing any page, **Then** the
   unread count is shown and updates within 2 seconds when a new notification arrives.
5. **Given** a user opens a notification, **When** they select it, **Then** they are taken to the
   task and the notification is marked read; they can also mark all notifications read at once.
6. **Given** a user tries to read or mark a notification that belongs to another user, **When**
   the request is made, **Then** it is rejected as if the notification does not exist.

---

### Edge Cases

- A project with no tasks shows all four columns empty, with a prompt to add the first task.
- Two users move the same task, or edit its title, description, or assignee, at nearly the same
  time: the last save wins, and both users' screens update to the final values within 2 seconds
  (FR-025).
- A user tries to open a project or task that no longer exists (for example, a stale link): they
  see a "not found" message and can return to the project list.
- Input containing markup or script content (for example, `<script>` in a task title) is shown as
  plain text and never executed.
- Input with leading or trailing whitespace only (for example, a title of spaces) is treated as
  empty and rejected.
- A request that names an assignee, column, or user that is not one of the predefined values is
  rejected without changing data.
- A task in Done can still be edited, reassigned, and commented on exactly like a task in any
  other column; no column is locked.
- Two projects with the same name are allowed; they are distinguished in the list by creation date.
- If live updates are interrupted (for example, a network blip), the screen catches up with all
  saved changes once the connection returns; no change is lost.
- A user is never notified about their own actions, and the same change never creates two
  notifications for the same person.
- Notifications older than 30 days are removed automatically.

## Requirements *(mandatory)*

### Functional Requirements

**Users and identity**

- **FR-001**: System MUST provide exactly five predefined users: one with the role Product Manager
  and four with the role Engineer.
- **FR-002**: System MUST let a person choose which predefined user to act as from a selection
  screen, without a password, and MUST let them switch user at any time.
- **FR-003**: System MUST NOT allow users to be created, renamed, or deleted in this phase.
- **FR-004**: System MUST record the selected user as the author of every project, task, comment,
  and task move made while that user is selected.

**Projects**

- **FR-005**: System MUST provide three sample projects, each with sample tasks spread across all
  four columns and assigned to a mix of users, available the first time Taskify is opened.
- **FR-006**: Any user MUST be able to create a project with a name of 1–100 characters (after
  trimming whitespace) and an optional description of up to 1,000 characters.
- **FR-007**: System MUST show all projects to all users.

**Tasks and board**

- **FR-008**: Each project board MUST show exactly four columns, in order: To Do, In Progress,
  In Review, Done.
- **FR-009**: Any user MUST be able to create a task in a project with a title of 1–200 characters
  (after trimming whitespace) and an optional description of up to 5,000 characters; new tasks
  start in To Do.
- **FR-010**: A task MUST be assigned to at most one predefined user or be unassigned.
- **FR-011**: Any user MUST be able to edit any task's title and description, and MUST be able to
  assign it to any predefined user, reassign it, or unassign it. When edits or moves to the same
  task are saved at nearly the same time, the last save wins.
- **FR-012**: Any user MUST be able to move any task from any column to any other column,
  regardless of who created or is assigned to the task.
- **FR-013**: System MUST visually distinguish tasks assigned to the selected user from all other
  tasks on the board.
- **FR-014**: Each task card on the board MUST show at least its title and assignee name (or
  "Unassigned").
- **FR-023**: System MUST record a status history entry each time a task changes column
  (user who moved it, from column, to column, time) and MUST show the history in the task
  details, newest first, to all users. History entries MUST NOT be editable or deletable.

**Comments**

- **FR-015**: Any user MUST be able to add a comment of 1–2,000 characters (after trimming
  whitespace) to any task.
- **FR-016**: System MUST show each comment's author and posting time, ordered oldest first, and
  MUST indicate when a comment has been edited.
- **FR-017**: System MUST allow a user to edit or delete only the comments they authored, and MUST
  reject such requests for comments authored by others.
- **FR-024**: When a comment is deleted, System MUST permanently remove its text and MUST keep a
  placeholder in its place in the thread reading "Comment deleted by [author]" with the deletion
  time. Deleted comments MUST NOT be editable or restorable.

**Live updates and notifications**

- **FR-025**: System MUST show changes made by other users (projects created; tasks created,
  edited, assigned, or moved; comments added, edited, or deleted) on any open project list, board,
  or task details screen within 2 seconds, without a refresh.
- **FR-026**: If live updates are interrupted, System MUST still save every change and MUST bring
  open screens up to date once live updates resume; a refresh MUST always show the saved state.
- **FR-027**: System MUST create an in-app notification for a user when another user (a) assigns
  a task to them, including at task creation, (b) moves a task currently assigned to them, or
  (c) comments on a task currently assigned to them. No other action creates a notification, and
  a user's own actions never notify them.
- **FR-028**: Each notification MUST show who acted, on which task, and what they did, and MUST
  link to the task. Users MUST see their unread count, list their notifications newest first,
  and mark one or all as read.
- **FR-029**: A user MUST only be able to see and change their own notifications.
- **FR-030**: System MUST delete notifications 30 days after they are created.

**Data, validation, and security**

- **FR-018**: All projects, tasks, assignments, column positions, and comments MUST be saved and
  remain available after Taskify is closed and reopened.
- **FR-019**: System MUST validate every input against the limits and allowed values in this
  specification on the receiving side, reject invalid input as a whole with a clear message that
  does not reveal internal details, and leave existing data unchanged. All length limits in this
  specification count user-perceived characters: each visible character, including an emoji, a
  combined accented letter, or a flag, counts as one, and the same count is shown to the user
  while typing.
- **FR-020**: System MUST display all user-entered text as plain text so that embedded markup or
  scripts are never executed.
- **FR-021**: System MUST reject any reference to a user, project, task, comment, or column that
  does not exist.
- **FR-022**: System MUST log rejected requests and changes made to data (who, what, when) for
  audit, without recording comment or description content in the log.
- **FR-031**: System MUST limit each user to 60 changes per minute (creating, editing, moving,
  assigning, commenting, marking notifications read) and 300 reads per minute. A request over
  the limit MUST be rejected with a "Too many requests, please wait a moment" message, MUST NOT
  change any data, and MUST be logged for audit.
- **FR-032**: System MUST record in the audit log every time a person selects or switches the
  user they act as, with the user chosen, the previous user (if any), the time, and the source IP
  address. This is the phase 1 stand-in for login auditing.

### Key Entities

- **User**: A predefined team member. Attributes: display name, role (Product Manager or Engineer).
  Fixed set of five in this phase.
- **Project**: A container for related tasks. Attributes: name, optional description, creator,
  creation date. Has many tasks.
- **Task**: A unit of work within one project. Attributes: title, optional description, column
  (To Do, In Progress, In Review, Done), optional assignee (a User), creator, creation and
  last-updated dates. Has many comments and many status changes.
- **Status Change**: A record of one column move on a task. Attributes: task, user who moved it,
  from column, to column, time. Read-only once recorded.
- **Comment**: A message on a task. Attributes: text, author (a User), posted time, edited time
  (if edited), deleted time (if deleted; text is cleared on deletion).
- **Notification**: A message to one user about another user's action on a task. Attributes:
  recipient (a User), type (assigned, moved, or commented), task, project, acting user, summary,
  created time, read time (if read). Removed 30 days after creation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user can select a user, open a project, and find the tasks assigned to
  them in under 30 seconds without instructions.
- **SC-002**: A user can move a task to a different column in a single action, and the new column
  is shown within 1 second.
- **SC-003**: A user can create a new task and assign it in under 1 minute.
- **SC-004**: 100% of changes (projects, tasks, moves, comments) are still present after Taskify
  is closed and reopened.
- **SC-005**: 100% of invalid inputs in a test set covering each validation rule are rejected with
  no change to stored data.
- **SC-006**: 0 instances of user-entered markup or script being executed in a test set of common
  injection inputs.
- **SC-007**: Boards with up to 200 tasks open and become usable within 2 seconds.
- **SC-008**: In a test with two users viewing the same board, 100% of changes made by one user
  appear on the other's screen within 2 seconds without a refresh.
- **SC-009**: In a test set covering each notification rule, 100% of qualifying actions create
  exactly one notification for the right user, and 0 notifications are created for a user's own
  actions.
- **SC-010**: In a test, the 61st change and the 301st read by one user within a minute are
  rejected with no data changed, while normal use (at least 1 change per second, sustained for a
  minute) is never rejected.

## Assumptions

- **No login in phase 1**: Anyone who can reach Taskify can act as any predefined user. Phase 1
  is therefore intended only for a trusted internal environment (for example, a demo or internal
  network) and MUST NOT be exposed publicly. Real authentication is planned for a later phase, and
  this spec does not satisfy the constitution's authentication requirement on its own; the plan
  must record this as a justified, time-limited deviation.
- The five users have fixed sample names chosen during planning (for example, a Product Manager
  and four Engineers with distinct names).
- All users have the same permissions in this phase; the Product Manager role is a label only and
  grants no extra rights.
- Deleting projects and tasks is out of scope for phase 1; tasks that are no longer needed are
  moved to Done.
- Ordering tasks within a column is out of scope; tasks in a column are shown newest first.
- Taskify is used in a desktop web browser; mobile layouts, email or push notifications (only
  in-app notifications are in scope), attachments, due dates, labels, and search are out of scope
  for phase 1.
