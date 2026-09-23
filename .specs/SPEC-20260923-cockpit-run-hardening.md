# SPEC-20260923-cockpit-run-hardening

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cockpit-run-hardening` |
| Type | `Feature` + `Bugfix` (API + Domain + Engine + Blazor UI) |
| Stack | `Blazor WASM / .NET 10 / ABP / EF Core SQLite / SignalR / xterm.js` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260923-cockpit-run-hardening` |
| Status | `Done` |
| Referência | SPEC-20260919-ade-multi-agent-orchestration, SPEC-20260919-ade-cockpit-hitl, SPEC-20260919-harness-workspace-isolation, SPEC-20260920-cockpit-pause-resume, SPEC-20260921-agent-execution-event-pipeline, SPEC-20260921-cockpit-live-logs-explorer-diff, SPEC-20260922-cockpit-agent-selection-fallback, SPEC-20260917-terminal-tabs |
| Evidence | `pipe_0bfefc2f06fb41a0a7b33259f34dca9c` (AwaitingRetry), `pipe_1a2e329b0b814bd5b01e3c3cf71002cc` (Completed) |

## 1. User Story

**As a** Harness operator running pipelines from the Cockpit
**I want** the run to clone the repository into `~/repos` when it is missing,
get a push-style notification (with a summary) when a stage needs my
approval, see rich persisted run logs (not only `output` events), have failed
stages retried automatically across agent CLIs, watch the page update in
real time, and open a terminal inside the run's worktree
**So that** a run never silently dies on a missing clone, I am not chained
to F5, and failures surface their root cause without me babysitting the
pipeline.

**Problem context — reproduced from the live database:**

1. **Missing clone produces a worktree of the wrong repo.**
   `pipe_0bfefc2f06fb41a0a7b33259f34dca9c` targeted `afonsoft/Afonsoft.EFCore`,
   which is **not cloned** locally. `POST /api/harness/runs` resolved the
   path via `WorkspaceService.ResolveCardWorkdir`, which returns the
   workspace **root** (`~/repos`) when `~/repos/<name>` does not exist.
   `~/repos` itself is a git clone (of `afonsoft/LangGraph-UI`), so
   `GitWorktreeManager` happily created `~/repos/pipe_0bfefc2f…` as a
   worktree **of LangGraph-UI** — the stages ran against the wrong codebase
   and the verifier failed with `No .sln/.slnx found in the run worktree.`
2. **`AwaitingRetry` waits forever.** `RecomputeStatus` parks the execution
   in `AwaitingRetry` on any stage failure; only the manual
   `POST …/stages/{key}/retry` endpoint unblocks it. There is no automatic
   retry, no agent rotation over time, and no terminal failure state — a
   run can sit in `AwaitingRetry` indefinitely.
3. **Approvals are invisible unless the run page is open.** The only
   surface is the SignalR `RequireApproval` → `ApprovalGateModal` on
   `CockpitRun`. No toast, no browser notification, no list-page indicator;
   the request payload carries no summary of what is being approved.
4. **Logs are thin.** `AgentRunEvents` for the run contain only
   `lifecycle` (title-only, no payload), `approval` and `output` rows —
   `tool_call`/`plan`/`metric`/`error` are never emitted by the pipeline
   path, stage failures publish no `error` event, and the verifier's
   no-solution early return skips even its `verification` event.
   `GET /api/harness/runs/{id}/events` serves only the volatile in-memory
   cockpit buffer (500-cap, lost on restart) — the persisted
   `AgentRunEvents` table is never read back for the run.
5. **No realtime refresh.** `CockpitRun` only re-fetches `_details` on
   `kind == "status"` events — and the engine only publishes `status` on
   pause/resume. Stage transitions and completion never refresh the status
   badge/stage strip; the `/cockpit` list page has no hub subscription at
   all. F5 is the only update path.
6. **No terminal on the run screen.** `TerminalHub.Open(repo)` resolves a
   *clone* cwd; there is no way to open a PTY anchored at the run's
   worktree to follow the process hands-on.

## 2. Scope

**In scope:**

- Server-side repository provisioning: clone into `~/repos/<name>` on run
  start when missing; validate existing clones; never dispatch a run whose
  `RepositoryPath` is the workspace root or a foreign repo (RF-001).
