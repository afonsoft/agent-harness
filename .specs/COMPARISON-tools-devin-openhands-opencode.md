# COMPARISON — tool sets: Devin Web × OpenHands × OpenCode TUI × Harness chat

**Date:** 2026-10-07 · **Scope:** the callable tool surface of each agent — what the model can *do* — not UI/layout (covered by `COMPARISON-devin-web-openhands-harness-chat.md`).

**Sources:** opencode clone at `packages/opencode/src/tool/*.{ts,txt}` (v1.x, anomalyco/opencode fork); OpenHands cloud 1.43.0 at `.venv/…/openhands/tools/*` (default + planning presets); Devin Web from docs.devin.ai; Harness from `Program.cs` chat-tool list (~49 `IChatTool`s).

## 1. Side-by-side matrix

| Capability | Devin | OpenHands | OpenCode TUI | Harness | Coverage |
|---|---|---|---|---|---|
| Read file (paged, line numbers) | editor | `file_editor view` | `read` | `read_file` | ✅ |
| Write/create file | editor | `file_editor create` | `write` | `write_file` | ✅ |
| Exact-string edit | editor | `file_editor str_replace`/`insert`/`undo_edit` | `edit` (replaceAll) | `edit_file` | ✅ |
| Multi-file atomic patch | — | `apply_patch` | `apply_patch` | — | ❌ **MIGRAR** |
| List dir / glob | shell | `glob` (planning) | `glob` | `list_dir` + `find_files` | ✅ |
| Content grep | shell | `grep` (planning) | `grep` | `search_files` | ✅ |
| Shell command | `shell` (persistent tmux) | `terminal` (tmux session) | `shell` | `shell_exec` (one-shot) + `job_*` (bg) | ⚠️ parcial |
| Persistent agent PTY | shell persists | tmux panes | shell persists per session | jobs only — agent cannot reattach stdin | ❌ **MIGRAR (P2)** |
| Task/todo tracking | internal plan | `task_tracker` | `todowrite` | `todo` | ✅ |
| Sub-agent spawn | child sessions | `task`/`delegate` | `task` (subagent_type) | `task` + `delegate_*` family (DAG, fanout, coordinate, mailbox) | ✅ superset |
| Web fetch | browser | `browser_*` | `webfetch` | `fetch_url` | ✅ |
| Web search | search | — (via browser) | `websearch` | `web_search` (backends configuráveis) | ✅ |
| Browser automation | browser | `browser_use` (~14 tools: navigate/click/type/get_state/scroll/tabs/storage/record) | — | — (SPEC-20261016) | ❌ **já specado (slice 6)** |
| Ask user a question | message/ask | (approval flow) | `question` (options + custom) | `agent_decide` (decision gate) | ⚠️ **MIGRAR versão leve** |
| Plan enter/exit | — | `planning_file_editor` (PLAN.md) | `plan` enter/exit | plan mode + `exit_plan_mode` | ✅ |
| Load skill on demand | playbooks | `skills` | `skill` | `use_skill` | ✅ |
| Code intelligence (LSP) | IDE | — | `lsp` (goToDef, findRefs, hover, docSymbol, workspaceSymbol) | — | ❌ **MIGRAR (P1)** |
| Confined orchestration script | — | `workflow` (declarative multi-step) | `code_mode` (script calls tools/MCP in one turn) | `delegate_plan`/`delegate_coordinate` (DAG) | ⚠️ equivalente via DAG; `code_mode` é P2 |
| Run tests | shell | terminal | shell | `run_tests` | ✅ |
| Code interpreter | — | — | `code_mode` (confined) | `code_interpreter` (python/node/dotnet, sem tool-calls) | ⚠️ |
| Git ops | shell/gh (mutations too) | terminal | shell | `git` **read-only** (status/log/diff/branch) | ⚠️ mutations via slice 4 / preset=full |
| GitHub PR/issues | gh/PRs | — | `gh` via shell | `board_*` (issues/labels/priority/comment) | ✅ issues; PR tool = slice 4 |
| Jobs/schedules | schedules/jobs | — | — | `job_*` + `schedule_*` | ✅ (nós temos mais) |
| Memory | knowledge | `sleeptime_compute`/`tom_consult` | — | `memory` | ✅ |
| Calendar/datetime, calc | — | — | — | `current_datetime`, `calculator`, `generate_image`, `read_image`, `session_search`, `present` | ✅ (exclusivo nosso) |
| Operate outside workspace | — | runtime confinado | `external_directory` (permission request) | path-jail fixo | ❌ **MIGRAR (P3)** |
| Image generation | — | — | — | `generate_image` | ✅ exclusivo |

