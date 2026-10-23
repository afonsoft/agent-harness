# COMPARISON — hermes-agent × OpenHarness × ECC × Harness

**Date:** 2026-10-23 · **Scope:** three external agent repos mined for features, patterns and
guardrails worth porting into agent-harness — `nousresearch/hermes-agent` (Python personal agent:
CLI + ~20-platform gateway + TUI + desktop), `HKUDS/OpenHarness` (Python harness library + `ohmo`
personal agent), `affaan-m/ecc` (Claude Code plugin bundle: 71 agents, 311 skills, 97 commands,
hooks, rules, Node scripts).

**Sources:** shallow clones at `/tmp/repo-analysis/{hermes-agent,OpenHarness,ecc}` — key evidence
cited per row: `hermes-agent/{AGENTS.md, agent/AGENTS.md, tools/AGENTS.md, plugins/AGENTS.md,
gateway/AGENTS.md, toolsets.py, trajectory_compressor.py}`; `OpenHarness/src/openharness/{hooks,
permissions, sandbox, services/autodream, swarm, tools, channels}`; `ecc/{AGENTS.md, hooks/,
.agents/skills/*, scripts/}`. Harness side: `src/Taskboard.*/**` + `.specs/*` current main.

## 1. Agent runtime & tool surface

| Aspect | hermes-agent | OpenHarness | ECC | Harness (today) | Coverage |
|---|---|---|---|---|---|
| Tool registry | `tools/registry.py` + auto-discovery; schema only exposed when a named toolset lists it | `BaseTool` + registry; 43+ tools | n/a (content pack) | `IChatTool` + `ChatCapabilityRegistry`, ~25 tool files | ✅ parity |
| Progressive disclosure | `tool_search` + **deferred tools** (`browser_vault` ships catalog-line only) — keeps schema token cost flat | `tool_search_tool` — same pattern | — | All tools + MCP tools sent every call | ❌ **MIGRATE** |
| Availability gating | `check_fn` per tool (credential/reachability probe, TTL-cached per profile) — unavailable tools leave the schema | same idea via settings | — | none — tools always in schema | ❌ **MIGRATE** |
| Toolsets | Named bundles (`messaging`, `kanban`, `browser`…) enable/disable per platform/profile | toolsets by profile | — | static set per conversation | ⚠️ partial (allowed-list exists) |
| Parallel tool exec | sequential round | **parallel tool execution** in engine | — | `foreach` sequential (`ChatService.cs:1961`) | ❌ **MIGRATE** |
| Prompt cache invariant | **System prompt byte-stable per conversation**; mutations ride user/tool messages only; prompt footprint eval in CI | — | strategic-compact skill | compaction exists (prune→summarize) but no prefix-stability audit/budget | ⚠️ **MIGRATE** |
| Compression | prune tool results (no LLM) → boundary-picked LLM summary → deterministic fallback on stall | auto-compaction preserves task state + channel logs | — | `CompactWireAsync` = prune→summarize already | ✅ parity (add deterministic fallback + state preservation check) |
| Ask-user tool | `clarify` | `ask_user_question_tool` | — | approval cards only | ⚠️ |

## 2. Delegation & multi-agent

| Aspect | hermes | OpenHarness | Harness | Coverage |
|---|---|---|---|---|
| Model | `delegate_task` single/batch; `group` completion units share ONE pool slot | teams + file mailbox + subprocess/in-proc backends | `DelegationTask` DAG + `AgentMailboxMessage` + dispatcher cap 4 | ✅ core parity (ours DB-backed, richer) |
| Roles/depth | `leaf` vs `orchestrator`; `max_spawn_depth` (default 2) | — | flat (any task) | ❌ **MIGRATE** |
| Routing | `delegation.backends.<name>` pluggable provider; per-task `model`/`provider`/`profile`/`service_tier`; fails closed on unroutable task | spawn with inherited CLI flags/env | `ExtraArgs`, model tiers global | ⚠️ **MIGRATE** (per-task route fields) |
| Output contract | `output_schema` per task; on validation miss keeps `status: completed` + `schema_valid:false` — never discards child result | — | none | ❌ **MIGRATE** |
| Heartbeat | `heartbeat=N` → first-class queue event, one daemon timer | — | mailbox `heartbeat` type exists | ⚠️ parity |
| Orphan accounting | child result carries `orphaned_processes` + `unread_completions`; `process_manage(action=handoff)` transfers ownership to parent | task_output/stop tools | run kills own tree via `IProcessIsolator` | ⚠️ **MIGRATE** (report orphans on run) |
| Async completion | background delegation → completion queue; deferred close drains pipe after timeout | subprocess pollable | dispatcher + mailbox | ✅ parity |
| Gates | `clarify` + approvals | permission_sync across teammates | `agent_decide` human gate | ✅ parity |

