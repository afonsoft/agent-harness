# SPEC-20261014-chat-git-bar-overview: Git control bar + conversation overview + PR surface

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat git bar (repo/branch/pull/push/PR) + overview peek + PR hover card |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Server + Application + Blazor |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261014-chat-git-bar-overview` |
| Status | `Draft` |
| Depends on | SPEC-20261011-chat-workspace-panel (workspace resolution), SPEC-20261004-promote-leg-pr (promote→PR plumbing) |
| Reference | OpenHands `git-control-bar` (repo/branch/pull/push/PR chips); Devin PR hover cards + overview; OpenHands conversation overview panel |

## 1. Executive Summary

### Problem

The conversation's git/workspace state is a one-line context string. Doing
anything with it (see branch, push, open a PR) means leaving the chat for
Cockpit/delegation pages. OpenHands puts a git control bar under the
composer; Devin shows PR cards inline.

### Solution

1. **Git bar** under the chat header: `repo · branch` chips (click →
   overview), `Pull`, `Push`, `Create PR` buttons operating on the resolved
   workspace/worktree (same resolution as RF-003 of workspace-panel spec).
2. **Create PR in chat**: `POST /api/chat/conversations/{id}/pull-request`
   reuses the promote→PR plumbing (`git config remote.origin.url` read —
   never `git remote get-url`), base = repo default branch, title/body from
   the conversation's latest run summary; result renders as a PR card
   message in-thread.
3. **PR hover card**: messages containing a `github.com/.../pull/N` link get
   a hover card (title, state, checks rollup) via `GET
   /api/chat/pr-card?url=` (Octokit, cached 60s).
4. **Overview peek**: info button opens a right-side peek (not a tab —
   lightweight) listing workspace path, branch/dirty, provider+model,
   permission preset, skills and MCP servers currently visible to the
   conversation.

### Scope

In scope: the above on `/ai-chat`. Out of scope: merge/rebase actions,
conflict resolution UI, GitHub auth changes, PR create for delegation runs
(existing `runs/{id}/create-pr` covers those).

## 2. Requisitos

### Funcionais

- RF-001 `ChatGitBar.razor` under the header when a conversation is open:
  `repo` chip (link), `branch` chip (current or `detached`), `dirty` dot,
  actions Pull / Push / **Create PR** (primary).
- RF-002 `POST /api/chat/conversations/{id}/git/pull` and `/git/push` —
  execute on the resolved workspace; refuse outside a git repo (400
  `not-a-repo`); stream output into a collapsible log in the bar popover;
  blocked while a run is `running` on the same workspace.
- RF-003 `POST /api/chat/conversations/{id}/pull-request {title?, body?}`:
  commits pending worktree changes **only when the workspace is a run
  worktree** (user edits in a plain workspace are never auto-committed —
  409 `uncommitted-changes` listing files); pushes head branch; creates PR
  via Octokit; returns `{number, url}`; renders a `pr-card` in-chat.
- RF-004 `GET /api/chat/pr-card?url=` → `{title, state, merged,
  checks: {total, succeeded, failed}, author}`; 60s cache; failures render
  plain link.
- RF-005 Hover card: on message links to `github.com/*/pull/*`, popover
  with the card (title/status/checks); `open` → new tab; `copy` → URL.
- RF-006 Overview peek `ConversationOverview.razor`: workspace path (+copy),
  repo/branch/dirty, provider + model + agent CLI, preset, plan-mode state,
  visible skills count, MCP servers (name+status) from capability catalog;
  opened via ⓘ button, closes on outside click/tab open.
- RF-007 All actions confirm in place (Pull/Push inline result, PR → card);
  nothing navigates away from the chat.

### Não-funcionais

- Git ops use `WithoutHarnessEnv` + the resolved workspace dir; never the
  server cwd.
- PR creation is idempotent per conversation head (existing open PR for the
  same head/base returns `{existing:true,url}`).
- pr-card endpoint: no secrets in cache key; rate-limited politely via the
  60s cache.

## 3. Arquitetura

```mermaid
flowchart LR
  Bar[ChatGitBar] --> WS[ConversationWorkspaceService.resolve]
  Bar --> Pull[POST git/pull|push → run git on workdir]
  Bar --> PR[POST pull-request → promote worktree → Octokit]
  Msg[PR links in messages] --> Card[GET pr-card → hover popover]
  Info[ⓘ] --> Peek[ConversationOverview: catalog+git+preset]
```

- Reuse `GitRemoteSlug` + remote-url resolution + `ExistingWorktreeRunId`
  conventions from the delegation promote path; extract the smallest shared
  `IPromoteToPrService` if the current code is delegation-scoped.
- Workspace resolution is the same service added by
  SPEC-20261011-chat-workspace-panel — no second resolver.

## 4. Fases

- **P1** — git bar + pull/push + overview peek.
- **P2** — create-PR endpoint + pr-card endpoint + hover card + in-chat PR
  message.

## 5. Testes

- Unit: workspace resolution reuse; pull/push refusal outside repo and
  during active run; PR idempotency (existing open PR → existing:true);
  uncommitted-changes guard (plain workspace never auto-commits).
- Integration: conversation + worktree → create PR → url + message card;
  hover endpoint returns checks rollup (mock Octokit via existing fakes).
- UI text guards: bar chips render; hover popover; overview panel fields.

## 6. Open questions

1. Auto-suggest PR title/body from the run? Proposed: draft = last
   assistant summary; user edits in a small dialog before create.
2. Show behind/ahead counts on the branch chip? Proposed: yes (`↑n ↓n`)
   when cheap to compute (`git status -sb`).
