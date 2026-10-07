# COMPARISON — Devin Web × OpenHands × Harness Chat

- Status: `Draft` — analysis doc (requested by afonsoft, 2026-10-07)
- Author: devin
- Scope: what the Harness chat (`/ai-chat`, ProviderChat) needs to reach
  Devin-Web/OpenHands-level **layout and autonomy** — task execution, live
  workspace surfaces, run control — on the **host** (no sandbox; sandbox is a
  later phase, see §5).

## 1. Reference surfaces studied

| Source | What it is |
| --- | --- |
| Devin Web (app.devin.ai) | docs.devin.ai session tools + release notes (Oct 5 2026 session-workspace update) |
| OpenHands | local clone `~/repos/OpenHands` (release 1.10.0 / cloud 1.43.0), frontend `frontend/src`, SDK in `.venv`, docs.openhands.dev "Agent Canvas" |
| Harness | this repo @ `7c7b2c4` — `ProviderChat.razor`, `AiChat.razor`, `ChatService`, `IChatTool` set, run dispatcher, approvals |

## 2. How each product works

### Devin Web (app.devin.ai)

- **Session = chat + workspace.** Left: message stream. Right: a tabbed
  workspace bound to the session's machine — **Shell** (watch commands/logs),
  **IDE** (embedded VS Code, take-over capable), **Browser/Computer**
  (interactive live browser + desktop, used for CAPTCHA/MFA help),
  **Files**, **Terminal** (runs commands on Devin's machine; opening it or
  Files *wakes* a sleeping session), **Preview** (running app preview with an
  element picker that quotes the picked element back into chat).
- **Progress steps** in the chat link into the tools; PR links render hover
  cards even in the owning session.
- **Run model:** fully autonomous; messages queue while the agent works;
  sessions sleep and resume; `Ctrl+Shift+E` mark done; `⌘K` command palette;
  side chats; attachments; secrets; web-push style notifications.
- **Autonomy backbone:** each session owns a VM (sandbox) with desktop,
  browser, terminal and editor — agent and user share the same surfaces.

### OpenHands (Agent Canvas / v1)

- **Conversation = chat + workspace panel.** Sidebar lists conversations
  grouped by workspace with tag chips and automation-run filters. Inside a
  conversation: left chat panel (resizable 30–80%), right tabbed panel —
  **TaskList** (agent-maintained todo via `task_tracker` tool), **Planner**
  (plan document via `planning_file_editor`), **Changes** (unified uncommitted
  diff + commit history drawer), **VS Code**, **Terminal**, **Browser**
  (screenshot of the agent browser). Mobile: the right panel becomes a bottom
  sheet.
- **Agent controls:** agent state chip (`running / paused / stopped /
  finished / error / stuck / waiting_for_confirmation`), stop/pause buttons,
  git control bar (repo/branch/pull/push/create-PR), confirmation-mode banner.
- **Autonomy backbone:** SDK agent loop emits an `ActionEvent`/`ObservationEvent`
  stream over WebSocket; a **security analyzer** scores each action's risk
  (`LOW/MEDIUM/HIGH`) and **confirmation mode** gates actions accordingly;
  a **condenser** compacts long contexts; tools: terminal (tmux),
  file_editor, apply_patch, glob, grep, browser_use, delegate, task_tracker,
  planning_file_editor, workflow, skills (`invoke_skill`), think/finish.
- **Execution:** agent runs inside a backend workspace — local, Docker, VM,
  Modal or Cloud (sandbox per conversation).
- **Automations:** GitHub resolver (issue → PR), Slack resolver, schedules.

### Harness chat today (AS-IS)

- **Single-column chat** (`/ai-chat`): left rail of conversations (search +
  FTS hits, archive, fork count, active-run chip, context-pressure meter),
  message stream with tool cards / diff stats / inline images / deliverables,
  composer with icon-pill pickers (provider/model/agent/repo/mode) +
  attachments + slash palette.