- Automatic retry of failed stages: N attempts per agent CLI spaced by a
  configurable interval, then rotate to the next untried eligible CLI;
  terminal `Failed` status with full error detail on exhaustion (RF-002).
- Realtime status: `run_status` cockpit events on every execution/status
  transition, a global `runs` group for the list page, and `_details`
  refresh on stage/lifecycle events (RF-003).
- Richer run events: payloads on `lifecycle`, `error` on failures,
  `metric` on usage, `verification` on the early-return path, clone/worktree
  provisioning events; `GET /runs/{id}/events` serves the persisted stream
  (RF-004).
- Approval notifications: browser Notification + toast + summary payload +
  pending-approval indicator on the runs list (RF-005).
- `Terminal` tab on the run page: interactive PTY anchored at the run
  worktree via a new `OpenForRun` hub method (RF-006).
- Tests (RF-007).

**Out of scope:**

- Web Push (VAPID/service-worker push while the app is closed) — the spec
  delivers in-app + browser notifications for open/background tabs only.
- Pipeline template authoring, parallel stages, per-stage budgets.
- Cancelling/cleaning the `~/repos` clone itself (repos are shared
  workspace state; only run worktrees are lifecycle-managed).
- Re-running `pipe_0bfefc2f…` — this spec fixes provisioning so the next
  run clones `afonsoft/Afonsoft.EFCore` correctly.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Server/Program.cs` — `POST harness/runs` and
  `POST harness/pipelines/start`: `RepositoryPath` must be resolved and
  provisioned **server-side** (today `pipelines/start` trusts the client
  value verbatim and `runs` falls back to the workspace root).
  `GET harness/runs/{id}/events` switches from the volatile buffer to the
  persisted `AgentRunEvents` stream (or merges both).
- `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` —
  `StartAsync` calls the provisioning service before
  `PipelineExecution.Create`; status-changing methods publish `run_status`.
- `src/Taskboard.Application/Harness/PipelineEngine.cs` —
  `DispatchCoreAsync` gains the auto-retry sweep for `AwaitingRetry`
  executions (it already scans non-terminal executions each 15 s tick);
  `RunAgentStageAsync` runs **one** bound agent per dispatch (the
  same-tick CLI sweep is superseded by RF-002 rotation); richer event
  emission throughout.
- `src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs` —
  `RecomputeStatus` handles `Failed` as terminal; new `Fail(reason, now)`.
- `src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs` —
  `AutoRetryCount`, `NextAutoRetryAtUtc`, `ScheduleAutoRetry`/`RotateAgent`
  semantics reusing `Retry`/`BeginFallbackAttempt`.
- `src/Taskboard.Domain.Shared/Harness/PipelineStatus.cs` — new `Failed`.
- `src/Taskboard.EntityFrameworkCore` — additive migration
  `AddPipelineStageAutoRetry` (`AutoRetryCount` int default 0,
  `NextAutoRetryAtUtc` TEXT null).
- `src/Taskboard.Integrations/Harness/` — new
  `RepositoryProvisioningService` over `IGitCommandRunner`
  (`clone`/`remote get-url`); registration in `Program.cs`.
- `src/Taskboard.Server/Hubs/HarnessCockpitHub.cs` — `JoinRunsGroup`/
  `LeaveRunsGroup`; `CockpitEventStream` dual-publishes run events to the
  run group and the `runs` group.
- `src/Taskboard.Server/Hubs/TerminalHub.cs` — `OpenForRun(runId)`
  resolving `IWorktreeSessionRepository.GetByRunIdAsync` → `session.Path`
  (server-side only, confined to the worktree root).
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — refresh
  `_details` on `stage`/`status`/`verification`/`error`/`run_status`
  events; new `Terminal` tab; notification trigger on `RequireApproval`.
- `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — hub connection
  to `harness-cockpit-hub` `runs` group; in-place row updates; pending
  approvals chip.
- `src/Taskboard.Client/wwwroot/js/taskboard.js` — `taskboardNotify`
  helper wrapping the Notification API (permission, show, click-to-focus).
- `src/Taskboard.Application.Contracts/Harness/CockpitDtos.cs` —
  `ApprovalRequestDto` gains `Summary`.

