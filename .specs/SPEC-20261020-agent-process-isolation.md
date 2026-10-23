# SPEC-20261020-agent-process-isolation

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-process-isolation` |
| Type | `Infra` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `devin/20261020-prod-oom-hardening` |
| Ticket | [#577](https://github.com/afonsoft/agent-harness/issues/577) |
| Status | `Done` |

## 1. User Story

**As a** Harness operator
**I want** agent CLI processes (PTY terminals, delegated agent runs) isolated in their own cgroup with their own memory budget, and new sessions refused under sustained memory pressure
**So that** a runaway `node` agent triggers an OOM kill only inside its own scope — the server, other sessions and SignalR connections keep running instead of the whole `harness-server.service` unit restarting.

**Problem context:** Production logs (2026-10-20) show repeated memcg OOM kills in `harness-server.service`: kernel killed `node` children at 1.4–1.8GB anon RSS, `OOMPolicy=stop` tore down the whole unit (~40s downtime, all terminal sessions dropped). 4 oom-kills and 8 Kestrel "thread pool starvation" heartbeat stalls in 7 days — stalls correlate with the cgroup crossing `memory.high` and entering direct reclaim. Root cause: `PtySession` and `StreamingProcessRunner` spawn `script`/`bash`/agent CLIs as plain children inside the server cgroup — no isolation exists. An ops quick-fix (`OOMPolicy=continue`, `MemoryMax=6G`, `MemoryLow=1.5G`) was applied to the unit on 2026-10-20; this SPEC is the structural fix. The env var `MEMORY_PRESSURE_WATCH` is already exported to the service but unused in `src/`.

## 2. Scope

**In scope:**
- `IProcessIsolator` abstraction + Linux implementation that wraps child spawns in `systemd-run --user --scope` (per-process transient scope with own `MemoryMax`/`MemoryHigh`/`OOMScoreAdjust`).
- Transparent fallback to direct `Process.Start` when not on Linux, not under systemd, or `systemd-run` unavailable — never breaks spawn.
- Apply the wrapper at both spawn surfaces: `PtySession.Start()` (interactive terminals incl. agent CLIs) and `StreamingProcessRunner.RunAsync` (agent runs/delegation).
- PSI memory-pressure gate: refuse creation of new terminal/agent sessions when the service cgroup's `memory.pressure` `some avg60` exceeds a configured threshold; explicit error to the caller (HTTP 429-equivalent contract).
- Configuration knobs for isolation budget and PSI threshold.
- Observability: log scope creation/fallback, log gate refusals, expose current PSI + cgroup `memory.current`/`memory.events` on a diagnostics endpoint or existing health surface.

**Out of scope:**
- systemd unit changes already applied manually (`OOMPolicy=continue` etc.) — documented as prerequisite, not implemented by this SPEC.
- Containerizing agents, Windows/macOS resource isolation (fallback = direct spawn, documented degradation).
- Killing/shrinking already-running sessions under pressure (detection + refusal only).
- The DevinCliDbExtractor size-cap fixes — separate SPEC (`SPEC-20261020-clidb-large-db-resilience`).

## 3. Technical Context

**Where the change happens:** `Taskboard.Integrations` (process spawning) + `Taskboard.Server` (session endpoints, DI wiring, config). No Domain/EF schema changes.

**Files to read before implementing:**
- `src/Taskboard.Integrations/Terminal/PtySession.cs` — spawns `script -qfc "… exec bash -l"` (lines ~90–124)
- `src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs` — `ProcessStartInfo(executable)` (lines ~33–60)
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — session lifecycle, orphan/idle reaper
- `src/Taskboard.Server/Program.cs` — `PtySessionFactory` registration (~line 1094), session endpoints
- `src/Taskboard.Integrations/Agents/LocalCliAgentAcpClient.cs`, `AcpSessionClient.cs` — verify whether they spawn via `StreamingProcessRunner` or their own `ProcessStartInfo` (all spawn surfaces must be covered)
- `~/.config/systemd/user/harness-server.service` + `.d/override.conf` — prod unit context (read-only reference)

**Files to create or modify:**
```text
src/Taskboard.Integrations/Execution/IProcessIsolator.cs            (new)
src/Taskboard.Integrations/Execution/SystemdScopeProcessIsolator.cs (new)
src/Taskboard.Integrations/Execution/NoopProcessIsolator.cs         (new — fallback)
src/Taskboard.Integrations/Execution/MemoryPressureGate.cs          (new — PSI reader + gate)
src/Taskboard.Integrations/Terminal/PtySession.cs                   (modify — wrap spawn)
src/Taskboard.Integrations/Agents/StreamingProcessRunner.cs         (modify — wrap spawn)
src/Taskboard.Server/Program.cs                                   (modify — DI, config, gate call sites)
tests/Taskboard.Tests.Unit/Integrations/Execution/*               (new tests)
```

## 4. Requirements

### RF-001: Spawn isolation via systemd-run scope
- **Description:** When `Harness:ProcessIsolation:Enabled=true` (default true) and `systemd-run` + a user systemd manager are available, agent/PTY child processes must run inside a transient scope `harness-agent-<shortId>` with `MemoryMax`/`MemoryHigh` via `-p` and `OOMScoreAdjust` applied by a `choom -n` wrapper inside the scope (OOMScoreAdjust is Exec-context-only — `-p` on a scope makes `systemd-run` exit 1 and kills the spawn; observed in prod 2026-10-23) (defaults: `MemoryMax=2G`, `MemoryHigh=1.5G`, `OOMScoreAdjust=500`).
- **Rules:** argv passed verbatim after `--` (no shell interpolation); working directory honored (`--same-dir`/equivalent or explicit `cd` wrapper); scope cleanup via `--collect`; `OOMScoreAdjust` chosen so the kernel prefers killing the agent over the server.
- **Input → Output:** spawn request (executable + argv + workdir + env) → `Process` handle identical in behavior to today's direct spawn (stdin/stdout/stderr redirection preserved).

### RF-002: Transparent fallback
- **Description:** Non-Linux hosts, missing `systemd-run`, or scope creation failure → `NoopProcessIsolator` (direct `Process.Start`) with a single Warn log per boot; spawn must never fail because isolation failed.
- **Rules:** capability probe at startup (detect systemd user session: `systemctl --user is-system-running` or `sd_booted`/`XDG_RUNTIME_DIR` heuristics); per-spawn scope-creation failure falls back for that process only.

### RF-003: Coverage of all spawn surfaces
- **Description:** `PtySession.Start()` and `StreamingProcessRunner.RunAsync` must route through `IProcessIsolator`. Any other `ProcessStartInfo`/`Process.Start` agent-spawn surface found in `Taskboard.Integrations/Agents` (e.g. `LocalCliAgentAcpClient`, `AcpSessionClient`) must be covered or explicitly documented as exempt (short-lived probes like `opencode models` may be exempt — decide during T1 and record in the SPEC).

### RF-004: PSI memory-pressure gate
- **Description:** Before creating a terminal session or an agent run, read the service cgroup's `memory.pressure` (path from `MEMORY_PRESSURE_WATCH` env when set, else resolve own cgroup via `/proc/self/cgroup` → `/sys/fs/cgroup`). If `some avg60` > `Harness:MemoryPressure:SomeAvg60Max` (default `30.00`), refuse with a structured error and Warn log. When the pressure file is unreadable/absent → allow (fail-open).
- **Input → Output:** session-create request under high PSI → refusal `{ "error": "MEMORY_PRESSURE", "message": "…" }` mapped to HTTP 429 for REST and an explicit failure for SignalR/hub paths.

### RF-005: Observability
- **Description:** Log (Info) each scope spawn with unit name + limits; Warn on fallback; Warn on every gate refusal including PSI values. Diagnostics endpoint (or existing health/config surface) exposes `memory.current`, `memory.high`, `memory.events` counters (`oom`, `oom_kill`, `high`) and last PSI sample.

### RF-006: Configuration
- **Description:** New config section `Harness:ProcessIsolation` (`Enabled`, `MemoryMax`, `MemoryHigh`, `OomScoreAdjust`, `ScopePrefix`) and `Harness:MemoryPressure` (`SomeAvg60Max`, `PressureFile` override). All documented in `appsettings.json` comments + `docs/`.

**Business rules / invariants:**
- Isolation failure must never break session creation.
- The server process itself must never be moved into a child scope.
- Gate is fail-open (unreadable PSI → allow) but logs the read failure once.

## 5. API Contract

Session-creation endpoints gain one possible refusal:

**POST `/api/…sessions…` / hub invocations**
**Error:** `429` — `{ "error": "MEMORY_PRESSURE", "detail": "server under memory pressure; retry later" }`

No other contract changes.

## 6. Acceptance Criteria

- [ ] **Given** Linux + systemd user session + isolation enabled, **when** a terminal session starts, **then** `systemd-cgls` shows the `script`/bash/agent process inside `harness-agent-*.scope`, not in the server cgroup.
- [ ] **Given** a scope whose child exceeds its `MemoryMax`, **when** the kernel OOM-kills it, **then** the server stays `active (running)` and the terminal session reports exit — verified via journal (no `Failed with result 'oom-kill'` on the unit).
- [ ] **Given** `systemd-run` missing or failing, **when** a session starts, **then** it spawns directly and works as today.
- [ ] **Given** `some avg60` > threshold (simulated via `PressureFile` pointing to a fixture file), **when** a create-session request arrives, **then** it is refused with `MEMORY_PRESSURE` and a Warn is logged.
- [ ] **Given** PSI file absent, **when** a session is created, **then** it succeeds (fail-open).
- [ ] **Given** the diagnostics endpoint, **when** queried, **then** it returns current cgroup memory stats.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `systemd-run` exists but returns non-zero | spawn request | fallback to direct spawn + Warn |
| Non-Linux host | spawn request | Noop isolator, zero log noise beyond one-time Info |
| PSI file readable but unparsable | create session | allow + single Warn |
| Scope name collision | two sessions same instant | unique scope names (guid suffix) |

## 7. Task Plan

- [ ] **T1 — Discovery:** read section 3 files; enumerate every agent-spawn surface (decide exemptions for short-lived probes like `opencode models`); check systemd version on target (`systemd-run --user --scope` flags).
- [ ] **T2 — Implementation:** `IProcessIsolator` + systemd/noop impls; wire `PtySession` + `StreamingProcessRunner`; `MemoryPressureGate` + config + gate calls at session-creation endpoints; diagnostics surface.
- [ ] **T3 — Verification:** unit tests for isolator selection/fallback (mock command runner), PSI parsing (fixture files), gate behavior; integration test that a spawned scope appears under `--user` scopes (marked Linux-only).
- [ ] **T4 — Validation:** `dotnet build` (TreatWarningsAsErrors), `dotnet test`, coverage ≥ gate.
- [ ] **T5 — Done + PR** per template flow.

## 8. Organization Guardrails

- Branch `devin/20261020-prod-oom-hardening`; never `main`/`master`/`develop`.
- No `.github/workflows` changes.
- No secrets in logs; PSI/cgroup data contains no PII.
- Scope: no container rework, no unit-file shipping in this SPEC (ops override already applied manually; document required unit settings in `docs/`).

## 9. Definition of Done

- [ ] All RFs implemented.
- [ ] Acceptance criteria covered by tests.
- [ ] `dotnet build` + `dotnet test` green; coverage gate ≥ current threshold.
- [ ] Docs updated (`docs/` — ops section with required unit overrides).
- [ ] SPEC Status → `Done` on completion; PR opened.

## Open Questions / Pending Ambiguity

- Confirm all spawn surfaces during T1 (`LocalCliAgentAcpClient`, `AcpSessionClient`, `ShellExecTool`) — any spawn outside the two known surfaces must be listed in the SPEC update before T2.

## Implementation Notes (2026-10-22)

**Delivered:** `IProcessIsolator` + `SystemdScopeProcessIsolator` (`systemd-run --user --scope --collect --quiet`, per-scope `MemoryMax`/`MemoryHigh` via `-p` + `OOMScoreAdjust` via in-scope `choom`) + `NoopProcessIsolator` fallback; `MemoryPressureGate` (PSI `some avg60`, fail-open, rising-edge Warn) + `GET /api/diagnostics/memory` (`memory.current|high|max|events` + PSI sample). Config: `Harness:ProcessIsolation:{Enabled,MemoryMax,MemoryHigh,OomScoreAdjust,ScopePrefix}`, `Harness:MemoryPressure:{SomeAvg60Max,PressureFile}`; `Harness:MemoryPressure:PressureFile` overrides `MEMORY_PRESSURE_WATCH` env (explicit config wins), which overrides `/proc/self/cgroup` self-resolution.

**Spawn coverage (T1 inventory):**

| Surface | Decision |
|---|---|
| `PtySession.Start()` (interactive terminals, agent CLIs) | **Wrapped** |
| `StreamingProcessRunner.RunAsync` (delegation agent runs, CLI/code-server installs) | **Wrapped** |
| `LocalCliAgentAcpClient` (long-lived agent process) | **Wrapped** |
| `AcpSessionClient` (long-lived ACP agent session) | **Wrapped** |
| `JsonRpcAcpClient` (one-shot ACP agent run) | **Wrapped** |
| `ShellExecTool`, `GitCommandRunner`, `GitRunner`, `ISkillsInstallRunner`, `SessionFileActions`, `AgentTranscriptSearcher` | **Exempt** — short-lived probes/tool calls bounded by their own timeouts; wrapping each in a scope adds latency without isolation value. |
| `DockerCliSpawner`/`docker exec` paths | **Exempt** — container runtime applies its own cgroup limits. |

**Gate points:** `TerminalSessionManager.OpenAsync` refuses with `MEMORY_PRESSURE` (rebinds exempt — they create nothing); `DelegationDispatcherService.TickAsync` skips the dispatch cycle under pressure so ready tasks stay queued rather than failing.