## 3. Security & governance

| Aspect | hermes | OpenHarness | ECC | Harness | Coverage |
|---|---|---|---|---|---|
| Command gating | approval config + rubric | modes: default/plan/**full_auto** | gateguard hooks | `PermissionGateway` + `DynamicCommandClassifier` + risk tiers | ✅ parity (ours fail-closed) |
| Path jail | per-tool confinement | `PathRule` + **SENSITIVE_PATH_PATTERNS always-deny**: `*/.ssh/*`, `*/.aws/credentials`, `*/.gnupg/*`, `*/.docker/config.json`, `*/.kube/config`, own credential stores — immune to mode/prompt-injection | config-protection hook | `PathJailValidator` (canonical + symlink) confines to worktree — but workspace picker runs anywhere under `$HOME`, no credential denylist | ❌ **MIGRATE — P0** |
| Shell path escapes | — | denylist applies to command inspection too | — | classifier flags `ssh`/`scp` but `cat ~/.ssh/id_rsa` shape unchecked | ⚠️ **MIGRATE — P0** |
| Secrets | `secret_scope` contextvar, fail-closed `UnscopedSecretError` under multiplex; children get `served_profile_child_env` (strips launch-profile residue) | scoped credential store | — | `ISecretRedactor` on output; `WithoutHarnessEnv` on spawn | ✅ parity (different axis) |
| Hooks engine | plugin hooks: `pre/post_tool_call`, `pre/post_llm_call`, session start/end, `pre/post_auxiliary_call` | **10 events + 3 kinds**: `command`, `http`, `prompt` (LLM-as-judge gate); matcher + priority + `block_on_failure` + timeout; hot reload | rich PreToolUse catalog (gateguard, doc-file-warning, suggest-compact, observe-runner, governance-capture, config-protection, mcp-health-check) | AgentHookInstaller pushes hooks **into** CLIs → `/api/agent-hooks` inbound; no harness-side user hook engine for chat tools | ❌ **MIGRATE — P1** |
| Prompt-injection defense | — | sensitive paths + `web_fetch` URL validation | **Prompt Defense Baseline** in CLAUDE.md (unicode/homoglyph/zero-width suspicion; external content untrusted) | issue bodies + comments + mailbox + fetched URLs flow into prompts unscreened | ❌ **MIGRATE — P0** |
| MCP supply chain | plugin catalog SHA-pin + `removed.yaml` kill list | — | `mcp-dependency-review` (static lint of mutable package refs) | provisions MCP into agent configs; no pin lint, no kill list | ⚠️ **MIGRATE** |
| Exec sandbox | docker/ssh/modal/daytona terminal backends | Docker sandbox session backend | — | `DockerCliSpawner` (docker exec for CLIs) + `SystemdScopeProcessIsolator`; chat tools run on host | ⚠️ extend docker to chat tools (P2) |

## 4. Memory & context

| Aspect | hermes | OpenHarness | ECC | Harness | Coverage |
|---|---|---|---|---|---|
| Store | `MemoryProvider` ABC + `MemoryManager` (sync_turn/prefetch/shutdown/post_setup); providers moved to plugin catalog | `MEMORY.md` + `session_memory` + `memory_extract` | `ecc.memory.v1` portable docs, scopes project/team/user, fail-closed `.gitignore` | `ChatMemoryStore` (chat-memory.json) + `ProjectMemoryItem` per remote | ✅ parity (single backend) |
| Auto-consolidation | curator + provider `on_session_end` extraction | **autodream service**: scans sessions touched since last consolidation → lock → backup → consolidation prompt → diff/rollback | memory-persistence hook | manual `memory` tool only | ❌ **MIGRATE — P1** |
| Recall discipline | — | — | recall-before-write; recalled memories are untrusted context, not instructions | `memory search` exists | ⚠️ doc-level gap |
| Cross-CLI sharing | — | — | **unified-memory vault** shared across harnesses | project memory is harness-only; CLIs get context files | ⚠️ P3 (`.agent-harness/memory/` vault CLIs can read) |

## 5. Evals, regression & ops

| Aspect | hermes | OpenHarness | ECC | Harness | Coverage |
|---|---|---|---|---|---|
| Agent-behavior evals | `evals/` fixtures: compaction, delegation_group_schema, tool_search, token_accounting, **prompt_footprint**, provider_wire, plugin_isolation audit | E2E suites | `eval-harness` skill (EDD, pass@k) | xUnit unit/integration only | ❌ **MIGRATE — P1** |
| Failure postmortem | — | — | `agent-introspection-debugging` (capture→diagnose→contained recovery→report) | run logs + verification retry loop | ⚠️ **MIGRATE** (auto introspection report on failed runs) |
| Module regression | `scripts/check` = all CI lint gates locally + ratchets (CC≤20, file≤2000, AGENTS.md≤12k) | — | `module-regression` REGRESSION.md ledger (consumers + acceptance cmd + propagation rule) | CI gates exist; no ledger, no local `check` | ⚠️ **MIGRATE** |
| Preflight | `hermes pm lock` + pinned deps | **`oh --dry-run`**: ready/warning/blocked verdict + next actions; preview matched skills/tools/MCP without executing | `doctor.js`, operator-readiness-dashboard | — | ❌ **MIGRATE — P0** (`taskctl doctor`) |
| Doc routing | root AGENTS.md = hub ≤12k, per-area AGENTS.md, routing table, caps enforced | — | common-* + per-language rule packs | CLAUDE.md + .claude/rules; no per-area routing/caps yet | ⚠️ adopt when it grows |
| Contribution rubric | explicit wanted/rejected + **footprint ladder** (extend → skill → service-gated tool → plugin → MCP → core tool) | — | workflow-surface policy (skills canonical) | DO/DON'T + hard rules | ⚠️ add footprint ladder |

## 6. Surfaces Harness lacks entirely

| Feature | Repo | Notes | Verdict |
|---|---|---|---|
| Messaging channels (Telegram/Slack/Discord/Feishu/WhatsApp/Matrix) | hermes gateway (~20 adapters), ohmo channels | escalation/`agent_decide` → phone; steer agents from chat | **P2** — mailbox model already exists; start with one Slack adapter |
| Plugin system (tools/hooks/commands/providers via `ctx`; host-proc isolation for untrusted) | hermes | biggest architectural idea; our answer so far is MCP provisioning | **P2/P3** — honest call: MCP covers most extension; revisit if 3rd-party demand |
| Plugin catalog + kill list | hermes `plugin-catalog/` SHA-pins + `removed.yaml` | — | applies to our skills catalog: versioned pins + revocation | **P1-lite** |
| Skill curator (agent maintains its own skills) | hermes `agent/curator.py` | — | **P3** |
| LSP tool for chat agent | OpenHarness `lsp_tool` + services/lsp | symbol nav cuts grep/read tokens | **P1-lite** — cheap win on big repos |
| Voice/themes/vim/output styles | OpenHarness | TUI polish | skip (we're Blazor) |
| Multi-profile multiplex + scoped secrets | hermes | one process, many `~/.hermes` profiles | **P3** — only if multi-user lands |

## 7. What to migrate — ranked

**P0 (security + preflight — cheap, high leverage):**

1. `SPEC-*-sensitive-path-denylist` — always-deny credential patterns
   (`*/.ssh/*`, `*/.aws/*`, `*/.gnupg/*`, `*/.docker/config.json`, `*/.kube/config`,
   `~/.agent-harness` secret stores) enforced in `PathJailValidator` **and** in
   `DynamicCommandClassifier` for shell args; deny regardless of risk tier/mode.
   Source: OpenHarness `permissions/checker.py`.
2. `SPEC-*-prompt-defense-baseline` — untrusted-content screen on everything
   ingested into prompts (issue bodies/comments, mailbox messages, fetch_url,
   browser_use): strip zero-width/invisible chars, tag as data-not-instructions.
   Source: ecc CLAUDE.md baseline.
3. `SPEC-*-run-preflight-doctor` — `taskctl doctor` + `POST /api/harness/preflight`:
   ready/warning/blocked verdict (CLI installed/authed, worktree root writable,
   MCP reachable, model flag valid, disk) + next-action hints; `--dry-run` flag on
   `taskctl run` and delegation dispatch. Source: OpenHarness `oh --dry-run`.
4. `SPEC-*-deferred-tools` — mark heavy/conditional tools deferred (`browser_use`,
   `generate_image`, MCP adapters); expose `tool_search`; add `check_fn`-style
   availability probes (TTL-cached) so dead tools leave the schema. Sources:
   hermes `toolsets.py`/`tools/AGENTS.md`, OpenHarness `tool_search_tool.py`.
   Direct per-turn token savings + fewer hallucinated calls.

**P1 (structure — medium effort):**

5. `SPEC-*-harness-hooks` — hook engine on the chat/run pipeline: events
   (run.start, run.end, pre/post_tool_use, user_prompt_submit, pre/post_compact,
   run.stop), kinds (`command`, `http`), regex matcher + priority +
   `block_on_failure` + timeout; config in `Taskboard:Hooks` + managed block
   sentinel (reuse `AgentHookInstaller.Marker` pattern). First consumers ported
   from ecc: config-protection, mcp-health-check, governance-capture (audit log),
   suggest-compact. `prompt`-kind (LLM-gate) deferred to v2.
6. `SPEC-*-memory-consolidation` — autodream-style job on the Jobs infra:
   scan ended conversations/runs since last consolidation → per-scope lock →
   extract durable facts via aux model → merge into `ProjectMemoryItem` with
   backup + diff/rollback. Source: OpenHarness `services/autodream/`.
7. `SPEC-*-delegation-contract-v3` — per-task route fields
   (`model`/`provider`/`service_tier`), `output_schema` + `schema_valid` flag
   (never discard child result), `group` completion units sharing a dispatch
   slot, leaf/orchestrator roles + `max_spawn_depth`, orphan accounting fields
   on run result. Source: hermes `tools/AGENTS.md` § Delegation.
8. `SPEC-*-agent-evals` — `evals/` with behavioral fixtures: tool_search recall,
   compaction boundary, delegation schema, mailbox routing, security-gateway
   refuse paths (injection probes), prompt-footprint budget; `taskctl eval`
   runner + optional pass@k. Sources: hermes `evals/`, ecc eval-harness.
9. `SPEC-*-parallel-tool-round` — execute independent tool calls in a round
   concurrently (respect approvals; serialize writes to wire). Source:
   OpenHarness engine.
10. `SPEC-*-regression-ledger` — `REGRESSION.md`: per module
    (Domain/Application/EF/Server/Integrations/Blazor) downstream consumers +
    executable acceptance command + propagation rule; referenced from the
    verification loop. Source: ecc module-regression.
11. `SPEC-*-run-introspection` — on failed agent/chat runs auto-attach a
    structured introspection report (failure class, last tool, pressure,
    suggested fix) to the run record + Needs You card. Source: ecc
    agent-introspection-debugging.

**P2 (bigger bets — needs a product decision):**

12. `SPEC-*-chat-channels` — Slack (or Discord/Telegram) adapter: Needs You
    escalations + approval buttons + steer-in → mailbox. One adapter first.
13. `SPEC-*-skills-catalog-v2` — versioned pins + `removed` kill list + per-repo
    recommended sort (ecc agent-sort idea) + host-compat lint on SKILL.md.
14. `SPEC-*-sandbox-backend` — Docker exec backend for chat shell/file tools
    (extend `DockerCliSpawner` work), opt-in per workspace.
15. `SPEC-*-lsp-tool` — `lsp` chat tool (defs/refs/symbols) to cut grep/read
    token spend on large repos.
16. `SPEC-*-plugin-model` — decide: in-proc plugin assemblies with a
    `PluginContext` (register tool/hook/command) vs. MCP-only stance. Hermes
    compat contract is the reference if we build it (additive kwargs,
    signature-inspected callbacks, ≥2-release deprecation window, no
    re-export shims).

**Do NOT take (rejected upfront):**

- God-file→facade split as a *goal*: apply only to real pain points
  (`ChatService.cs` 3.5k lines is the one candidate — split approvals/streaming/
  attachments into partials; leave generated migrations).
- Per-language rule packs, agent-count inflation (ecc's 71 agents/311 skills):
  our skills catalog already covers; volume is not the win.
- File-based mailbox (OpenHarness): our SQLite mailbox is strictly better.
- Full plugin-host process isolation (hermes `plugins.isolation: host`):
  only if the plugin model (item 16) ships.
- TUI/vim/themes: out of scope for a Blazor product.

## 8. Quick wins inside existing code (no new spec needed)

- `ChatService.cs` — sequential `foreach` over `toolCalls` (line ~1961):
  parallelizable read-only tools can run under `Task.WhenAll`.
- `ChatRiskRules` — add credential-path patterns to the High-risk rules until
  item 1 lands (`~/.ssh`, `~/.aws`, `id_rsa`, `credentials`).
- `AGENTS.md` — append a **footprint ladder** line to the DON'T section
  (extend → skill → service-gated tool → plugin → core change).
- `Directory.Packages.props` — adopt `>=floor,<next_major` policy note
  (hermes `pm` policy analog).
