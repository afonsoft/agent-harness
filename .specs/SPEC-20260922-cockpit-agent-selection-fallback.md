# SPEC-20260922-cockpit-agent-selection-fallback

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cockpit-agent-selection-fallback` |
| Type | `Feature` + `Bugfix` (Frontend + API + Domain) |
| Stack | `Blazor WASM / .NET 10 / ABP / EF Core SQLite` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-cockpit-agent-selection-fallback` |
| Ticket | [#330](https://github.com/afonsoft/agent-harness/issues/330) |
| Status | `Approved` |
| Referência | SPEC-20260919-ade-multi-agent-orchestration, SPEC-20260919-ade-cockpit-hitl, SPEC-20260920-board-cockpit-unified-runs, SPEC-20260918-agent-model-config, SPEC-20260918-agent-model-tiers, SPEC-20260917-agent-eligibility-task-badge |

## 1. User Story

**As a** Harness operator starting a pipeline run
**I want** the Cockpit to only offer/dispatch eligible agent CLIs, let me
pick a per-stage CLI+model (or one CLI for every stage), and automatically
fall back to another eligible CLI when the chosen one fails
**So that** a run never dies because a template hard-codes a CLI that is
not installed/enabled — and a rejected model name retries instead of
failing the stage.

**Problem context — observed failure (reproduced from the run log):**

```text
── architect ──
Permission allow rule (../../.claude/settings.json): Glob(**) is not matched…
"Opus" isn't described by this version's model catalog; …
[claude-code:unrecognized_model] {"model":"Opus","query_source":"sdk"}
```

Root causes verified in code:

1. **No eligibility gate on the pipeline path.** `PipelineEngine.RunAgentStageAsync`
   calls `_acpClient.ExecuteAsync` directly with `stage.Agent ?? Codex` —
   the eligibility check lives only in `AgentOrchestrationService.EnqueueAsync`
   (the queue path used by Board/AI-Chat). Templates hard-code CLIs
   (`standard-feature`: architect→`Claude`, builder→`OpenCode`,
   reviewer→`Devin`; `test-driven`: Claude→Codex→OpenCode), so any template
   can dispatch a disabled/unauthenticated CLI and the stage just dies.
2. **No model validation/fallback.** `AgentModelConfigService.ResolveModelAsync`
   returns a per-tier override or the curated `AgentCliModels` name, passed
   verbatim to the CLI. An override/catalog name the installed CLI rejects
   (e.g. `"Opus"` vs the alias `"opus"` — claude-code's
   `unrecognized_model`) kills the stage on first contact.
3. **No fallback CLI.** `result.IsSuccess == false` → `FailStage` → the
   execution fails. Nothing retries the stage with another CLI.
4. **Overrides are single-agent-only.** `PipelineStartRequest.AgentOverride`/
   `TierOverride`/`SkipVerification` are rejected for every other template
   (`StartAsync` throws `InvalidValue`), so there is no way to run a
   multi-stage template with one CLI — or to re-map an unavailable stage
   CLI before starting.
5. The `Glob(**)`/`Write(**)` lines are **claude-code warnings about the
   user's own `.claude/settings.json`** — harmless noise, not Harness
   caused; no code change (documented in §6 edge cases).

## 2. Scope

**In scope:**

- `PipelineStartRequest` gains per-stage overrides (`StageOverrides`) and a
  `SingleAgent` convenience — valid for **every** template (RF-001).
- Server-side agent resolution at start: override → template default →
  `Auto` = first eligible CLI; start fails fast (422) when a stage cannot
  resolve to any eligible CLI (RF-002).
- Stage fallback chain: on agent failure, retry the stage with the next
  untried eligible CLI (same synthesized prompt/context); persist tried
  CLIs so restarts don't loop on the same failure (RF-003).
- Model hardening: validate resolved model against the probed CLI catalog
  when available; on `unrecognized_model` output signature retry once with
  no model flag before moving to the next CLI (RF-004).
- New Run modal rework: stage grid always visible, `Auto` option, Single
  agent checkbox, "+ options" collapse with per-stage CLI+tier (RF-005).
- Run detail: stage row shows the effective CLI + attempt count; cockpit
  timeline gets `agent fallback` events (RF-006).
- Tests (RF-007).

**Out of scope:**

- Editing/deleting the built-in templates (`PipelineTemplates` statics stay
  the defaults that `Auto` resolves against).
- Parallel stage execution, template authoring UI, per-stage budget caps.
- Fixing the user's `.claude/settings.json` permission rules (external).
- Changing `EnqueueAsync` (Board one-shot runs already gate eligibility).

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Application.Contracts/Harness/PipelineDtos.cs` —
  `PipelineStartRequest` gains `StageOverrides` + `SingleAgentType`/
  `SingleAgentTier`; `PipelineTemplateDto` gains a `Stages` detail list
  (key/name/kind/role/default agent+tier) so the modal can render override
  rows without hard-coding template internals; `PipelineStageDto` gains
  `TriedAgents` (CLI history) — additive, wire-compatible.
- `Taskboard.Domain` — `PipelineStageExecution` gains `TriedAgentsJson`
  (JSON `string[]`, persisted) + `RetryWithAgent(AgentType, error, now)`
  (fail-into-pending semantics reusing `Attempts++`); additive EF migration
  `AddPipelineStageTriedAgents`.
- `Taskboard.Application/Harness/PipelineExecutionAppService.cs` —
  generalize `ApplyOverrides` to all templates + eligibility resolution at
  `StartAsync` (inject `IAgentEligibilityService`).
- `Taskboard.Application/Harness/PipelineEngine.cs` —
  `RunAgentStageAsync` fallback loop + model-flag retry; inject
  `IAgentEligibilityService` + `IAgentModelCatalogService` (via scope).
- `Taskboard.Blazor/Components/Pages/Cockpit.razor` — New Run modal rework.
- `src/Taskboard.Blazor/Components/Pages/CockpitRun.razor` — stage shows
  effective CLI + attempts.
- `Taskboard.Application.Contracts/Agents/AgentCliModels.cs` —
  `ModelFor(type, tier)` reused for per-CLI tier remap on fallback.
- Detection signature: match `unrecognized_model` /
  `isn't described by this version's model catalog` in agent output
  (`AgentEventNormalizer`/raw chunk scan) → `AgentFailureKind.InvalidModel`.

**Files to read before implementing:**

- `src/Taskboard.Application/Harness/PipelineEngine.cs` —
  `RunAgentStageAsync` (~line 291) is where dispatch + fail live.
- `src/Taskboard.Application/Harness/PipelineExecutionAppService.cs` —
  `StartAsync` + `ApplyOverrides` (~lines 61-93, 279-293).
- `src/Taskboard.Application/Harness/PipelineTemplates.cs` — stage
  defaults per template.
- `src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs` —
  `Retry`/`Fail`/`MarkRunning` guards (fallback reuses `Retry` semantics).
- `src/Taskboard.Application.Contracts/Agents/AgentCliModels.cs` —
  `Catalog`/`ModelFor`/`ModelListProbe`.
- `src/Taskboard.Application/Agents/AgentEligibilityService.cs` +
  `AgentModelConfigService.ResolveModelAsync` — eligibility + override ??
  curated resolution.
- `src/Taskboard.Blazor/Components/Pages/Cockpit.razor` — New Run modal
  (~lines 67-148) and `RunStartForm`.
- `tests/Taskboard.Tests.Unit/Harness/*`, `tests/Taskboard.Tests.Integration/CockpitEndpointsTests.cs`.

**Existing pieces reused as-is:**

- `PipelineContextSynthesizer.BuildStagePrompt` — context handoff already
  synthesizes prior-stage summaries; the fallback attempt reuses the same
  prompt (user requirement: "repassando o contexto").
- `IAgentEligibilityService.GetEligibleTypesAsync` — installed +
  authenticated + enabled set (same source as `/agents` badges).
- `AgentCliModels.ModelFor(type, tier)` — per-CLI tier remap so a fallback
  CLI runs at the *same tier* with its own model name.
- `PipelineStageDto.Attempts` — already surfaces retry count.

## 4. Requirements

### RF-001: Per-stage overrides for every template

- **Description:** `PipelineStartRequest` gains
  `IReadOnlyDictionary<string, PipelineStageOverrideDto>? StageOverrides`
  (`PipelineStageOverrideDto(AgentType? Agent, AgentModelTier? Tier)` — a
  `null` Agent means `Auto`) plus `AgentType? SingleAgentType` /
  `AgentModelTier? SingleAgentTier` — apply to **all** templates, not only
  `single-agent`.
- **Rules:**
  - `SingleAgentType` expands to a `StageOverrides` entry for every
    `AgentWork` stage before resolution (explicit per-stage entry wins over
    the single-agent value if both are sent).
  - Legacy `AgentOverride`/`TierOverride`/`SkipVerification` keep working
    on `single-agent` (they fold into the same path); they remain rejected
    on other templates only when combined conflictingly — sending
    `StageOverrides` alongside them on `single-agent` is allowed,
    stage-level wins.
  - Overrides may only target existing `AgentWork` stage keys — unknown key
    or non-AgentWork target → `400 InvalidValue` listing the offender.
  - `PipelineTemplateDto` gains `Stages` (`{ key, name, kind, role,
    defaultAgent, defaultTier }`) — drives the modal.
- **Input → Output:** request with overrides → definition rewritten before
  `PipelineExecution.Create`.

### RF-002: Eligibility resolution at start

- **Description:** no run may start with a stage bound to an ineligible
  CLI — and `Auto` must resolve deterministically.
- **Rules:**
  - Effective agent per `AgentWork` stage = `StageOverrides[key].Agent`
    (non-null) → else stage default → if that is ineligible, `Auto`
    fallback to the first eligible `AgentType` (enum order — Devin, Claude,
    Codex, …).
  - An **explicit** pick (override or single-agent) that is not eligible →
    `422 AgentNotEligible` naming stage + CLI.
  - `Auto`/default resolution that finds **no** eligible CLI →
    `422 AgentNotEligible` ("no eligible agent CLI — authenticate one in
    Settings → Agents").
  - Resolved values are stored on `PipelineStageExecution.Agent` so the DTO
    always shows the effective CLI.
  - Eligibility is re-checked at each dispatch (it is dynamic — a CLI
    disabled mid-run triggers the RF-003 fallback, not a silent dispatch).
- **Input → Output:** start request → resolved per-stage agents, or 422
  with the offending stage/CLI.

### RF-003: Agent fallback on stage failure

- **Description:** when an agent stage fails, the engine retries it with
  the next untried eligible CLI instead of failing the run.
- **Rules:**
  - Candidate order per stage: `[resolved agent]` + eligible types not yet
    tried (enum order), each tried **at most once** — tracked in
    `PipelineStageExecution.TriedAgentsJson`.
  - Failure → `stage.RetryWithAgent(next, error, now)` → `Status = Pending`,
    `Attempts++`, `LastError` recorded, `Agent = next`; engine re-dispatches
    in the same tick. Same `BuildStagePrompt` output — handoff/context is
    unchanged across CLIs.
  - Exhaustion (all candidates tried) → existing `FailStage` path
    (run fails; human `retry` endpoint still works — a manual retry resets
    `TriedAgentsJson` so the full chain is available again).
  - Cancellation (`OperationCanceledException`), budget-cap cancel and
    `Paused` never trigger fallback.
  - Every fallback publishes a cockpit event
    `stage: '{name}' — {old} failed ({reason}); retrying with {new}`
    so the timeline shows the chain; `PipelineStageDto.TriedAgents`
    exposes it to the UI.
- **Input → Output:** agent stage exit≠0 → next CLI dispatched; all fail →
  stage Failed as today.

### RF-004: Model validation + `unrecognized_model` retry

- **Description:** never pass a model name the CLI has already told us it
  does not know.
- **Rules:**
  - At dispatch: resolved model (override ?? curated `ModelFor`) is checked
    against `IAgentModelCatalogService.ListAvailableAsync(agentType)` **when
    the CLI has a probe**; a name absent from the probed list → warn-log +
    cockpit `activity` event, and dispatch with `OmitModelFlag` (CLI
    default) instead of the bad name. No probe → keep current behavior.
  - Runtime: agent output matching the `unrecognized_model` signature
    (normalized `error` event or stderr chunk containing
    `unrecognized_model` / `not.*described.*model catalog`) → mark the
    attempt `InvalidModel` → **same-CLI retry once** with `OmitModelFlag`
    before consuming a fallback CLI (counts as one tried agent, not two).
  - On CLI fallback the tier re-resolves via `ModelFor(nextCli, tier)` —
    the fallback CLI never receives the previous CLI's model name.
- **Input → Output:** `Opus`-style rejection → one no-flag retry → then
  next eligible CLI if still failing.

### RF-005: New Run modal — stage grid + Single agent

- **Description:** the modal always exposes per-stage agent control.
- **Rules:**
  - Template select unchanged; below it a read-only stage list preview
    (`key · name · role`) built from the new `PipelineTemplateDto.Stages`.
  - **`Single agent` checkbox** → one `Agent CLI` select (`Auto` + every
    `Available` CLI — names only, no status suffix) + one tier select
    (`Auto`/`Lite`/`Normal`/`Ultra`); applies to all AgentWork stages.
  - **`+ options` collapse** → one row per template stage:
    - AgentWork rows: `Agent CLI` select (`Auto` first, then available CLI
      names) + tier select (`Auto` keeps the stage/tier default).
    - Approval/Verification rows: rendered read-only (no selects).
  - When `Single agent` is checked the per-stage selects are shown
    disabled (mirroring the single pick) so the collapse still explains the
    flow.
  - The existing `single-agent` template keeps its top-level Agent/Tier/
    Verification fields as today (they map onto the same request fields).
  - Agents load via `GetAvailableAgentsAsync` filtered to
    `Status == Available` — unavailable CLIs are never listed (user
    requirement: "só pode executar com os CLIs disponíveis"); zero
    available → Start disabled + inline warning pointing to Settings.
- **Input → Output:** modal state → `PipelineStartRequest` with
  `StageOverrides`/`SingleAgent*`.

### RF-006: Effective-CLI visibility

- **Description:** the operator can always see which CLI actually ran.
- **Rules:**
  - `PipelineStageDto` gains `TriedAgents: IReadOnlyList<string>` — the
    `Agent` field stays "effective/last" CLI.
  - Cockpit run header/table: stage line shows `Agent` + `×Attempts` when
    `Attempts > 1`.
  - Cockpit timeline shows the `agent fallback` events (RF-003) between the
    failed and retried stage entries.
- **Input → Output:** DTO + timeline reflect the real execution path.

### RF-007: Tests

- **Rules:**
  - Unit (BDD pt-BR): override expansion (single-agent → per-stage),
    unknown-stage-key rejection, `Auto` resolution order, explicit-ineligible
    → 422, fallback ordering + `TriedAgentsJson` persistence, exhaustion →
    `Failed`, `unrecognized_model` → same-CLI no-flag retry then next CLI,
    tier remap on fallback.
  - Integration: `POST /api/harness/pipelines/start` with `StageOverrides`
    on a multi-stage template (was 400 before); `GET templates` returns the
    new `Stages` detail; `422` when no CLI is eligible.
  - Engine-level test with a fake `IAgentAcpClient` (fail first CLI, succeed
    second) asserting stage completes and `Agent`/`TriedAgents` updated.

## 5. API Contract

```http
POST /api/harness/pipelines/start
{
  "templateId": "standard-feature",
  "repositoryFullName": "owner/repo",
  "baseBranch": "main",
  "initialPrompt": "…",
  "singleAgentType": "Codex",            // optional — all AgentWork stages
  "singleAgentTier": "Normal",           // optional
  "stageOverrides": {                    // optional — per stage
    "architect": { "agent": null, "tier": "Ultra" },   // Auto agent, Ultra
    "builder":   { "agent": "OpenCode" }               // explicit CLI
  }
}
→ 201 PipelineExecutionDto | 400 invalid stage key | 422 no eligible CLI

GET /api/harness/pipelines/templates
→ 200 { templates: [{ templateId, name, stageKeys,
       stages: [{ key, name, kind, role, defaultAgent, defaultTier }] }] }
```

`PipelineStageDto` gains `triedAgents: string[]`. Legacy
`AgentOverride`/`TierOverride`/`SkipVerification` unchanged.

## 6. Acceptance Criteria

- [x] **Given** `standard-feature` while Claude is disabled **when** Start
  runs **then** architect resolves to the next eligible CLI (`Auto`) or the
  request fails 422 — never dispatches a disabled CLI.
- [x] **Given** a stage that exits non-zero **when** another eligible CLI
  exists **then** the stage retries on it with the same prompt; the
  timeline shows the fallback event and `Agent`/`Attempts` update.
- [x] **Given** all eligible CLIs tried **then** the stage fails as today
  and `triedAgents` lists every attempt.
- [x] **Given** an `unrecognized_model` rejection **then** the stage first
  retries the same CLI without a model flag; only then falls back.
- [x] **Given** `Single agent` checked **when** the run starts **then**
  every AgentWork stage runs the chosen CLI at the chosen tier and context
  handoff still flows (`BuildStagePrompt` unchanged).
- [x] **Given** the `+ options` collapse **then** every stage row renders —
  AgentWork rows with `Auto` + available-CLI + tier selects.
- [x] **Given** zero eligible CLIs **then** Start is disabled/blocked with
  a pointer to Settings (UI) and 422 (API).

**Edge cases:**

| Scenario | Expected |
| --- | --- |
| User's `.claude/settings.json` warnings (`Glob(**)`/`Write(**)` lines) | Noise forwarded to the log stream — not a stage failure by itself; no Harness change |
| Stage fails on CLI A, server restarts, stage re-dispatches | `TriedAgentsJson` persists — A is not retried |
| Manual `POST …/stages/{key}/retry` after exhaustion | Resets `TriedAgentsJson`; full chain available again |
| Override on Approval/Verification stage key | `400 InvalidValue` |
| Fallback CLI has no headless model flag (Cline/Continue/Kiro) | Dispatched with `OmitModelFlag` — no name invented |
| Budget cap crossed during a fallback attempt | Existing over-cap cancel wins; no further dispatch |
| `Auto` + all CLIs eligible | Template default wins (predictable), not alphabetical-first |

## 7. Task Plan

- [x] **T1 — Red tests:** resolution/override/fallback/model-retry unit
  tests + endpoint contract tests (per RF-007).
- [x] **T2 — Contracts + Domain:** `StageOverrides`/`SingleAgent*`/
  `PipelineTemplateDto.Stages`/`PipelineStageDto.TriedAgents`;
  `TriedAgentsJson` + `RetryWithAgent` + EF migration.
- [x] **T3 — Start path:** `StartAsync` generalized override rewrite +
  eligibility resolution (`Auto`, 422s).
- [x] **T4 — Engine:** dispatch-time re-check, fallback loop, model-flag
  retry + catalog validation, cockpit fallback events.
- [x] **T5 — UI:** New Run modal stage grid + Single agent + `+ options`
  collapse; Cockpit run effective-CLI display.
- [x] **T6 — Docs:** `docs/api*.md`, `docs/features*.md` (en + pt-br).
- [x] **T7 — Validation:** build (warnings-as-errors), tests, coverage ≥
  77%, SPEC status, PR.

## 8. Guardrails

- No new top-level routes; `POST /pipelines/start` body extension is
  additive and backward compatible.
- `PipelineContextSynthesizer` unchanged — context handoff semantics are
  preserved verbatim across CLIs.
- Eligibility semantics are the existing
  `IAgentEligibilityService` (installed ∧ authenticated ∧ enabled) — no new
  definition of "available".
- A fallback attempt is still one billable run — FinOps metric is recorded
  per attempt under the CLI actually used.

## 9. Definition of Done

- [x] All RFs implemented; acceptance criteria green.
- [x] `dotnet build` clean, `dotnet test` green, coverage ≥ ratchet.
- [x] Migration additive; existing runs deserialize (null `TriedAgentsJson`).
- [x] Docs en + pt-br; SPEC → `Done` on PR.

## Open Questions

- **Fallback order:** enum order proposed (deterministic). Alternative: a
  `Taskboard:Pipelines:FallbackOrder` config — defer unless asked.
- **Should fallback also apply to `single-agent` template?** Proposed: yes
  (uniform behavior); disabling per-run could be a checkbox later.
- **Auto tier in per-stage selects:** `Auto` = template stage tier
  (recommended) vs a global default — spec assumes template tier.