**Files to read before implementing:**

- `PipelineEngine.cs` — `DispatchCoreAsync` (active-exec scan),
  `RunAgentStageAsync` (fallback loop ~line 276-463), `PublishAsync`/
  `PublishApprovalAsync` (event mapping).
- `PipelineExecutionAppService.cs` — `StartAsync`, `RetryStageAsync`,
  `Approve/RejectStageAsync`.
- `WorkspaceService.ResolveCardWorkdir` + `WorkspacePaths.RepoWorkdir` —
  the root-fallback to eliminate on the run path.
- `GitWorktreeManager.CreateWorktreeAsync` — expects a real clone at
  `RepositoryPath`; the "not a stale worktree" guard already exists.
- `PipelineExecution.cs`/`PipelineStageExecution.cs` — `RecomputeStatus`,
  `Retry`, `BeginFallbackAttempt`, `TriedAgents`.
- `CockpitEventStream.cs`, `HarnessCockpitHub.cs`, `TerminalHub.cs`,
  `TerminalSessionManager.cs`.
- `CockpitRun.razor`, `Cockpit.razor`, `RunTerminal.razor`,
  `Terminal.razor` (tab/PTY patterns to reuse), `AgentRunTimeline.razor`.
- `tests/Taskboard.Tests.Unit/Harness/*`,
  `tests/Taskboard.Tests.Integration/CockpitEndpointsTests.cs`.

**Existing pieces reused as-is:**

- `IGitCommandRunner.RunAsync` — shell-free git execution (clone, remote
  get-url, rev-parse).
- `IWorktreeSessionRepository`/`GitWorktreeManager` — worktree sessions,
  stale-dir guards, diff/push.
- `AgentExecutionEventSink` — normalized durable events + broadcast;
  `EmitNormalized` mirrors run events to the bound issue scope.
- `IAgentEligibilityService` — the eligible-CLI set for rotation.
- `PipelineStageExecution.TriedAgents`/`Attempts` — retry accounting.
- `taskboardTerminal` JS + `TerminalHub` PTY plumbing — the run terminal
  is a new entry point over the same machinery.

## 4. Requirements

### RF-001: Clone-and-resolve on run start

- **Description:** starting a run guarantees a real clone of
  `RepositoryFullName` at `<workspaceRoot>/<repo-name>` and passes that
  clone path to the worktree stage — never the bare workspace root.
- **Rules:**
  - New `IRepositoryProvisioningService.EnsureCloneAsync(repositoryFullName,
    ct)` → absolute clone path. Resolution order:
    1. `~/repos/<name>` exists, is a git repo, and `git remote get-url
       origin` matches `*github.com[:/]owner/name(.git)` (case-insensitive)
       → reuse it (`git fetch` is **not** forced — reuse as-is);
    2. path missing or empty dir → `git clone
       https://github.com/<owner>/<name>.git ~/repos/<name>` (timeout
       5 min) → return path;
    3. path exists but is a foreign repo / non-repo with content →
       `422 InvalidValue` naming the conflict — never delete or reuse it.
  - `POST /api/harness/runs` and `POST /api/harness/pipelines/start` both
    call it server-side; a client-supplied `RepositoryPath` that disagrees
    with the resolved clone is ignored (log + `activity` event). The
    workspace root itself (`~/repos`) is **never** a valid repository
    path for a run.
  - Provisioning happens inside `StartAsync` *before* dispatch so clone
    failure → `422` with the git error, not a stuck run. Clone start/done/
    fail emit normalized `lifecycle` events (scope `run`) with payload
    `{url, path, elapsedMs}` — the events are emitted after the execution
    row exists, ordered before the first stage.
  - The worktree keeps its current shape: `~/repos/pipe_<id>` worktree of
    the clone on branch `feature/agent-pipe_<id>-<template>`; agent stages
    run with `RepoPath = WorktreePath` (unchanged behavior).
- **Input → Output:** `RunStartRequest`/`PipelineStartRequest` →
  execution with `RepositoryPath = ~/repos/<name>` (real clone) → worktree
  from that clone.

### RF-002: Automatic retry with agent rotation and terminal failure

