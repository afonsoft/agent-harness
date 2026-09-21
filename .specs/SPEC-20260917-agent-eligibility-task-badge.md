# SPEC-20260917-agent-eligibility-task-badge

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-eligibility-task-badge` |
| Type | `Feature` |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / EF Core SQLite / Blazor WASM / CSS` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260917-agent-eligibility-task-badge` |
| Ticket | N/A |
| Status | `Done` |

## 1. User Story

**As a** Taskboard administrator,
**I want** the Settings → Agents section to list only agent CLIs that are installed *and* authenticated, with an enable/disable toggle per CLI; task agent selection to offer only enabled+authenticated CLIs; and each task popup/card to show which agent CLI is assigned to it,
**So that** orchestration never dispatches work to a CLI that cannot actually run, and I can see at a glance (including on mobile) which agent owns each task.

**Problem context:**

- `Settings.razor` → Agents lists every `AgentPreferenceDto` returned by `SettingsService` — it does not check whether the CLI is installed or authenticated, so the user can "enable" a CLI that cannot run.
- `AgentOrchestrationService.GetAvailableAgentsAsync` returns every binary found on PATH (`IAgentDiscoveryService`) + busy marking — it ignores `AgentPreference.Enabled` and the auth probe (`IAgentCliStatusService`). `AgentSelectionModal` therefore offers CLIs that will fail at login.
- `AgentType` (orchestration enum) contains `OpenHands`; `AgentCliKind` (detection enum, `AgentCliMap`) contains `Antigravity`. The two models are not mapped — an authenticated `agy` cannot be enabled/selected, and `OpenHands` is selectable but undetectable.
- Issue→agent assignment exists only in-memory (`_running` in `AgentOrchestrationService`) and as free text inside `AgentLog.Content` — the task popup and kanban card cannot show "which agent is on this task".
- Mobile: kanban uses HTML5 drag-and-drop (does not fire on touch), columns are fixed 18rem without scroll-snap, settings rows/toggles/chips are below the 44px touch-target guideline, and modals are not fullscreen on narrow screens — the "move task → pick agent" flow is unreachable on phones.

## 2. Scope

**In scope:**

- `AgentType` ↔ `AgentCliKind` unification: add `Antigravity` to `AgentType`; `AgentCliMap` becomes the single map between the two enums. `OpenHands` is kept for historical data but has no CLI mapping (always ineligible).
- Settings → Agents section lists only CLIs where `Installed == true && AuthStatus == Authenticated` (probe via `IAgentCliStatusService`), each row with an Enabled toggle persisted to `AgentPreference`. Non-authenticated CLIs are hidden, with a hint linking to `/agents`/`/terminal`.
- `GetAvailableAgentsAsync` filtered to discovered ∩ enabled ∩ authenticated; `EnqueueAsync` re-validates server-side and rejects ineligible agent types.
- New persisted entity `AgentRun` (issueId, agentType, state, timestamps) written by the orchestration service; `GET /api/agents/runs` endpoints for popup + board hydration.
- `TaskDetailDialog`: agent section showing the latest run's agent name + state + timestamps.
- `.kanban-card` badge `agent-badge agent-{kind}`: icon + short name; animated while `Running`, muted when finished.
- Mobile package: ≥44px touch targets on toggles/chips/card menu, kanban `scroll-snap` below 768px, `modal-fullscreen-sm-down`, `.setting-row` stacking below 576px, `/terminal` mobile font/100dvh, `prefers-reduced-motion` support, and a "Move to…" menu on the card so the move→agent flow works on touch.
- Unit + integration tests; docs updates.

**Out of scope:**

- Removing or migrating existing `OpenHands` `AgentPreference`/`AgentLog` rows (they remain in the DB, just never eligible).
- Assignment history UI beyond the latest run per issue (full history is a later spec).
- Per-project/per-user agent preferences (toggles remain global).
- Touch-based drag-and-drop (the "Move to…" menu is the mobile path; HTML5 drag stays desktop-only).
- Changing the ACP/CLI execution itself, credential handling, or the terminal.
- Brand-accurate vendor logos (badge uses Bootstrap icons + short-name text).

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Domain.Shared` — `AgentType.cs` (+`Antigravity`); `AgentCliMap.cs` gains `AgentTypeFor(AgentCliKind)` / `CliKindFor(AgentType)` (`null` for `OpenHands`).
- `Taskboard.Domain` — new `Agents/AgentRun.cs` entity + `AgentRunState` enum (`Queued, Running, Succeeded, Failed, Canceled`).
- `Taskboard.EntityFrameworkCore` — `AgentRunConfiguration`, `DbSet<AgentRun>`, migration `AddAgentRuns` (index on `IssueId`).
- `Taskboard.Application.Contracts` — `AgentRunDto` record; `AgentPreferenceDto` may gain `AuthStatus`/`Installed` or the join moves server-side into `SettingsService`.
- `Taskboard.Application` — `SettingsService` joins `AgentCliStatusService` results with `AgentPreference` rows so `SettingsDto.Agents` only contains authenticated+installed CLIs (creating missing preference rows with `Enabled=true` default, preserving existing rows incl. `OpenHands`).
- `Taskboard.Integrations` — `AgentOrchestrationService`: eligibility filter in `GetAvailableAgentsAsync` (needs `IAgentCliStatusService` + `IAgentPreference`-reading scope), server-side guard in `EnqueueAsync`, and `AgentRun` writes on enqueue/start/finish/cancel.
- `Taskboard.Server` — `Program.cs`: `GET /api/agents/runs?issueId=` (latest run per issue or list) and `GET /api/agents/runs/active` (running/queued map for board badges); `EnabledAgentResolver` reuse.
- `Taskboard.Client` — `TaskboardClient` methods for the new endpoints.
- `Taskboard.Blazor` — `Settings.razor` Agents section (filtered list + empty state + auth hint), `AgentSelectionModal.razor` (works unchanged once the endpoint filters), `TaskDetailDialog.razor` (agent info section), `KanbanBoard.razor` (badge on `.kanban-card` + "Move to…" menu), `site.css` (badge, scroll-snap, touch targets, modal-fullscreen, reduced-motion).

