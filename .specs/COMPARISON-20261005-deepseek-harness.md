# COMPARISON-20261005 — DeepSeek Harness (`dsh`) vs agent-harness chat

## 0. Metadata

| Field | Value |
|---|---|
| Type | Analysis / comparison report (feeds improvement specs) |
| Reference repo | [deepseek-ai/deepseek-harness](https://github.com/deepseek-ai/deepseek-harness) — TypeScript pnpm monorepo, `npx @deepseek-ai/dsh web` → Web UI |
| Compared against | agent-harness `ChatConversation`/`ChatMessage`/`ChatRun` pipeline (`/ai-chat`, `ProviderChat`) at commit `origin/main` after SPEC-20261005 P1–P3 |
| Author | Devin (session `devin-70fa492ae0ee46b9b0a70be63b30c875`) |

## 1. Executive summary

DeepSeek Harness is a **plugin-everything** harness: 56 package groups on a
vendored Cordis kernel, a single event-sourced `Session` log per agent, a
web/desktop/CLI client set, and a wide model-facing tool catalog. Its chat is
not a page bolted onto a task app — the whole product is the chat — so its
surface area is much larger than ours. The useful comparison is not "they have
more tools" but **which of their loop capabilities close real gaps in our
chat UX and correctness**.

What we already do well (parity or better):

- Detached server-side runs with re-attachable SSE (our SPEC-20261005 P1 is
  the same property their durable `Session` gives: browser close ≠ run death).
- Multi-channel `run.completed` notifications incl. Web Push (they notify via
  their own desktop/web only).
- A broad tool catalog: shell, file read/write/edit/search, tests, web
  search/fetch, image gen, memory, todo, skills, MCP adapter, plus a
  delegation suite (fanout/plan/coordinator/compare/inbox/send/decide) and
  worktree checkpoints that maps to their subagent/workflow area.
- Conversation archive, unified history surface, per-capability toggles,
  per-browser notification prefs, finops/token accounting per run.

Where they are ahead and worth adopting (in priority order):

| # | Capability | Their implementation | Our gap | Spec |
|---|---|---|---|---|
| 1 | **Tool approval seam + permission presets** | `ctx.approval` one-shot prompt; `ask`/`never` policy; closed outcome `allowed-once/rejected/cancelled/unavailable`, fail-closed; `approval/asked`+`approval/decided` audit events; permission presets per session | `RequiresConfirmation` is only a Settings hint — nothing gates execution | SPEC-…-chat-tool-approval |
| 2 | **Plan mode** | `/plan` toggles logged state; `plan:policy` prompt section; `exit_plan_mode` tool presents a markdown plan for review via user-questions seam; mutating tools stay denied until approved | none | SPEC-…-chat-plan-mode |
| 3 | **Conversation fork** | `ctx.sessions.create(seed)` replays a prefix into a new session; fork mid-turn keeps the original; `subagent_fork` = fork + inherited context | none (only retry = new run in same thread) | SPEC-…-chat-fork-steering |
| 4 | **Mid-turn steering** | `agent.steer()` injects a user message at the next step boundary of the open turn | queued send = next **turn**; no intra-turn steer | SPEC-…-chat-fork-steering |
| 5 | **Context compaction** | `agent/pre-step` pressure check → tool-result pruning → summary replaces old surface; `compaction/*` log events; survives resume | `BuildTranscriptAsync` sends the full history every call — unbounded growth until provider rejects | SPEC-…-chat-context-management |
| 6 | **Tool-output spill** | capped results spill to a store; the tool result carries a pointer the model can read back | tool output is inlined whole (and may be silently truncated) | SPEC-…-chat-context-management |
| 7 | **Scheduled follow-ups** | `schedule_{create,list,update,delete}` model tools → the session wakes itself later via cron/at/afterSeconds | none | SPEC-…-chat-jobs-schedule-search |
| 8 | **Background shell jobs** | `bash` with `run_in_background` + `job_{list,output,kill}`; live job stream UI | `shell_exec` is synchronous only; managed-jobs exist but are not model-facing | SPEC-…-chat-jobs-schedule-search |
| 9 | **Full-text session search** | `session-query` service + SQLite FTS: cross-corpus list, surface filters, `session_search`/`session_event_search` tools | sidebar search = title `LIKE` only | SPEC-…-chat-jobs-schedule-search |
| 10 | **Composer attachments** | `file-upload` client pkg, `attachment` group, `read_image`; commands declare `attachments` acceptance | composer is text-only; `ChatImageStore` holds generated images only | SPEC-…-chat-attachments-feedback |
| 11 | **Per-message feedback** | `feedback/message-put|delete` log-only events + `FeedbackCategory` taxonomy + edit (CAS token) | none | SPEC-…-chat-attachments-feedback |
| 12 | **Deliverables** | `present` tool declares files + `workspace-changes` git diff card per turn | delegation worktrees diff exists, plain-chat has no changed-files summary | SPEC-…-chat-attachments-feedback |

Deliberately **not** adopted:

- **Cordis-style plugin kernel** — our DI + `IChatTool` registry already gives
  tool/extension seams; a kernel rewrite buys nothing.
- **Event-sourced session log as storage** — our `ChatMessage`/`ChatRun` rows
  + SSE replay already deliver resume/fork-equivalent UX with EF queries; a
  log-sourcing rewrite is out of scope. We borrow the *capabilities*, not the
  storage model.
- **PTC (`run_code` programmatic tool calls)**, agent teams
  (`spawn_teammate`, `team_task_*`), ACP subagent backends, hooks bridges,
  SSH/sandbox providers, LSP tool, voice input, `ralph` loop — real but
  over-fit to their agent product; revisit only if demand appears.
- **Trajectory/virtualized event view** — our `AgentRunTimeline` already gives
  a run/debug view; chat transcript stays chat.

## 2. Their loop in one paragraph

Every agent is an `Agent` owning one append-only `Session` event log
(`turn/start`, `user/message`, `assistant/message`, `tool/call`,
`tool/result`, `step/end`, `turn/end`, plus plugin-merged families like
`compaction/*`, `todo/write`, `plan/mode`, `approval/*`, `feedback/*`). Model
history is *derived* from the log; replay/fork/resume are re-derivation. The
driver runs turns of steps: claim queued input → `agent/pre-step` waterfall
(compaction, plan-state append, steering) → LLM request → classify tool calls
→ `tools/pre-execute` → monotonic guards → `ctx.approval` → execute →
`tools/post-execute` → `tool/result` → next step or `turn/end`. Clients see
live `agent/*` events and read durable `session/event` for replay. Everything
— todos, jobs, schedules, plan state, approvals, feedback — is events in the
same log, which is why resume/fork is free for them.

## 3. Feature-by-feature notes

### 3.1 Interaction & UX (client `packages/client/ui-*`)

- Composer: slash-command registry (`ctx.commands`) with `input.hint` +
  `attachments` flags; message queue while running; **steer** (inserts at next
  step); keyboard-shortcut palette.
- Session list: running badges, archive, paged history, `session-query` FTS.
- Turn UI: todo checklist card, plan-review card, approval cards, job cards,
  subagent activity panel, deliverables card (declared files + changed files),
  trajectory (raw event) view, message feedback buttons, token meter,
  markdown images, virtualization.
- Settings pages: models, agent loop, shell, subagent, plugins, web search,
  session log, shortcuts, presets, permission presets.
- Voice input (experimental), file upload rail, office-to-pdf, document
  preview sidebar, terminal sidebar (PTY), web/desktop/CLI parity.

### 3.2 Model-facing tool surface (docs/tool-catalog.md)

`bash`/`pwsh` (+PTY, `run_in_background`), `job_{list,output,kill}`,
`edit`/`read`/`write`/`read_image`, `str_replace_editor`, `glob`/`grep`
(bundled ripgrep, spill store), `lsp`, `web_fetch`/`web_search`,
`run_code` (PTC), `subagent`/`subagent_fork`/`list_subagent_models`,
`send_message`/`interrupt_agent`/`list_agents`, `workflow`, `ralph`,
`todo_write`, `skill`, `plugin_manager`, `schedule_*`, `create_goal`/
`get_goal`/`update_goal`, `ask_user_question`, `exit_plan_mode`, `present`,
`terminal_*`, `session_{search,event_read,event_search,trace}`,
`load_workspace_dependencies`, `stagehand_*` browser tools, MCP resource
tools, `cordis_inspect_*`, experimental `spawn_teammate`/`team_task_*`/
`wait_agent`.

### 3.3 Notable design ideas worth copying

- **Fail-closed approval**: an unanswerable approval channel denies, never
  silently allows (`unavailable` outcome).
- **Surface vs log**: UI-facing "current surface" is a projection; replaced
  context stays `shadowed` in the log — compaction can prune without losing
  history.
- **Fork = replay prefix into new session** — cheap branching with lineage
  (`inheritedEventCount`).
- **Steering is a next-step input**, not a side-channel: same
  `agent/pre-step` claim path as queued prompts, so ordering is defined.
- **Spill store**: any tool result can be capped; the model gets a pointer +
  bounded read-back tool — keeps transcript size controlled.
- **All notifiers as plugins**: `IChatRunNotifier`-style fan-out is exactly
  what our dispatcher now does.

## 4. Our equivalent primitives (for spec writers)

| deepseek-harness | agent-harness |
|---|---|
| `Session` event log | `ChatConversation` + `ChatMessage` (+`ChatRun`) rows |
| `turn` / `step` | `ChatRun` + iterations inside `ChatService.RunAsync` |
| `agent.steer()` | none — `POST /messages` enqueues a new run (FIFO) |
| `ctx.sessions.create(seed)` fork | none |
| `ctx.approval` + `approval/*` | `IChatTool.RequiresConfirmation` (metadata only) |
| `tools/pre-execute` + guards | `ChatCapabilityRegistry` enable/disable toggles |
| `compaction` plugin | none — full-history `BuildTranscriptAsync` |
| `spill` store | inline tool results in `ChatMessage.Content` |
| `session-query` (FTS) | `GetChatConversationsAsync` title search |
| `schedule` pkg | `ScheduledJob`/`ManagedJob` infra (not model-facing) |
| `jobs` + `run_in_background` | `shell_exec` sync only |
| `attachment`/`file-upload`/`read_image` | `ChatImageStore` (generated images) |
| `feedback/message-*` | none |
| `present` + `workspace-changes` | worktree diff (delegation only) |
| `token-meter` | `ChatRun.TokensIn/Out` persisted; no pressure meter |
| `plan-mode` + `exit_plan_mode` | none |

## 5. Recommended roadmap

1. **SPEC-20261005-chat-tool-approval** — approval seam + permission presets
   (foundation for plan mode; turns `RequiresConfirmation` into a real gate).
2. **SPEC-20261005-chat-plan-mode** — `/plan`, `exit_plan_mode`, review card,
   read-only gating (depends on #1 for enforcement).
3. **SPEC-20261005-chat-fork-steering** — fork-at-message + mid-turn steer.
4. **SPEC-20261005-chat-context-management** — compaction + spill + pressure
   meter (the single biggest correctness gap: today the transcript grows
   until the provider errors).
5. **SPEC-20261005-chat-attachments-feedback** — upload + `read_image` +
   message feedback + deliverables card.
6. **SPEC-20261005-chat-jobs-schedule-search** — background `shell_exec` jobs,
   scheduled self-prompts, FTS across conversations.

Order is by (correctness value × dependency), not by effort; #4 is the one
that silently degrades today.