- **Description:** a stage failure no longer parks the run — the engine
  retries it on a schedule, rotates the agent CLI after a per-agent
  attempt budget, and only then fails the run with the full error detail.
- **Rules:**
  - New config `Taskboard:Pipelines:AutoRetry` —
    `Enabled` (default `true`), `AttemptsPerAgent` (default `5`),
    `Interval` (default `00:01:00`), applied per stage.
  - `PipelineStageExecution` gains `AutoRetryCount` (int, per current
    agent) and `NextAutoRetryAtUtc` (nullable). EF migration
    `AddPipelineStageAutoRetry` (additive; existing rows read as
    `0`/null → no auto-retry for runs that predate the feature is the
    accepted default — see edge cases).
  - `PipelineStatus` gains `Failed` (terminal, like `Completed`/
    `Cancelled`); `PipelineExecution.Fail(reason, now)` sets
    `Status = Failed`, `CompletedAtUtc`, skips Pending stages; new
    `FailureReason` string column on the execution for the summary line.
  - Engine tick (`DispatchCoreAsync`, executions already include
    `AwaitingRetry`): for each `Failed` stage —
    - `NextAutoRetryAtUtc` null → set `now + Interval`, persist, emit
      `lifecycle` `auto-retry scheduled (attempt 1/N on {agent})`.
    - `NextAutoRetryAtUtc > now` → skip (still waiting).
    - due → `RetryStage`-equivalent: stage → `Pending` → dispatched in the
      same tick on the **same** bound agent.
  - Each new failure of that stage: `AutoRetryCount++`. When
    `AutoRetryCount >= AttemptsPerAgent` → rotate: next `AgentType` that is
    eligible **and** not in `TriedAgents` → `BeginStageFallback(next)` +
    `AutoRetryCount = 0` + `NextAutoRetryAtUtc = now + Interval` + cockpit/
    normalized event `stage '{name}' — {old} exhausted {N} retries ({lastError}); rotating to {new}`.
  - Exhaustion: no untried eligible CLI remains → `exec.Fail(
    $"Stage '{key}' failed after {Attempts} attempt(s) across agents
    [{tried}]. Last error: {lastError}", now)` → `run_status` + `error`
    event with `{stageKey, lastError, triedAgents, attempts}`; the run
    stops (terminal).
  - The same-tick multi-CLI sweep in `RunAgentStageAsync` (SPEC-20260922
    RF-003) is **superseded**: a dispatch runs the bound agent only (the
    model-flag same-CLI retry of RF-004 is preserved). `TriedAgents` still
    records every agent that ran; `Attempts` still counts dispatches.
  - Manual `POST …/stages/{key}/retry` keeps working and resets
    `TriedAgents` + `AutoRetryCount`/`NextAutoRetryAtUtc` (existing
    semantics + counters).
  - `Paused`, `Cancelled`, `Completed`, `Failed` are never auto-retried;
    pause stops the clock by skipping the exec entirely (existing
    behavior).
  - Verification-kind stages participate in auto-retry too (transient
    build failures are real), but they never rotate agent — they retry the
    same step `AttemptsPerAgent` times then fail the run.
- **Input → Output:** stage failure → up to `AttemptsPerAgent` spaced
  retries per CLI → rotation → terminal `Failed` with detail, or recovery
  to `Running`/`Completed`.

### RF-003: Realtime run updates

- **Description:** cockpit pages reflect status changes without F5.
- **Rules:**
  - `PipelineEngine`/`PipelineExecutionAppService` publish a cockpit
    `run_status` event (`title = "Run {status}"`, payload `{status,
    completedAtUtc?}`) on **every** execution-level transition: created →
    Running, → WaitingApproval, → AwaitingRetry, → Running (resume/retry),
    → Completed, → Cancelled, → Failed, → Paused. Same event mirrored as a
    normalized `lifecycle` event with payload.
  - `HarnessCockpitHub` gains `JoinRunsGroup()`/`LeaveRunsGroup()`;
    `CockpitEventStream` publishes every run-scoped event to **both** the
    run group and the `runs` group (payload unchanged).
  - `CockpitRun.razor`: `_details` re-fetch on `stage`, `status`,
    `verification`, `error`, `approval` and `run_status` events (debounced
    ~500 ms to collapse bursts; `agent_output` never triggers a fetch).
    Status badge, stage strip, control bar and the live badge update live.
  - `Cockpit.razor` (list): connects to the hub, joins `runs`, and on
    `run_status`/`stage`/`approval` events updates the matching row in
    place (status badge, completed-stage count) — re-fetching the single
    run DTO on `run_status` only; a run not in the list triggers one list
    refresh. Disconnect falls back to today's manual Refresh.