- **Modes:** `Chat` (provider + tools — the "Ask") / `Agent` (chat delegates
  runs to CLIs). Context line shows repo/workspace (Devin-style).
- **Runs:** `POST messages` enqueues → `ChatRunDispatcherService` detached,
  FIFO per conversation, SSE attach `runs/{id}/stream`, resume after browser
  close, toast/browser/Web-Push notifiers, jobs strip + schedules panel.
- **Tools (~35):** shell exec, filesystem, workspace/worktree, fetch, web
  search, code interpreter, image gen, memory, skills, `todo` (OpenCode-style
  per-conversation task list), jobs, schedules, session search, delegation
  (`delegate_plan/task/fanout/status/compare`, mailbox, decide), run
  agent/CLI, sub-agent, board, MCP adapter — all behind the security gateway.
- **Control:** per-tool approval policies (`ask|never|allow`) + presets,
  plan-mode with `exit_plan_mode` + plan-review approval cards, fork/steering,
  condenser-ish context management.
- **Surfaces that exist but live OUTSIDE the chat:** `/terminal` (xterm tabs,
  host/docker exec), `/editor` (code-server iframe, proxied `/vscode/`),
  `GitDiffViewer` (Cockpit), `AgentRunTimeline` (Agents page), `PtyThreadPane`
  (in-chat, only for legacy "terminal" threads), `ChatTodoStore` (tool state,
  **no UI**), pause/resume endpoints (orchestration runs only — **not** chat
  runs).

## 3. Gap list (verdicts live in `gap-analysis-20261007.md`)

### LAYOUT — make `/ai-chat` a conversation workspace

- **G-L1 Right workspace panel** — split view: chat on the left, a tabbed
  panel on the right bound to the active conversation/run on the **host**:
  `Tasks` (todo store), `Changes` (worktree diff vs base, reuse
  `GitDiffViewer` + `GetDiffAsync`), `Terminal` (PTY bound to the run
  worktree, reuse `PtyThreadPane`/`TerminalHub`), `Editor` (embed code-server
  iframe on the run workspace), `Plan` (plan-mode artifact), `Timeline`
  (`AgentRunTimeline` scoped to the run). Desktop: resizable split; mobile:
  bottom sheet. *Mirrors Devin tabs and OpenHands right panel.*