Legend: ✅ covered · ⚠️ partial/different shape · ❌ gap.

## 2. What to migrate — ranked

### P1 — high autonomy value, reasonable effort

1. **`question`** (opencode) — a lightweight "ask the user" tool: structured options + free-text answer, mapped onto a new `ChatApproval.Kind = "question"` so it rides the existing approval pipeline/UI. Devin asks clarifying questions mid-run; ours only has the heavier `agent_decide` gate (orchestration decisions). *Why:* kills the biggest autonomy killer — agent stalls on ambiguity.
2. **`apply_patch`** (opencode + OpenHands) — one call, multi-file atomic patch (`*** Begin Patch` envelope: add/update/delete/move). More reliable than N sequential `edit_file` calls for multi-hunk changes and supports file delete/move which we lack. Port opencode's `apply_patch.ts` parser to `ApplyPatchTool` (path-jailed, redacted).
3. **`code_nav` / LSP subset** (opencode `lsp`) — goToDefinition, findReferences, documentSymbol, workspaceSymbol. For a .NET-first harness the high-leverage port is in-proc Roslyn (`Microsoft.CodeAnalysis.CSharp`) over the workspace's `.sln`/`.csproj` — no external server needed; fall back to grep for other languages. Cheapest viable: `documentSymbol`+`workspaceSymbol` first.

### P2 — structural autonomy wins

4. **`pty_exec`** — OpenHands' agent-facing persistent terminal (tmux). Ours: `shell_exec` is one-shot; `job_*` covers background but the agent can't `npm run dev` and watch, or answer interactive prompts. Port: bind to `TerminalSessionManager` sessions (`agent-<runId>`), actions `open|input|read|close` — reuses the slice-1 terminal backend.
5. **`code_mode`** (opencode) — confined script that calls registered tools/MCP in a single turn (tool batching). Our `code_interpreter` can't invoke `IChatTool`s. High value for token/context economy; needs the tool-call bridge + sandbox — schedule after approvals are risk-tiered (slice 3).
6. **`browser_use`** — already spec'd (SPEC-20261016, issue #527): navigate/click/type/scroll/screenshot/extract, headless Playwright, `browser-shot` attachments.

### P3 — small, opportunistic

7. **`external_directory`** — opencode's permission request for paths outside the workspace. Map to approval-gated `path` escape in filesystem tools (Kind `tool-call`, HIGH risk) instead of a new tool.
8. **git mutations** — ours is read-only by design; add `git add|commit|checkout|push` gated by `permission preset = full` — naturally part of slice 4 (git-bar, `git_pull`/`git_push`/PR endpoints already there; mirror as tools for the agent).
9. **`plan_enter`** counterpart — minor; our plan mode is user-controlled so the model rarely needs to enter it.

### Skip (covered or not a fit)

- `workflow` (OpenHands) — our `delegate_plan`/`delegate_coordinate` DAG is a superset.
- `tom_consult`/`sleeptime_compute` — background memory condensation; our `memory` tool + compaction covers it.
- `gemini/*` vendor variants — provider-specific aliases, no value.
- `invalid` (opencode) — internal error sink, not a capability.
- `read_file`-style Gemini preset, `planning_file_editor` — our plan-review flow is equivalent.

## 3. Proposed backlog additions

Fold into the parity epic (#521) as **slice 8 — tool parity**:

| New tool | Source | Effort |
|---|---|---|
| `question` | opencode `question.ts` | M — new approval kind + options UI |
| `apply_patch` | opencode `apply_patch.ts` / OpenHands `apply_patch` | M — envelope parser + applier |
| `code_nav` | opencode `lsp.ts` → Roslyn in-proc | L (C#); M-lite for symbol-only |
| `pty_exec` | OpenHands `terminal` → `TerminalSessionManager` | M |
| `code_mode` | opencode `code-mode.ts` | L — needs tool-bridge sandbox |
| `external_directory` | opencode `external-directory.ts` | S — approval-gated jail escape |
| git mutations | slice 4 companion | S-M |

Spec-ready once merged: `SPEC-20261018-chat-tool-parity.md` covering `question` + `apply_patch` + `code_nav` (the three P1s), deferring `pty_exec`/`code_mode` to a follow-up.