- **Input → Output:** status transition → SignalR event → badge/list row
  update without reload.

### RF-004: Richer persisted run events

- **Description:** the normalized `AgentRunEvents` stream (and the events
  endpoint) tells the full story, not just `output`.
- **Rules:**
  - `lifecycle` events carry `PayloadJson`
    `{stageKey, name, agent?, model?, attempt, elapsedMs?}` for start /
    complete / scheduled-retry / rotate / dispatch.
  - Stage failure emits an `error` event: `{stageKey, agent, attempt,
    exitCode?, error}` where `error` = `LastError` (redacted by the sink as
    usual).
  - Each billed attempt emits a `metric` event `{tokensIn, tokensOut,
    costUsd, cumulativeUsd, agent, model?}` right after
    `RecordUsageAsync`.
  - Bugfix: `RunVerificationStageAsync` emits the `verification` event on
    the no-solution early return too (today it returns before publishing).
  - Clone/worktree provisioning emits `lifecycle` events (`clone started`,
    `clone done in {elapsed}`, `worktree attached at {path}`).
  - `GET /api/harness/runs/{id}/events` serves the persisted
    `AgentRunEvents` (scope `run`, ordered by `Sequence`, same pagination
    shape as `agents/events`) **merged with** the live cockpit buffer for
    kinds that only exist there (`agent_output` chunks) — dedup by
    timestamp+kind+title as the client already does. The run's event list
    survives server restart.
  - `RunTerminal` (Logs tab) gains a "source" that replays persisted
    events through the same render path, so old runs show full logs.
- **Input → Output:** every state change produces a persisted, payload-
  bearing event; the events endpoint returns the durable stream.

### RF-005: Approval notifications with summary

- **Description:** an approval gate notifies the operator even when the
  run page is not focused, and says *what* is being approved.
- **Rules:**
  - `ApprovalRequestDto` gains `Summary` — composed at publish time:
    stage name + the `HandoffSummary` of the stage that produced the gate's
    input (e.g. the plan the architect produced) + repo + run short id.
  - `PublishApprovalAsync` also emits a `run_status` `WaitingApproval`
    event (RF-003 covers the transition).
  - `CockpitRun`/`Cockpit`: on `RequireApproval` — (a) existing modal,
    (b) toast with the summary, (c) browser Notification via a new
    `taskboardNotify` JS helper (`Notification.requestPermission` lazily
    on first cockpit visit; click focuses the tab and navigates to the
    run). Permission denied/absent → toast-only, no error.
  - `Cockpit` list: `WaitingApproval` rows get a `Needs approval` badge;
    the page header shows a pending-approvals chip with the count.
- **Input → Output:** stage gate → notification (modal + toast + browser
  notification) containing the approval summary.

### RF-006: Terminal tab anchored at the run worktree

- **Description:** the run page offers an interactive terminal that opens
  inside the run's worktree so the operator can follow/inspect the process
  hands-on.
- **Rules:**
  - `TerminalHub` gains `OpenForRun(string runId)`: resolves the worktree
    session server-side (`IWorktreeSessionRepository.GetByRunIdAsync`) →
    `session.Path`, verified `WorktreePaths.IsUnder(root, path)`; missing
    session → `HubException("Run has no worktree")`. Same
    `TerminalSessionManager`/`PtySession` machinery, same session caps and
    orphan/reattach semantics as `Open`.
  - `CockpitRun` adds a `Terminal` tab (next to `Logs`): reuses the
    `taskboardTerminal` xterm plumbing and the `/terminal-hub` connection
    pattern from `Terminal.razor`, opened via `OpenForRun(RunId)`; title
    `run <shortId>`.
  - Read-only `Logs` tab unchanged in purpose (stream view); the new tab
    is the interactive one.
- **Input → Output:** tab click → live bash PTY whose cwd is the run
  worktree.

