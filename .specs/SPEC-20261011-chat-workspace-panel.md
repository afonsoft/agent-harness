# SPEC-20261011-chat-workspace-panel: Conversation workspace panel (right-side tabs)

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Conversation workspace — right-side tabbed panel on `/ai-chat` |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Server (endpoints + hub) + Blazor WASM (components) + Application (services) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261011-chat-workspace-panel` |
| Status | `Draft` |
| Depends on | SPEC-20261005-chat-background-resume (ChatRun detached), SPEC-20261005-chat-jobs-schedule-search (todos store is in place) |
| Reference | COMPARISON-devin-web-openhands-harness-chat.md G-L1/G-A3; Devin session tabs (Shell/IDE/Files/Terminal); OpenHands right panel (TaskList/Planner/Changes/VSCode/Terminal/Browser) |

## 1. Executive Summary

### Problem

The chat is single-column: the run's *artifacts* live elsewhere — diffs only
per tool-call, the todo list is invisible, PTY only exists for legacy
`terminal` threads, and the real terminal/editor/diff viewers sit on other
pages (`/terminal`, `/editor`, Cockpit). The user cannot *watch* the agent
work like in Devin/OpenHands.

### Solution

Add a **workspace panel**: a right-hand split pane on `/ai-chat` (bottom
sheet on mobile) with tabs bound to the active conversation and its latest
run, all executing against the host workspace/worktree:

- **Tasks** — render `ChatTodoStore` items (the `todo` tool state) live.
- **Changes** — unified diff of the conversation workspace vs its base
  (worktree diff via `IWorkspaceIsolationService.GetDiffAsync` when the run
  has a worktree; `git status/diff` of `Conversation.WorkspacePath`
  otherwise), reusing `GitDiffViewer`.
- **Terminal** — interactive PTY rooted at the conversation workspace via
  `TerminalHub` (same transport as `PtyThreadPane`, new session key
  `conv-<conversationId>`).
- **Editor** — existing code-server iframe pointed at the workspace path.
- **Plan** — latest `plan-review` artifact (markdown) of the conversation.

Panel state (open/closed, selected tab, width) persists per conversation in
localStorage; tabs are pinnable (hidden tabs reachable via `…` menu).

### Scope

In scope: split layout + tabs above, read endpoints, PTY binding, empty and
no-run states, mobile sheet. Out of scope: Preview tab (SPEC-…-chat-preview-
panel), Browser tab (SPEC-…-chat-browser-tool), files tree browser,
interactive browser, sandbox.

## 2. Requisitos

### Funcionais

- RF-001 `/ai-chat` renders `ConversationWorkspacePanel` beside
  `ProviderChat`'s message column on `lg+`; a drag handle resizes
  (30–70%, default 45); `<lg` the panel mounts as a bottom sheet.
- RF-002 Header shows tab strip: `Tasks · Changes · Terminal · Editor · Plan`
  + `…` overflow (unpin/pin, close panel). Active tab + open state persist
  in `localStorage` key `harness.chat.workspace.<conversationId>`.
- RF-003 `GET /api/chat/conversations/{id}/workspace` → `{path, repository,
  branch, dirty, worktreeRunId?}` — resolves the run worktree when the
  latest run of the conversation has one, else the conversation
  `WorkspacePath` (same fallback as ChatService:2680).
- RF-004 **Tasks** tab: `GET /api/chat/conversations/{id}/todos` → items
  from `ChatTodoStore`; poll on `chat.sync`/hub turn events + 5s interval
  while open; statuses `pending|in_progress|completed` with progress header
  (`n/m`).
- RF-005 **Changes** tab: `GET /api/chat/conversations/{id}/diff` →
  `WorkspaceDiffDto` (worktree path when applicable, else workspace `git
  diff` incl. untracked); rendered by `GitDiffViewer`; refresh button +
  auto-refresh on run completion event.
- RF-006 **Terminal** tab: `TerminalHub.OpenForConversation(conversationId)`
  → session `conv-<id>` PTY at the resolved workspace dir; reuses the
  `PtyThreadPane` xterm surface (extracted so both thread and conversation
  bindings share it); replay scrollback on re-attach; survives tab switches.
- RF-007 **Editor** tab: iframe `src="/vscode/?folder=<workspace>"` reusing
  `VscodeEditor`'s start/status flow; `!installed`/`Failed` states render the
  existing guidance block.
- RF-008 **Plan** tab: latest `ChatApproval` of kind `plan-review` for the
  conversation rendered as markdown (read-only), with the decision state.
- RF-009 Empty states: no conversation → panel hidden; conversation with no
  run/worktree → Changes shows workspace diff or "Sem alterações"; no todos
  → hint that the agent tracks work here; Plan without plan-review → hint.
- RF-010 Opening Terminal/Editor does **not** pause or block the run; all
  tabs are read-observe except Terminal (interactive PTY on the host — same
  permission model as `/terminal`).

### Não-funcionais

- No new top-level page; panel must not regress the composer/rail layouts
  (Blazor panes render-all rule — guard against a crash blanking chat).
- Diff endpoint caps output (file count/bytes) like `GetDiffAsync` callers.
- i18n keys en-US + pt-BR (+es) for all new literals.

## 3. Arquitetura

```mermaid
flowchart LR
  ProviderChat --> Panel[ConversationWorkspacePanel]
  Panel --> Tabs{tab}
  Tabs --> Tasks[ChatTasksTab → GET todos]
  Tabs --> Changes[ChatChangesTab → GET diff → GitDiffViewer]
  Tabs --> Term[ChatTerminalTab → TerminalHub.conv-<id>]
  Tabs --> Editor[ChatEditorTab → /vscode iframe]
  Tabs --> Plan[ChatPlanTab → last plan-review]
  ChatService --> WS[GET workspace → worktree|WorkspacePath]
```

- `ConversationWorkspaceService` (Application): resolves effective workspace
  (worktree of latest run with `use_worktree`, else
  `WorkspacePath`/`ResolveCardWorkdir`), git branch/dirty via
  `GitWorktreeManager`/`LibGit2` path already used by Compare.
- `TerminalHub`: add `OpenForConversation` alongside `OpenForThread`;
  `ThreadPtyResolver`-equivalent resolution for conversation workspace.
- Extract xterm host surface from `PtyThreadPane` into shared
  `PtyHostPane.razor` (hub method name + session key as parameters) — no
  duplicated transport code.

## 4. Fases

- **P1** — split layout + tab strip + persistence + Tasks tab + read endpoints.
- **P2** — Changes tab (`GitDiffViewer` reuse) + Plan tab.
- **P3** — Terminal tab (hub + shared `PtyHostPane`) + Editor iframe +
  mobile bottom sheet.

## 5. Testes

- Unit: workspace resolver (worktree run vs WorkspacePath vs card default);
  todos endpoint shape; diff endpoint worktree-vs-workspace selection.
- Integration: create conversation → write todos via tool → GET list; run
  with worktree → GET diff returns worktree files; OpenForConversation binds
  to resolved dir.
- Blazor/text guards: tab strip renders, persistence key format, panel
  hidden without conversation, mobile sheet class.
- a11y: tabs `role=tablist`, `aria-selected`, keyboard arrows; focus returns
  to chat on close.

## 6. Open questions

1. Show diff of *latest run only* or merged conversation worktree? Proposed:
   latest run's worktree when one exists, else workspace — simpler mental
   model (matches Devin "what the agent changed").
2. Should Terminal tab auto-open on first `shell_exec` call (Devin shows
   Shell as work happens)? Proposed: badge the tab with a dot when the run
   has shell activity; never steal focus.