- **G-L2 Preview tab + element picker** — iframe to a running app URL from
  the conversation (run launches it), with a pick-an-element overlay that
  quotes the element into the composer (Devin's Preview quote box).
- **G-L3 Browser/Computer tab** — live view of an agent-controlled browser.
  Phase 1: screenshots from a `browser_use` tool (Playwright). Phase 2:
  interactive (click-through). *Largest lift; defer the interactive part.*

### AUTONOMY — let it work longer, unsupervised-but-safe

- **G-A1 Chat-run pause/resume + explicit state chip** — expose
  paused/running/waiting-approval/stuck on the chat header; add cooperative
  `pause`/`resume` for `ChatRun` (exists only for orchestration runs today:
  `runs.MapPost("{id}/pause")` at Program.cs:1537). Surface **STUCK**
  (no-progress watchdog → badge + nudge action).
- **G-A2 Risk-tiered approvals** — keep `ask|never|allow` and add a
  classifier on each call (writes outside worktree, `rm -rf`, network, env
  mutation → HIGH; read-only → LOW). Preset `auto`: LOW auto-runs, MED runs
  with notice, HIGH asks. OpenHands' security analyzer does exactly this.
- **G-A3 `task_tracker` surface** — the `todo` tool + `ChatTodoStore` already
  exist; render the list in the `Tasks` tab (and a slim strip in the chat
  header). Zero backend work beyond a read endpoint.
- **G-A4 Git control bar in chat** — OpenHands-style chips bound to the run
  worktree: repo, branch, pull, push, **Create PR** (wire existing
  promote→PR), plus a PR hover card on PR links in messages (Devin parity).
- **G-A5 Conversation overview panel** — peek panel: workspace path, git
  state (branch/dirty), loaded skills/MCP servers, active policies, model —
  OpenHands' overview; we already show a context line.
- **G-A6 `browser_use` tool** — Playwright headless browse returning
  screenshot + DOM text; feeds the Browser tab and lets the agent verify UI
  it builds (Devin/OpenHands both have it; our "test the app" loop is manual).
- **G-A7 Suggested next actions** — post-run suggestion chips
  (OpenHands chat-suggestions / Devin suggested tasks).
- **G-A8 Voice input** — mic button in composer (Devin voice parity);
  `MediaRecorder` → transcribe → fill input. Low priority.
- **G-A9 `⌘K` command palette + session shortcuts** — Devin parity
  (mark done, new chat, jump tabs). We have a slash palette; global ⌘K is new.

### HOST EXECUTION (per afonsoft decision — sandbox deferred)

- Execution stays on the host: Terminal/PTY already run on host (or docker
  exec for containers). The safety boundary = approval policies + risk
  classifier (G-A2) + worktree jail (`GitWorktreeManager.ResolveInsideWorktree`)
  + security gateway — **not** VM isolation.
- `Changes`/`Terminal`/`Editor` tabs point at the run's worktree on the host;
  no sandbox provisioning, port-forwarding or runtime lifecycle is needed now.
- Later phase (tracked, not in scope): per-run sandbox runtime — Docker
  workspace root + bind-mount, port exposure for Preview, noVNC for Computer.

## 4. Proposed spec slices (draft on approval)

| Order | Spec | Covers |
| --- | --- | --- |
| 1 | `chat-workspace-panel` | G-L1 core: right panel + `Tasks` + `Changes` + `Terminal` tabs (reuses PtyThreadPane/TerminalHub, GitDiffViewer, ChatTodoStore) |
| 2 | `chat-run-controls` | G-A1 pause/resume + state chip + stuck watchdog |
| 3 | `chat-risk-approvals` | G-A2 risk classifier + `auto` preset |
| 4 | `chat-git-bar-overview` | G-A4 + G-A5 (worktree chips, promote→PR in chat, overview peek) |
| 5 | `chat-preview-panel` | G-L2 Preview tab + element picker |
| 6 | `chat-browser-tool` | G-A6 (+G-L3 screenshot-only Browser tab) |
| 7 | `chat-polish` | G-A7 suggestions, G-A9 palette, G-A8 voice (last) |

## 5. Explicit non-goals (this wave)

- Sandbox/VM isolation per run — deferred by owner decision to a later phase.
- Interactive (click-through) remote browser — screenshot-only first.
- Devin billing/ACU equivalents (FinOps page already covers costs).

## 6. Sources

- docs.devin.ai — "The Devin Interface", "devin-session-tools", release notes
  Oct 5 2026 (Terminal/Files tabs, Preview quote box, ⌘K, PR hover cards).
- docs.openhands.dev — Agent Canvas: overview, conversations (workspace
  groups, tags, overview panel, Commits drawer, Files drawer).
- OpenHands repo — `frontend/src/components/features/conversation/`
  (tab nav: planner/changes/vscode/terminal/browser/tasklist),
  `types/agent-state.tsx`, `openhands.sdk` (events, security analyzer,
  condenser), `openhands/tools/*` (terminal/tmux, file_editor, browser_use,
  task_tracker, delegate, workflow).
- This repo — `ProviderChat.razor`, `AiChat.razor`, `ChatService.cs`,
  `ChatCapabilityRegistry.cs`, `src/Taskboard.Integrations/Chat/Tools/*`,
  `Terminal.razor`, `VscodeEditor.razor`, `GitDiffViewer.razor`,
  `AgentRunTimeline.razor`, `ChatTodoStore`.