### RF-007: Tests

- **Rules:**
  - Unit (BDD pt-BR): provisioning resolution matrix (reuse/missing/
    foreign/invalid name), auto-retry scheduling, per-agent attempt
    counting, rotation order, exhaustion → `Failed` + `FailureReason`,
    verification-stage retry without rotation, manual-retry reset,
    `RecomputeStatus` with `Failed`.
  - Engine-level: fake `IAgentAcpClient` failing N times then succeeding
    asserts retry→complete; fake eligibility with 2 CLIs asserts rotation;
    all-agents-exhausted asserts `Failed`.
  - Integration: `POST /api/harness/runs` for a repo without clone →
    `RepositoryPath` = new clone path (provisioner mocked/fake git); clone
    failure → 422; `GET /runs/{id}/events` returns persisted rows after
    restart; `TerminalHub.OpenForRun` rejects unknown/missing worktree.
  - Component-level coverage where test infra allows (status badge
    `Failed`, approval summary render).

## 5. API Contract

```http
POST /api/harness/runs            # unchanged shape — RepositoryPath resolved+provisioned server-side
POST /api/harness/pipelines/start # RepositoryPath now server-resolved (client value advisory)
→ 201 | 400 | 422 clone failed / foreign dir / no eligible CLI

GET /api/harness/runs/{id}/events
→ 200 { events: [...persisted AgentRunEvents + live buffer...], nextAfter, hasMore }

POST /api/harness/runs/{id}/stages/{stageKey}/retry   # also resets auto-retry counters
```

```csharp
// Hub
TerminalHub.OpenForRun(string runId) → Task<string>  // sessionId
HarnessCockpitHub.JoinRunsGroup() / LeaveRunsGroup()

// DTOs
record ApprovalRequestDto(..., string? Summary);
enum PipelineStatus { ..., Failed }
record PipelineExecutionDto(..., string? FailureReason);

// Config
"Taskboard:Pipelines:AutoRetry": { "Enabled": true, "AttemptsPerAgent": 5, "Interval": "00:01:00" }

// Cockpit event kinds (additive)
"run_status"  // {status, completedAtUtc?}
```

## 6. Acceptance Criteria

- [ ] **Given** a run for a repo with no local clone **when** Start runs
  **then** `git clone` lands at `~/repos/<name>`, `RepositoryPath` points
  to it, and the worktree is created from that clone — the verifier finds
  the real `.sln`.
- [ ] **Given** `~/repos/<name>` exists as a **different** repo **then**
  Start fails `422` and nothing is dispatched.
- [ ] **Given** a failing stage **then** it is retried up to 5 times at
  ~1-minute intervals on the same CLI; on the 5th failure the stage rotates
  to the next untried eligible CLI and retries 5 more times.
- [ ] **Given** every eligible CLI exhausted **then** the run ends in
  `Failed` with `FailureReason` containing the last error and the tried
  agents, and no further dispatch happens.
- [ ] **Given** an approval stage becomes eligible **then** the operator
  gets the modal (run page) **and** a toast + browser notification with
  the approval summary; the runs list shows a `Needs approval` badge
  without refresh.
- [ ] **Given** any status transition **then** `CockpitRun` and the
  `/cockpit` list update live via SignalR — no F5.
- [ ] **Given** a finished run **then** `GET /runs/{id}/events` still
  returns its lifecycle/error/metric/verification history after a server
  restart.
- [ ] **Given** the run page **then** a `Terminal` tab opens an
  interactive shell with cwd = the run worktree.

**Edge cases:**

