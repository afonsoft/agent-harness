# SPEC-20261004-chat-board-tools — Chat tools for Board / GitHub issue management

**Status**: In implementation
**Depends on**: SPEC-20261001-chat-capability-registry, SPEC-20261001-chat-agent-delegation (tool registry + reporter), SPEC-20261004-promote-leg-pr (`GitRemoteSlug`).

## Context

The AI Code chat can already run agents, delegate tasks, use skills, touch the
filesystem and call MCP servers — but it cannot drive the **Board**. The Board
is a view over GitHub issues: cards are issues, columns are label-backed
(`GitHubBoardColumn` → `ToLabel()`), and every mutation already exists in
`IGitHubService` (Octokit, `GITHUB_TOKEN`). Today a user must leave the chat and
click through `/board` to create a card, move it between columns, label it,
comment, or kick off delegation for it.

This spec adds a `board_*` tool family so the assistant can work the board
directly: create issues (board + GitHub are the same object), move cards,
update fields, comment, close, and delegate an issue to an agent — the full
"card → work" loop from the chat.

## Goals

- Register board tools as first-class builtins (`Kind = BuiltinTool`) in the
  `IChatTool` registry (`Program.cs` list) — they appear in Settings toggles,
  tool definitions, and the activity reporter like any other builtin.
- Cover the whole card lifecycle with thin wrappers over `IGitHubService`;
  no new GitHub surface, no duplicated client logic.
- One chained "delegate this issue" tool that goes straight from card number
  to a running agent task.

## Non-goals

- Local-only cards (no GitHub repo) — the Board is GitHub-backed; issues
  without `owner/name` repos are out of scope.
- Projects v2 columns — columns here mean the label-backed
  `GitHubBoardColumn` model only.
- PR review/issue-search across multiple repos — single `repository`
  per call.
- Auto-delegation policies or schedules — `board_delegate_issue` is invoked
  by the model, confirmed by the human via the tool-call flow.

## Requirements

### RF-001 — `board_list_issues` (read)

`{ repository, column?, state? = "open" }` → compact array of
`{ number, title, column, labels, url, updatedAt }`. Keeps context small:
truncate to 50 issues, titles at 120 chars. Uses `GetIssuesAsync` +
`GitHubBoardGrouper` for column resolution.

### RF-002 — `board_get_issue` (read)

`{ repository, number }` → full issue body + last 5 comments.

### RF-003 — `board_create_issue` (write, `RequiresConfirmation = true`)

`{ repository, title, body?, column? = "todo", labels?[] }` →
`CreateIssueAsync` (already applies the column label, so the card lands on the
board). Returns `{ number, url, column }`. Because the Board *is* GitHub,
"create on the board" and "create on GitHub" are the same call — the tool
description says this explicitly so the model doesn't try to double-create.

### RF-004 — `board_move_card` (write, `RequiresConfirmation = true`)

`{ repository, number, column }` → resolves the issue's current
`GitHubBoardColumn` from its labels, then `UpdateIssueColumnAsync(old, new)`.
Rejects non-settable columns (`HasLabel() == false` — only `archived` is
derived) with a clear error naming the allowed set:
`backlog|todo|in-progress|in-review|in-pullrequest|blocked|done|canceled`.

### RF-005 — `board_update_issue` (write, `RequiresConfirmation = true`)

`{ repository, number, title?, body? }` → `UpdateIssueAsync`. At least
one field required.

### RF-006 — `board_set_labels` (write, `RequiresConfirmation = true`)

`{ repository, number, add?[], remove?[] }` → `AddLabelsToIssueAsync`
for `add`; per-label `RemoveFromIssue` for `remove` (needs a small
`RemoveLabelsFromIssueAsync` on `IGitHubService` — additive). Column labels in
`add` are rejected — use `board_move_card` (prevents two column labels).

### RF-007 — `board_close_issue` / `board_comment` (write)

`board_close_issue { repository, number, reason? }` → close + optional
reason comment; moves card to `done`. `board_comment { repository,
number, body }` → posts a comment. Both `RequiresConfirmation = true` for close;
comment is write but low-risk → `RequiresConfirmation = true` too, consistent
with other GitHub writes.

### RF-008 — `board_set_priority` (write)

`{ repository, number, priority }` → existing
`PUT /github/repos/{o}/{r}/issues/{n}/priority` semantics (`priority:
critical|high|medium|low` label convention).

### RF-009 — `board_delegate_issue` (write, `RequiresConfirmation = true`)

`{ repository, number, cli?, prompt?, repositoryPath? }` — the chained
"card → agent" tool:

1. Seeds a delegation task from the issue (`POST local/delegation/tasks/from-issue`
   semantics — reuse `IDelegationTaskService`, not HTTP).
2. Moves the card to `in-progress` (`board_move_card` path).
3. Starts the run via `delegate_task` semantics (`IAgentOrchestrationService`),
   returning `{ taskId, issueNumber, url, column }`.

`cli` defaults to the user's configured default CLI; `prompt` defaults to the
issue title+body. Failures after step 1 report the taskId so the human can
recover.

### RF-010 — Prompt/registry surfacing

- Tool `Description`s spell out the board model ("cards are GitHub issues;
  columns are labels") so the model routes board intents here instead of
  `shell_exec`-ing `gh`.
- `repository` validated as `owner/name` (`GitRemoteSlug.TryParse`
  shape); invalid input returns a `ChatToolResult` error, not an exception.
- Every tool reports progress through `IChatActivityReporter`
  (`running_tool` phase, label = `board_<verb>`).

## Design notes

- New files under `src/Taskboard.Integrations/Chat/Tools/Board/` —
  `BoardIssueTools.cs` (list/get/create/update/comment/close/labels/priority),
  `BoardMoveCardTool.cs`, `BoardDelegateIssueTool.cs`; column parsing helper
  shared (`ParseColumn` → `GitHubBoardColumn?`).
- One additive method on `IGitHubService` (`RemoveLabelsFromIssueAsync`) for
  RF-006 `remove`. Everything else uses existing service surface — no Octokit
  calls inside the tools.
- Registration: append to the `IChatTool[]` list in `Program.cs`; no changes
  to `ChatCapabilityRegistry` needed (toggle id `tool:board_*` is automatic).
- `RequiresConfirmation = true` on every mutating tool (RF-003..009); reads
  stay ungated, matching `web_search`/`fetch_url`.

## Acceptance criteria

- "cria uma issue no board com título X" → `board_create_issue` → card visible
  in the `todo` column and on GitHub.
- "move a #42 para review" → `board_move_card` → label swap, Board re-render
  shows the new column.
- "delega a issue #42 pro opencode" → `board_delegate_issue` → task running,
  card in `in-progress`.
- Toggling `tool:board_*` off in Settings removes the tool from the run's
  definitions.
