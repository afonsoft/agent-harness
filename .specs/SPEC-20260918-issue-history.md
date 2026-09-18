# SPEC-20260918 — Issue History Timeline & Direct VS Code Deep Link

- **Status**: Done
- **Date**: 2026-09-18
- **PR**: (see merge commit on `main`)

## Context

The issue card dialog (`TaskDetailDialog`) showed agent logs but no unified
record of *what happened to the issue* — which agent ran it, when it moved
columns, when it was edited or closed. Separately, "Open in VS Code" navigated
to the `/editor` wrapper page inside the app; the user wants it to open the
editor directly in a new browser tab.

## Requirements

### RF-001 — Persisted board-side history events

Every mutation the board performs on a GitHub issue is persisted as an
`IssueHistoryEvent` (Domain entity, EF Core table `IssueHistoryEvents`):

| Mutation | Endpoint | Kind | Detail |
|---|---|---|---|
| Column move | `PUT repos/{o}/{r}/issues/{n}/column` | `ColumnMoved` | `From`/`To` labels |
| Edit | `PATCH repos/{o}/{r}/issues/{n}` | `Edited` | changed fields (`title`, `body`) |
| Close | `POST repos/{o}/{r}/issues/{n}/close` | `Closed` | resolution (`canceled`/`archived`) |

Events are keyed by `IssueId` (GitHub issue id — the same key `AgentRun`
uses) plus `Repository`. Recording is **best-effort**: a history write failure
never fails the mutation itself.

### RF-002 — Unified timeline endpoint

`GET /api/github/issues/{issueId}/history?take=` (auth required) merges:

- persisted `IssueHistoryEvent` rows, and
- `AgentRun` records for the same issue id,

into `IssueHistoryItemDto` items (`kind`, `occurredAt`, optional `agentType`,
`agentRunState`, `finishedAt`, `from`, `to`, `detail`), newest first, default
take 50.

### RF-003 — History tab in the issue dialog

`TaskDetailDialog` gains a **Histórico** tab (`IssueHistoryTab`) rendering the
timeline with per-kind icons, human-readable descriptions (agent name + run
state, `from → to` moves, edited fields, close resolution) and local-time
timestamps. Includes loading/empty/error states and a refresh button.

### RF-004 — Direct-to-editor deep link

`GET /api/vscode/open?repo=owner/name` (auth required) resolves the card
workdir via `WorkspaceService.ResolveCardWorkdir` and 302-redirects to
`/vscode/?folder=<path>` — skipping the `/editor` wrapper. Invalid
`owner/name` input → 404. The dialog's "Open in VS Code" link now points at
this endpoint with `target="_blank"`, so the editor opens in a new browser
tab under the same authenticated proxy gate.

## Non-goals

- Reconstructing history for events that happened before this feature shipped
  (the timeline starts recording from deploy onward; agent runs already
  persisted appear retroactively).
- GitHub-side events (comments, external label edits) — only board-side
  mutations and agent runs are tracked.

## Test plan

- `IssueHistoryEndpointsTests` (8 tests): 401 unauthenticated on both new
  endpoints; history list shape; `column-moved`/`edited`/`closed` events with
  correct `from`/`to`/`detail`; `vscode/open` 302 → `/vscode/?folder=...` and
  404 on malformed repo.
- Migration `AddIssueHistoryEvents` creates `IssueHistoryEvents` with index on
  `IssueId`; `OccurredAt` stored as unix-ms integer.
