# SPEC-20261024-p1-agent-reliability — P1 reliability tranche

> Status: Draft→Implementing
> Priority: P1 (follows SPEC-20261023 P0 hardening — merged #635–#638)
> Source: `.specs/COMPARISON-20261023-hermes-openharness-ecc.md` §P1
> Tracking issue: see below

## 1. Context

P0 closed the injection/credential/preflight/token gaps. This tranche ports the
next tier of harness patterns — structured extensibility (hooks), memory hygiene,
result contracts and regression feedback loops — without changing the
.NET/ABP/DDD architecture.

Done already (quick wins): bounded-parallel read-only tool calls (#639).

## 2. Scope

| # | Item | Evidence |
|---|------|---------|
| S1 | Hook engine — lifecycle hooks in the chat/run pipeline with event + matcher + `block_on_failure` | ecc hooks; OpenHarness hook bus |
| S2 | Memory consolidation — periodic dedupe/prune of `ChatMemoryStore` facts via the existing managed-job infra | hermes autodream |
| S3 | Delegation contract v3 — `output_schema` on `delegate_task`, `schema_valid:false` keeps child result, leaf/orchestrator roles, per-task model route | ecc delegation |
| S4 | `evals/` — repeatable behavior suite asserting tool-loop outcomes (smoke: tool_search→activate flow, denylist refusals, preflight verdicts) | hermes evals |
| S5 | `REGRESSION.md` — append-only ledger: date, symptom, root cause, fix commit, regression test | ecc convention |
| S6 | Introspection report — failed/parked runs auto-attach a diagnostic card (last events, tool failures, pressure stats) | hermes introspection |

Non-goals: plugin process isolation, skill catalog v2, chat channels (P2).

## 3. Requirements

### Slice E — Hook engine

#### RF-E01: Hook model
`IChatHook { string Event; string Matcher; Task<HookVerdict> RunAsync(HookContext, ct) }`
verdicts `allow|block|warn`; `block_on_failure` on the hook record. Registry:
`IChatHookRegistry` resolves hooks for an event.

#### RF-E02: Lifecycle events
Minimum: `tool.before` (name+args), `tool.after` (name+result head), `run.start`,
`run.end`, `mcp.call`. Events carry the tool name for matcher filtering.

#### RF-E03: Config + first ports
`Taskboard:Chat:Hooks:Enabled` (default on). Built-ins:
- `config-protection` — `tool.before` matcher `write_file|edit_file`: blocks paths
  under `Taskboard:` config storage/secrets files (delegates to denylist verdict).
- `mcp-health-check` — `mcp.call` matcher `mcp_*`: warn when the server health is
  degraded (surfaces a status note, never blocks).

### Slice F — Memory consolidation

#### RF-F01: Consolidation job
`MemoryConsolidationJob` (managed job infra): dedupe identical facts, expire facts
older than `Taskboard:Chat:Memory:MaxAgeDays` (default 90), cap at
`MaxFacts` (default 500, LRU drop). Manual trigger + daily schedule.

#### RF-F02: Report
Job output = `{ kept, deduped, expired, evicted }`; a summary line lands in the
job result for the cockpit.

### Slice G — Delegation contract v3

#### RF-G01: `output_schema`
`delegate_task` accepts optional `output_schema` (JSON schema string). The worker
brief declares the contract; the completion result carries `schema_valid: bool` —
`false` never discards the child's output, it annotates it.

#### RF-G02: Roles + model route
`role: orchestrator|leaf` on the task (leaf tasks cannot delegate further —
depth already enforced; role makes it contractual). `model` override per task
routes through `IAgentLaunchResolver` (existing effective-model path).

### Slice H — Evals + regression feedback

#### RF-H01: `evals/` suite
`tests/Taskboard.Tests.Unit/Evals/` — deterministic provider-fake scenarios:
deferred discover→activate flow, credential-path refusal, preflight verdict
mapping. No live network.

#### RF-H02: `REGRESSION.md`
Root file, append-only rows: `date | symptom | root cause | fix commit | regression test`.
Seeded with the two flakes found during P0 (ChatJobs settle race, ManagedJob trigger race).

### Slice I — Introspection report

#### RF-I01: Run diagnostics
Failed/cancelled `ChatRun`/`DelegationTask` auto-attaches a diagnostics card:
tool failures (name+reason), last N status events, pressure/compaction stats,
error class. Surfaced via the existing run-detail DTO + cockpit panel.

## 4. Acceptance criteria

- [x] `write_file` on a protected config path → hook blocks with `config-protection` verdict before the tool executes; `Taskboard:Chat:Hooks:Enabled=false` disables the engine.
- [x] Consolidation job on a seeded store → dedupe+expiry counts reported; facts capped at `MaxFacts`.
- [x] `delegate_task` with `output_schema` + a worker returning non-conforming JSON → parent sees `schema_valid:false` with the full child output intact.
- [x] `dotnet test --filter Evals` runs the scenario suite offline.
- [x] `REGRESSION.md` has the two seeded rows and the append convention documented.
- [x] A failed chat run shows a diagnostics card with the failing tool + pressure stats.

## 5. Task plan

- [x] **T5 (Slice E)** — hook contracts + registry + lifecycle emission + 2 built-in hooks. Tests first.
- [x] **T6 (Slice F)** — consolidation job + config + report. Tests first.
- [x] **T7 (Slice G)** — `output_schema`/`role`/`model` on delegate_task + contract validation. Tests first.
- [x] **T8 (Slice H)** — `evals/` scenarios + `REGRESSION.md` seed. Tests first.
- [x] **T9 (Slice I)** — run introspection card + cockpit surface. Tests first.

Each slice is an independent PR; order T5→T9.