**Files to read before implementing:**

- `src/Taskboard.Blazor/Components/Pages/Settings.razor` (Agents section ~L210, `_enabledState`)
- `src/Taskboard.Application/Settings/SettingsService.cs` + `SettingsDto.cs` / `SaveSettingsRequest.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` (`_running`, `EnqueueAsync`, `GetAvailableAgentsAsync`)
- `src/Taskboard.Integrations/Agents/AgentDiscoveryService.cs` + `AgentCliStatusService.cs`
- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` + `AgentType.cs`
- `src/Taskboard.Blazor/Components/GitHub/{KanbanBoard,AgentSelectionModal,TaskDetailDialog,TaskLogTab}.razor`
- `src/Taskboard.EntityFrameworkCore/Agents/AgentLogConfiguration.cs` (entity config pattern)
- `src/Taskboard.Server/Program.cs` (`EnabledAgentResolver`, `agents` endpoint group ~L1262)
- `src/Taskboard.Client/wwwroot/css/site.css` (`.kanban-*`, `.setting-row`, `@media` blocks)

**Files to create or modify:**

```text
src/Taskboard.Domain.Shared/Agents/AgentType.cs                    # MOD — +Antigravity
src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs                  # MOD — AgentType↔CliKind map
src/Taskboard.Domain/Agents/AgentRun.cs                            # NEW
src/Taskboard.EntityFrameworkCore/Agents/AgentRunConfiguration.cs  # NEW
src/Taskboard.EntityFrameworkCore/TaskboardDbContext.cs            # MOD — DbSet
src/Taskboard.EntityFrameworkCore/Migrations/*_AddAgentRuns.cs     # NEW
src/Taskboard.Application.Contracts/Agents/AgentRunDto.cs          # NEW
src/Taskboard.Application/Settings/SettingsService.cs              # MOD — auth join
src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs     # MOD — filter + runs
src/Taskboard.Server/Program.cs                                    # MOD — runs endpoints
src/Taskboard.Client/TaskboardClient.cs                            # MOD
src/Taskboard.Blazor/Components/Pages/Settings.razor               # MOD — filtered agents UI
src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor      # MOD — agent section
src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor           # MOD — badge + Move-to menu
src/Taskboard.Client/wwwroot/css/site.css                          # MOD — badge + mobile package
tests/Taskboard.Tests.Unit/...                                     # NEW
tests/Taskboard.Tests.Integration/...                              # NEW
```

## 4. Requirements

### RF-001: AgentType ↔ AgentCliKind unification

- **Description:** `AgentType` gains `Antigravity` (appended — existing numeric values unchanged so persisted data is safe). `AgentCliMap` exposes `AgentTypeFor(AgentCliKind)` and `CliKindFor(AgentType)`; `CliKindFor(OpenHands)` returns `null` (no detectable CLI). All eligibility logic goes through this single map.
- **Input → Output:** enum value ↔ mapped counterpart or `null`.

### RF-002: Settings → Agents shows only authenticated CLIs

- **Description:** `SettingsService.GetAsync` joins `IAgentCliStatusService.GetStatusAsync()` with `AgentPreference` rows: `SettingsDto.Agents` contains only CLIs where `Installed && AuthStatus == Authenticated`. Missing `AgentPreference` rows for newly-detected authenticated CLIs are created with `Enabled=true`. Rows whose CLI is no longer authenticated are preserved in the DB but omitted from the DTO (toggle state survives a re-login).
- **UI:** each row shows display name, version, auth badge, and the Enabled toggle (≥44px target). Empty state: "No agent CLI authenticated — open /agents to log in" with a link. Saving keeps writing `AgentPreference.Enabled` via the existing `SaveSettingsRequest`.

### RF-003: Orchestration eligibility filter

- **Description:** `GetAvailableAgentsAsync` returns only agents that are (a) discovered on PATH, (b) mapped to an authenticated CLI, (c) `AgentPreference.Enabled == true` — then applies the existing busy marking. `EnqueueAsync` re-validates eligibility and returns/logs a rejection for an ineligible `AgentType` (never silently queues).
- **Input → Output:** `AgentExecutionRequest.AgentType` → queued or rejected with reason logged to `AgentLog`/`AgentRun`.

### RF-004: AgentRun persistence

- **Description:** New entity `AgentRun(Guid Id, string IssueId, AgentType AgentType, AgentRunState State, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt)`. The orchestration service creates a `Queued` row on `EnqueueAsync`, transitions to `Running` on start, and `Succeeded`/`Failed`/`Canceled` on completion. EF config + migration; index on `IssueId`.

### RF-005: Runs endpoints

- **Description:** `GET /api/agents/runs?issueId={id}` → latest N runs for an issue (popup). `GET /api/agents/runs/active` → `{ issueId, agentType, state }[]` for all `Queued`/`Running` runs plus each issue's most recent finished run — single batched call for board badge hydration. Both `RequireAuthorization`.

### RF-006: Task popup agent info

- **Description:** `TaskDetailDialog` gains an "Agent" section (or extends the existing "Logs do Agente" tab header) showing latest run: agent display name, state badge, started/finished timestamps. "No agent run" state renders a muted label. Data fetched via `TaskboardClient` on dialog open.

### RF-007: Agent badge on kanban card

- **Description:** `.kanban-card` renders `agent-badge agent-{kind}` (Bootstrap icon + short name) when a run exists for the issue. `Running`/`Queued` → accent border + pulse animation (disabled under `prefers-reduced-motion`); `Succeeded`/`Failed`/`Canceled` → muted badge. Badge never wraps the card layout; long names truncate. Board hydrates badges from `GET /api/agents/runs/active` on load and refreshes via the existing SignalR log hub or a poll after enqueue.

### RF-008: Mobile "Move to…" path

- **Description:** Each `.kanban-card` gains a `⋮` button (≥44px) opening a column list; choosing a column performs the same server move as a drop — including opening `AgentSelectionModal` when the target column triggers an agent action. HTML5 drag stays for desktop. Menu is `role="menu"` with keyboard support.

### RF-009: Mobile CSS package

- **Description:** (a) toggles, filter chips, and card `⋮` ≥44px hit area; (b) `.kanban-board` gets `scroll-snap-type: x mandatory` + `scroll-snap-align: start` on columns below 768px; (c) modals use `modal-fullscreen-sm-down` below 576px; (d) `.setting-row` stacks label above control below 576px; (e) `/terminal` uses smaller xterm font + `100dvh` height on mobile; (f) all new animations gated by `prefers-reduced-motion`.

**Business rules / invariants:**

- An agent that is not installed+authenticated+enabled can never be enqueued — enforced server-side, not only in the UI.
- Preference rows are never deleted by eligibility changes; `Enabled` survives auth loss.
- `AgentRun` records are append-only transitions of a single row per execution — they must not contain secrets or log content.
- Credential probes remain existence-only; nothing in this feature reads credential contents.

## 5. API Contract

**New endpoints** (all `RequireAuthorization`, same cookie/`X-Api-Key` as `/api/agents`):

```http
GET /api/agents/runs?issueId={issueId}&take=5
→ 200 { "runs": [ { "issueId", "agentType", "state", "startedAt", "finishedAt" } ] }

GET /api/agents/runs/active
→ 200 { "runs": [ { "issueId", "agentType", "state" } ] }
```

**Modified behavior:**

- `GET /api/agents` — response filtered to eligible agents only (discovered ∩ authenticated ∩ enabled); unchanged shape.
- `POST /api/agents/executions` — `202` when eligible; `422 { "error": "agent-not-eligible", "agentType" }` when the agent is disabled/unauthenticated/not installed.
- `GET /api/settings` — `agents[]` only contains authenticated+installed CLIs (each entry may carry `version`/`authStatus` for the badge).

**Expected errors:** `401` unauthenticated; `422` ineligible agent on enqueue; runs endpoints return empty arrays when nothing exists.

## 6. Acceptance Criteria

- [ ] **Given** a CLI installed but not authenticated **when** `/settings` loads **then** it does not appear in the Agents section and the empty/hint state links to `/agents`.
- [ ] **Given** an authenticated+enabled CLI **when** its toggle is switched off and saved **then** `GET /api/agents` no longer lists it and `POST /api/agents/executions` for it returns `422`.
- [ ] **Given** an agent that loses authentication **when** it re-authenticates **then** its previous `Enabled` value is restored.
- [ ] **Given** an `OpenHands` preference row exists **when** `/settings` loads **then** it is not listed (no CLI mapping) and no error occurs.
- [ ] **Given** a drop to an agent column **when** `AgentSelectionModal` opens **then** the dropdown only contains eligible agents; a `Busy` agent shows as disabled.
- [ ] **Given** an enqueue **when** it runs **then** an `AgentRun` row transitions `Queued→Running→Succeeded/Failed/Canceled` and `GET /api/agents/runs?issueId=` returns it.
- [ ] **Given** a running agent **when** the board loads **then** the issue card shows the agent badge in the animated state; after finish it renders muted.
- [ ] **Given** the task popup **when** a run exists **then** the agent name + state + timestamps are visible; with no run a muted "No agent run" label shows.
- [ ] **Given** a touch device (or narrow viewport) **when** the card `⋮` menu opens **then** choosing a column moves the issue and opens the agent modal when applicable.
- [ ] **Given** a 375px viewport **when** Settings/Kanban/terminal render **then** toggles/chips/buttons are ≥44px, kanban columns snap, modals are fullscreen, and `prefers-reduced-motion` disables badge animation.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| All agents disabled | toggle off all | `/api/agents` returns empty; modal shows empty state + link to Settings |
| Auth lost mid-run | credential removed while running | run completes/cancels normally; eligibility only gates *new* enqueues |
| Duplicate enqueue same issue | POST twice | existing busy/queue semantics preserved; second run recorded separately |
| Very long agent name on badge | 375px card | truncates with ellipsis, icon stays visible |
| Reduced motion | OS setting on | no pulse animation, static badge |
| `AgentRun` row missing | popup on untouched issue | "No agent run" muted label, no error |

## 7. Task Plan (agent execution)

- [ ] **T1 — Domain.Shared:** `AgentType.Antigravity`, `AgentCliMap` bidirectional map; unit tests incl. `OpenHands→null`.
- [ ] **T2 — Domain + EF:** `AgentRun` entity, `AgentRunState`, configuration, `DbSet`, migration `AddAgentRuns`.
- [ ] **T3 — Contracts:** `AgentRunDto`; extend `AgentPreferenceDto`/`SettingsDto` join contract.
- [ ] **T4 — Application:** `SettingsService` join (installed+authenticated filter, row creation, `Enabled` preservation) — TDD.
- [ ] **T5 — Integrations:** orchestration eligibility filter + enqueue guard + `AgentRun` writes — TDD.
- [ ] **T6 — Server:** runs endpoints + `422` on ineligible enqueue; integration tests.
- [ ] **T7 — Client/UI:** `TaskboardClient` methods; Settings section, popup agent section, card badge, "Move to…" menu.
- [ ] **T8 — CSS/mobile:** badge styles, scroll-snap, ≥44px targets, `modal-fullscreen-sm-down`, terminal mobile, reduced-motion.
- [ ] **T9 — Validation + Done:** `dotnet build -c Release`, `dotnet test`, manual smoke (desktop + 375px), `Status=Done`, PR.

**7.1 Validation strategy:** unit tests for the map/eligibility/run-state transitions; integration tests for endpoints + auth; manual responsive check at 375px; `TreatWarningsAsErrors`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`develop`; branch `feature/devin-20260917-agent-eligibility-task-badge`.
- **Workflows:** do not modify `.github/workflows/`.
- **Security:** new endpoints behind existing authorization; `AgentRun` stores metadata only — never instructions content, credentials, or log payloads; credential probes remain existence-only.
- **Architecture:** eligibility logic in Application/Integrations (not endpoints); UI via `TaskboardClient`; DB changes via EF migration only.
- **Data:** enum values appended (no reorder); no deletion of existing `AgentPreference`/`AgentLog` rows.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) covered by passing tests or documented manual checks.
- [ ] Edge cases handled.
- [ ] `dotnet build` clean (TreatWarningsAsErrors), `dotnet test` green.
- [ ] Guardrails respected; no secrets in logs.
- [ ] `docs/` updated (api.md + pt-br mirror; features notes on badges/mobile).
- [ ] Host redeployed and smoke-tested at desktop + 375px widths.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Whether the local-task board (`TaskCard.razor`/`BoardView`) also gets the badge — currently agents only run on GitHub issues, so this spec scopes the badge to `.kanban-card`. Revisit if local tasks gain agent execution.
- `[A DEFINIR]` Badge refresh strategy: SignalR `AgentLogHub` subscription vs. simple re-fetch after enqueue — pick at T7 based on what `KanbanBoard` already subscribes to.