| Scenario | Expected |
| --- | --- |
| `~/repos` itself is a git repo (today's state) | Irrelevant — runs never use the root as repository path; only `<root>/<name>` |
| Clone of a private repo without credentials | `git clone` fails → 422 with the stderr detail; `lifecycle` event records the failure |
| Server restarts mid `AwaitingRetry` | `NextAutoRetryAtUtc`/`AutoRetryCount` persisted — the tick resumes the schedule |
| Stage fails while `Paused` | Exec skipped by dispatch (existing) — auto-retry resumes on `Resume` |
| Manual retry mid auto-retry | Resets `TriedAgents` + counters; schedule restarts on next failure |
| Budget cap hit during auto-retry | Existing over-cap cancel wins — terminal `Cancelled`, no retry |
| Run predates the migration (`AutoRetryCount=0`, `NextAutoRetryAtUtc=null`) | First failure after deploy schedules retries normally |
| `AwaitingRetry` execs already in the DB (e.g. `pipe_0bfefc2f…`) | They pick up the auto-retry schedule on the next tick after deploy |
| Browser denies Notification permission | Toast + in-app badge still fire; no console error |
| `OpenForRun` on a run without worktree (pre-dispatch/removed) | `HubException`, tab shows the message — no shell opened |

## 7. Task Plan

- [ ] **T1 — Red tests:** provisioning matrix, auto-retry/rotation/
  exhaustion unit tests, endpoint contract tests, hub tests (RF-007).
- [ ] **T2 — Domain + migration:** `PipelineStatus.Failed`,
  `PipelineExecution.Fail`/`FailureReason`, stage `AutoRetryCount`/
  `NextAutoRetryAtUtc`, `RecomputeStatus`, EF migration.
- [ ] **T3 — Provisioning:** `IRepositoryProvisioningService` +
  `RepositoryProvisioningService`, wire into `StartAsync` + both start
  endpoints, provisioning events.
- [ ] **T4 — Engine:** auto-retry sweep in `DispatchCoreAsync`,
  single-agent dispatch + rotation, `run_status`/`lifecycle`/`error`/
  `metric` emission, verification early-return event fix.
- [ ] **T5 — Realtime:** `runs` group + dual publish, `CockpitRun`
  detail-refresh on event kinds, `Cockpit` live rows + approval badge.
- [ ] **T6 — Notifications + terminal:** `ApprovalRequestDto.Summary`,
  `taskboardNotify` JS, `TerminalHub.OpenForRun`, `Terminal` tab.
- [ ] **T7 — Events endpoint:** persisted-stream `GET /runs/{id}/events`
  merge with live buffer; `RunTerminal` replay from persisted source.
- [ ] **T8 — Docs + validation:** `docs/` en + pt-br, build
  (warnings-as-errors), tests, coverage ≥ ratchet, PR.

## 8. Guardrails

- Clone/provisioning runs only under the configured workspace root —
  `WorkspacePaths.IsUnder` enforced; names sanitized via
  `SanitizeRepoName`.
- `OpenForRun` never trusts a client path — the worktree comes from the
  persisted session and is re-confined to the worktree root.
- Auto-retry is bounded (`AttemptsPerAgent` × eligible CLIs) and
  observable — every schedule/retry/rotate/exhaust emits events.
- `Failed` is terminal and additive: existing `Completed`/`Cancelled`
  semantics unchanged; `IsRunLive`/status badges handle the new value.
- Events stay redacted/truncated via `AgentExecutionEventSink`; no secrets
  in payloads.
- `git clone` uses ambient credentials (`gh`/credential helper) — no token
  handling added by Harness.

## 9. Definition of Done

- [ ] All RFs implemented; acceptance criteria green.
- [ ] `dotnet build` clean, `dotnet test` green, coverage ≥ ratchet (77%).
- [ ] Migration additive; pre-existing runs deserialize and resume.
- [ ] Docs en + pt-br; SPEC → `Done` on PR.

## Open Questions

- **First auto-retry delay:** spec uses `Interval` (1 min) after the first
  failure. Alternative: retry once immediately, then space — rejected for
  now to match the stated behavior ("5 checks at 1-minute intervals").
- **In-dispatch fallback removal:** SPEC-20260922 RF-003's same-tick CLI
  sweep is superseded by rotation-on-failure. If operators prefer
  fail-fast rotation within one dispatch, a config flag
  (`AutoRetry:RotateImmediately`) can restore it later.
- **Verification retries:** spec retries verification stages on the same
  budget without rotation (there is no agent to rotate). Alternative:
  exempt verification from auto-retry entirely — kept in because the
  motivating failure (`No .sln` on a wrong worktree) disappears with
  RF-001, and transient build breaks are common.
- **Reclone freshness:** `EnsureCloneAsync` reuses existing clones as-is
  (no fetch). If stale clones become a problem, add `git fetch` +
  fast-forward behind a config flag.
