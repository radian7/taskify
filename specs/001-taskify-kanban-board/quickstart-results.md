# Quickstart results (T151)

Run on 2026-10-06, branch `main`, Windows, Podman. "Evidence" names the automated test that covers a scenario. No scenario
was exercised by hand in a browser; where only a test stands in for the manual steps this is stated.

## Automated validation

| Step | Result |
|---|---|
| `dotnet build -warnaserror` (via `verify.ps1`) | OK, no warnings |
| Unit tests | 448/448 |
| bUnit (Web.Tests) | 93/93 |
| Integration, whole suite | 281/282. The one failure was `EventsContractTests.A_move_made_while_the_Notifications_API_is_stopped_is_delivered_after_it_restarts_FR_026` (timed out waiting for the signal). |
| Integration, `*EventsContractTests` alone | failed once, then 17/17 on the next run. The FR-026 test is intermittent even on its own (2 failures in 3 runs). See Defects. |
| E2E (Playwright) | 12/12 |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any project |
| `npx @asyncapi/cli validate .../events.asyncapi.yaml` | valid |

## Manual scenarios 1-20

None of these was run by hand. "Covered" means an automated test exercises the same behaviour; it is not a claim of manual verification.

| # | Outcome | Evidence |
|---|---|---|
| 1 | Covered | bUnit `UserSelectTests` (five users with roles, no password); `UserSelectionTests.The_start_screen_lists_the_five_users_with_roles_and_asks_for_no_password` |
| 2 | Covered | bUnit `BoardTests` (four columns in order, title and assignee, highlight with text label); E2E `A_user_picks_themselves_sees_the_projects_and_a_board_with_their_own_cards_marked` |
| 3 | Covered | E2E `Dragging_a_card_to_another_column_moves_it_within_a_second_and_it_stays_after_a_reload` |
| 4 | Covered | E2E `A_move_made_in_one_browser_shows_in_a_second_browser_within_two_seconds`; integration `A_task_move_in_the_Tasks_API_reaches_a_client_in_the_project_group_within_2_seconds` |
| 5 | Covered | E2E `The_keyboard_menu_moves_a_card_the_same_way_dragging_does_and_the_history_records_it`; bUnit `MoveTests` |
| 6 | Covered | E2E `Dropping_a_card_in_its_own_column_changes_nothing_and_adds_no_history`; bUnit `Dropping_a_card_back_in_its_own_column_changes_nothing_and_sends_nothing` |
| 7 | Covered | bUnit `Status_history_shows_newest_first_who_moved_it_from_where_to_where_and_when`; the E2E keyboard test checks the history entry |
| 8 | Covered | E2E `A_user_creates_a_project_adds_and_assigns_tasks_edits_one_and_everyone_sees_it`; `CreateEditContractTests` |
| 9 | Covered in parts (API plus component, not the full UI path) | `NotificationsContractTests.The_three_actions_of_the_US6_independent_test_through_the_real_APIs_produce_exactly_three_notifications`; bUnit `NotificationBellTests` (badge count) |
| 10 | Covered in parts | `CommentsContractTests` (comment on another user's task) and `NotificationsContractTests` (notification on `CommentAdded`); bUnit `Edit_and_delete_buttons_appear_only_on_the_current_users_live_comments` |
| 11 | Covered | `CommentsContractTests.The_author_can_edit_...`, `Deleting_leaves_a_placeholder_...`; bUnit `An_edited_comment_has_an_edited_indicator_and_others_do_not`, `A_deleted_comment_shows_a_placeholder_with_the_deletion_time` |
| 12 | Covered | E2E `Markup_in_a_title_is_shown_as_text_and_never_runs`; bUnit `Task_titles_are_shown_as_plain_text_never_as_markup`; `InjectionTests` |
| 13 | Covered | E2E `Invalid_input_is_rejected_with_a_clear_message_and_nothing_is_created`; `CreateEditContractTests` and `CommentsContractTests` (invalid bodies, nothing saved) |
| 14 | Covered | `CreateEditContractTests.Editing_replaces_title_and_description_and_a_task_in_done_is_edited_like_any_other`; `CommentsContractTests.Any_user_can_comment_on_a_task_in_any_column_including_done`; bUnit `A_task_in_the_done_column_can_be_edited_like_any_other` |
| 15 | Covered | `PersistenceTests.A_project_task_move_and_comment_survive_a_restart_with_the_same_data_volume` (own throwaway volume via `Taskify:DataVolumeName`) |
| 16 | Partly covered | `CreateEditContractTests.Two_edits_at_the_same_time_both_succeed_and_the_last_one_wins_as_a_whole_not_a_mix` proves last-write-wins at the API. The two-browser "both show the final title within 2 s" part is covered only piecewise (bUnit `A_task_signal_refreshes_the_page_but_keeps_what_the_user_typed_in_the_open_edit_form`). Not verified end to end (manual). |
| 17 | Covered | `CreateEditContractTests.A_title_of_two_hundred_emoji_is_accepted_and_two_hundred_and_one_is_not`; bUnit `The_counter_counts_what_the_user_sees_so_two_hundred_emoji_fit_and_two_hundred_and_one_do_not` |
| 18 | Audit entry covered; the Aspire dashboard view is not verified (manual) | `AuditLogTests.Choosing_and_switching_users_is_audited_with_the_previous_user_and_the_ip`; `UserSelectionTests.Choosing_a_user_is_audited_with_the_user_and_the_source_ip`; no task text in logs: `AuditLogTests.Changing_a_deleted_comment_is_a_409_audit_and_the_text_stays_out_of_every_log`, `CommentsContractTests.A_refused_attempt_is_audited_with_the_ids_but_the_comment_text_is_never_logged` |
| 19 | Covered | `NotificationsContractTests.The_list_has_only_the_acting_users_notifications_newest_first`, `Another_users_notification_and_an_unknown_ID_both_give_the_same_404_and_change_nothing_FR_029`, `Read_all_gives_204_and_marks_only_the_acting_users_notifications` |
| 20 | Covered at API level, intermittent | `EventsContractTests.A_move_made_while_the_Notifications_API_is_stopped_is_delivered_after_it_restarts_FR_026` (move saved while stopped, signal after restart); bUnit `RealtimeTests` resync tests. See Defects. |

## API smoke checks

| Check | Evidence | Result |
|---|---|---|
| Non-author edit gives 403 | `CommentsContractTests.Another_user_cannot_edit_the_comment_and_gets_403_with_the_text_unchanged` | covered, passed |
| Unknown `assigneeUserId` gives 422 | `CreateEditContractTests.A_task_in_an_unknown_project_or_with_an_unknown_assignee_is_422_and_nothing_is_saved` | covered, passed |
| No `X-Api-Key` gives 401; unknown user gives 400 | `CrossCuttingTests.A_request_without_an_api_key_gets_401`; `AuditLogTests.A_missing_acting_user_is_a_400_audit_with_the_caller` (a missing user; an unknown user value was not re-checked live) | covered, passed |
| `POST /internal/events` with the Web key gives 403 | `EventsContractTests.The_Web_key_gets_403` | covered, passed |
| `GET /api/users` without `X-Taskify-User` gives 200 | `CrossCuttingTests.The_user_directory_needs_no_acting_user_for_any_allowed_caller` | covered, passed |
| Tasks API calls `GET /api/projects/{id}` with the Tasks key | used by every task-creation test through `ProjectsApiClient`; no test asserts the call directly | indirect only |
| Another user's notification read gives 404 | `NotificationsContractTests.Another_users_notification_and_an_unknown_ID_both_give_the_same_404_and_change_nothing_FR_029` | covered, passed |
| 61st write in a minute gives 429 with `Retry-After`, nothing saved | `RateLimitTests.The_61st_write_in_a_minute_is_429_with_Retry_After_nothing_is_saved_and_the_rejection_is_audited` | covered, passed |
| Plain `http://` refused | `TlsTests.A_plain_http_request_to_an_api_port_is_refused`, `No_service_has_a_plain_http_endpoint` | covered, passed |
| Title over 3,200 UTF-16 units gives 400; body over 1 MB gives 413 | 413: `CreateEditContractTests.A_request_body_over_one_megabyte_gets_413_and_an_unknown_field_gets_400`, `AuditLogTests.An_oversized_body_is_a_413_audit`. The 3,200 guard: unit tests `TextLengthTests` and `EventValidatorTests`; no live HTTP test of that limit was found | 413 covered; 3,200 at unit level only |

## Defects and observations

- `EventsContractTests.A_move_made_while_the_Notifications_API_is_stopped_is_delivered_after_it_restarts_FR_026` failed in
  2 of 3 runs here (timeout waiting for `BoardChanged` after the restart), including when the class ran alone. That is
  more than the known race in the whole suite. Either the test or the outbox retry timing after a restart (backoff up to
  1 minute) may be at fault. Not investigated, since this unit makes no code changes. It bears on scenario 20.
- Gaps rather than defects: no browser-level test for scenarios 9 and 10, scenario 16's two-browser case, scenario 18's
  Aspire dashboard view, or a direct assertion of the Tasks-to-Projects `200`.
