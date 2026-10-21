# COMPARISON — Orca (stablyai/orca) × Harness: agent CLIs, delegation, status, session DBs, board

**Date:** 2026-10-20 · **Scope:** how Orca drives agent CLIs (claude, codex, devin, opencode, gemini,
antigravity, cursor, grok, pi, droid, kimi, muse…), its delegation/orchestration model, status
detection, per-CLI session-store reading, and the board surface — mapped against agent-harness.

**Sources:** `https://www.onorca.dev/docs` (model/worktrees, model/agents-sessions, agents/*,
cli/orchestration, cli/overview, cli/automations, cli/worktree-checkpoints, activity,
notifications) and `stablyai/orca` tree (`src/main/runtime/orchestration/**`, `agent-hooks/**`,
`native-chat/agent-session-*`, `skills/orchestration`, `skill-guides/orchestration/references/*`).
Harness side: `.specs/*`, `src/Taskboard.*/**` current main.

## 1. How Orca drives agent CLIs

| Aspect | Orca | Harness (today) | Coverage |
|---|---|---|---|
| Launch | Agent picker spawns CLI in a PTY per worktree; per-agent editable launch args/env; permission-bypass flag pre-applied (`--dangerously-skip-permissions`, `--yolo`, …) — "the worktree is the sandbox" | `IAgentAdapter` builds argv; `IAgentAcpClient` runs process; `AgentInstallDialog`/`AgentModelConfigDialog` for config | ⚠️ no per-launch model/effort override |
| Isolation | Worktree-native: every task = own `git worktree`; shared gitignored dirs/files via Settings + `orca.yaml` + `.worktreeinclude` | `use_worktree` + `repository_path` on `delegate_task`; no shared-context provisioning | ⚠️ shared dirs/files missing |
| Status detection | OSC title sequences + **managed agent hooks** (written into each CLI's config, POST to a hook server — single status store all readers subscribe) + output-idle heuristics → working / waiting-you / done / idle / blocked | `AgentRunState` (Queued/Running/Succeeded/Failed/Canceled/BudgetExceeded) + mailbox `decision` → Needs You; no OSC/hook/output pattern detection | ❌ **MIGRAR** |
| Session stores | Scans each CLI's on-disk transcripts (`~/.claude/projects/*.jsonl`, `~/.codex/sessions`, `~/.local/share/opencode/opencode.db`, muse, cursor, qoder, codebuddy…) → unified history panel + native resume commands | `AgentSessionScanner` (claude/codex/opencode/gemini/agy/devin) + `CliDatabaseMap`/`ICliDbExtractor` for metrics; Sessions tab w/ Resume | ✅ core parity; search/grouping gaps |
| Usage/quota | Per-provider quota windows (5h/daily/weekly) + balances in status bar roster, warn @80% | FinOps token/cost metrics from CLI DBs | ⚠️ quota windows absent |

## 2. Delegation & status monitoring

| Aspect | Orca | Harness | Coverage |
|---|---|---|---|
| Model | **Run** (durable namespace + coordinator inbox) → **Task** (spec+deps) → **Dispatch** (one attempt; lifecycle authority) → supervised **worker** | `DelegationTask` (scope, deps, fanout, retry_of, worktree) + `AgentMailboxMessage`; scope = conversation | ⚠️ no Run/Dispatch split, no delivery/ack |
| Inbox | `check` returns oldest unacked **Delivery** (≤50 msgs, FIFO) replayed until `--ack`; `--peek/--all` read-only; group addresses `@all @idle @<cli> @worktree:<id>` | `agent_inbox` marks read on read; send supports `@all`/`@idle`/cli/task | ⚠️ at-least-once + ack missing |
| Worker→coordinator Q | `ask` blocking w/ `--resume <message_id>` after timeout; `reply` answers | `agent_decide` (human gate on Agents page) | ⚠️ worker→agent (non-human) ask absent |
| Decision gates | `gate-create/resolve/list` — coordinator-owned question blocks a task | `agent_decide` covers human gate; not bound to task DAG | ⚠️ task-bound gates absent |
| Completion | `worker_done --outcome succeeded|failed --files-modified --report-path` exactly once; heartbeats w/ `--phase` | `worker_done` text msg; heartbeat exists | ⚠️ outcome contract weaker |
| Post-settle | `worker-retain/release/read` (archived output), `worker-start --retry-of`, `--terminal` reuse | `retry_of`, `delegate_compare`; no retain/release/read lifecycle | ❌ **MIGRAR** |
| Launch prefs | `worker-start --model <id> --effort <lvl>` per worker; receipt shows `launch.requested` vs `launch.effective` | none (cli only) | ❌ **MIGRAR** |
| Placement | `--on <host>` federated workers (SSH/remote Orca servers); host badges | local only; Cloud module exists | ❌ P3 |
| Recovery | `dispatch-show --preamble`, `task-update --status blocked --result`, `reset --tasks/--messages/--all` | `delegate_status`, cancel; no blocked-result, no reset | ⚠️ |

## 3. Board / surfaces

| Aspect | Orca | Harness | Coverage |
|---|---|---|---|
| Agent board | **Agent Dashboard** kanban: Needs You / Working / Done / Idle; rich cards (icon, session title, last-msg preview, age, host badge, PR status), search + filters (project/workspace/PR), click→focus terminal, board settings (hide idle), pop-out | Agents → Dashboard tab: same 4 columns of list items + merged activity feed | ⚠️ columns exist; cards/filters/deep-link thin |
| Issue board | Worktree cards in sidebar w/ status comment checkpoint (`worktree set --comment --workspace-status`) | GitHub Kanban (issue-centric); `KanbanBoard` cards; no agent-state glyph or live preview on cards | ⚠️ **MIGRAR** |
| Activity | Agents feed (completions, blocking, previews, worktree events) + unread badge + mark-unread + per-machine mute + sounds | Merged activity feed on Dashboard tab; no unread model, no notifications surface | ⚠️ unread/notifications absent |
| Hibernation | Auto-sleep done+idle ≥30min sessions; resume on focus via native `--resume`; guards (unsettled dispatch, live subagents) | — | ❌ **MIGRAR** |
| Automations | `orca automations`: cron/RRULE + precheck probe + provider + workspace/repo target + `--reuse-session` + `--extra-agent-args` + missed-run grace + per-host table | `/jobs` managed background jobs (schedule/enable/run); no agent-targeted automations | ⚠️ parity candidate P3 |

## 4. What to migrate — ranked

**P1 (board + attention — user asked first):**

1. `SPEC-20261020-agent-board-v2` — kanban cards ricos (state glyph, título da sessão, preview da
   última mensagem, idade, badges), busca/filtros, deep-link para run/terminal, unread, idle toggle;
   + enriquecer cards do Kanban GitHub (glyph de estado do agente + preview).
2. `SPEC-20261020-agent-attention-model` — detecção unificada working/waiting-you/done/idle/blocked
   (hooks OSC/output-idle/question-patterns) num `AgentSessionStateStore` que alimenta Needs You,
   badges e notificações.

**P2 (orchestration contract):**

3. `SPEC-20261020-orchestration-contract-v2` — Run/Dispatch split, deliveries+ack FIFO, ask/reply,
   task-bound decision gates, retain/release/read, `worker_done --outcome`, launch prefs.
4. `SPEC-20261020-agents-feed-notifications` — feed global com unread, badge, mark-unread,
   preferências por categoria + som.

**P3 (sessions/ops):**

5. `SPEC-20261020-session-history-parity` — busca dentro de transcripts, grouping/sort, resume
   actions (cli/chat/copy cmd/id/log, open log/cwd), toggles por CLI, hide-empty.
6. `SPEC-20261020-agent-hibernation` — pausar sessões done+idle e retomar com `--resume` nativo.
7. `SPEC-20261020-provider-quota-usage` — janelas de quota por provider + reset + alerta 80%.
8. `SPEC-20261020-worktree-shared-context` — dirs/arquivos gitignored compartilhados + comentário/
   status de card por worktree (checkpoint leve).

## 5. Re-analysis 2026-10-21 — post-implementation

All eight migrations above shipped (PRs #601–#610). Re-analyzed current
`stablyai/orca` source + docs for the remaining gaps now documented in
`README.md` "Compared to Orca":

| # | Gap | Spec |
|---|---|---|
| ① | Worktree setup/archive scripts + `wait-for-setup` + `defaultTabs` + `issueCommand` | `SPEC-20261021-worktree-setup-scripts` |
| ② | Agent-targeted automations (precheck, missed-run grace, reuse-session, extra-agent-args) | `SPEC-20261021-agent-automations` |
| ③ | Multi-account hot-swap (claude/codex isolated homes, per-account usage) | `SPEC-20261021-provider-account-hot-swap` |
| ④ | Remote/federated hosts + pairing (`--on ssh:`, `serve`, revocable tokens) | `SPEC-20261021-remote-hosts` |
| ⑤ | Review surface — per-line AI/human attribution + diff annotations | `SPEC-20261021-review-attribution` |
| ⑥ | Artifact store (blob offload for large worker outputs) | `SPEC-20261021-artifact-store` |
| ⑦ | Secrets vault (encrypted credential store) | `SPEC-20261021-secrets-vault` |
| ⑧ | Quota-driven throttling on dispatch | `SPEC-20261021-quota-throttling` |
| ⑨ | Terminal splits + OS-level notifications + custom sounds | `SPEC-20261021-terminal-ux-notifications` |
| ⑩ | Project groups, CLI depth tiers, computer-use | `SPEC-20261021-project-groups`, `SPEC-20261021-cli-depth-tiers`, `SPEC-20261021-computer-use` |

Parity confirmed: delegation contract v2, attention model, board v2 + feed,
hibernation, provider quota observation, shared context, worktree cards,
session stores. Partial: session restore (no layout/scrollback persist),
terminal (tabs only), notifications (in-app only).

Suggested build order for the next wave: ① → ② → ⑧ (orchestration depth)
→ ⑥ → ⑦ (storage/security) → ⑤ (review) → ③ → ⑨ (UX) → ④ → ⑩ (scale).
