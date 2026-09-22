# SPEC-20260922-finops-dashboard-detail

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `finops-dashboard-detail` |
| Type | `Feature` (Frontend + API) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / EF Core SQLite / Blazor WASM / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-finops-dashboard-detail` |
| Ticket | [#316 — E17](https://github.com/afonsoft/agent-harness/issues/316) |
| Status | `Done` — delivered in PR #322 |
| Referência | Especificação externa "Session Monitor" (ingestão multi-fonte de agentes: devin/cognition SQLite, claude-code JSONL, copilot workspaceStorage, devin-desktop ACP, Devin API, DEVA-AI) — adaptada ao modelo já persistido pelo módulo `cli-metrics` |

## 1. User Story

**As a** Harness operator / engineering manager
**I want** a `/finops` page that shows the same level of detail the Session
Monitor exposes — per-source usage, real vs estimated tokens, activity
sparklines, recent sessions, source health and alerts — over the data already
ingested by `cli-metrics` and `RunCostMetric`
**So that** I can audit where token spend actually comes from (Harness runs vs
external CLI usage), spot unhealthy ingestion sources, and detect cost
anomalies without leaving the Harness UI.

**Problem context:**

1. `FinOpsSummaryDto.CliUsage` is computed by `FinOpsService` but **never
   rendered** — external CLI spend is invisible on the page it was built for.
2. The page shows only `RunCostMetric` rollups: no sessions table, no
   per-source breakdown, no token windows (24h/7d/30d), no error or alert
   surface, no source-health checklist.
3. Estimated vs real token provenance is not distinguished — SQLite sources
   often estimate tokens as `chars/4`, while Claude/ACP sources report real
   `usage` counters; the UI gives no hint which numbers are estimates.
4. Ingestion health (`CliMetricSource.Status`: `Missing`, `SchemaDrifted`,
   `Error`, `LastError`) already exists via
   `GET /api/local/cli-metrics/sources` but is only surfaced on `/agents`.

## 2. Scope

**In scope:**

- Extend `GET /api/harness/finops/summary` (and/or a new
  `GET /api/harness/finops/sessions` + reuse of
  `GET /api/local/cli-metrics/sources`) to carry the detail blocks listed in
  RF-002…RF-008.
- Rebuild `/finops` layout into sections:
  - **Header**: period selector (`24h`, `7 days`, `30 days`, `All`) + refresh.
  - **KPI cards**: total cost, total tokens, runs, external CLI sessions —
    plus token windows strip (24h / 7d / 30d totals).
  - **External CLI usage card**: renders `CliUsage` (sessions, in/out/cached
    tokens, cost, `CostByCli`) — today dead data in the DTO.
  - **Usage distribution**: `%` share by `Source · Model` (source-friendly
    name + model), covering both `RunCostMetric` rows and CLI aggregates.
  - **Activity sparkline**: sessions/messages/tokens per bucket; bucket =
    `hour` when period ≤ 2 days, `day` otherwise (auto rule from the
    reference spec); rendered as CSS bars (same approach as existing
    progress-bar tables — no chart library).
  - **Recent sessions table** (top 10): source, project/title, model,
    status (`running` when `lastActivity` within 30 min active window —
    `ACTIVE_WINDOW_SECS = 1800` from the reference spec — else `finished`),
    messages, tokens, cost when known.
  - **Source health checklist**: every registered `CliMetricSource` with
    status badge (`Available`, `Missing`, `SchemaDrifted`, `Error`,
    `CopiedToTemp`) + `LastSyncUtc` + `RowCount`.
  - **Alerts strip**: no active session in window; ≥1 source in
    `Error`/`SchemaDrifted`; runs that hit `BudgetExceeded` in the period;
    daily cost above 2× the period's daily average.
- Estimated-vs-real token flagging: sessions/aggregates whose tokens come
  from `chars/4` estimation are marked (badge `~` / tooltip "estimated") —
  provenance flag added to the CLI session DTO at ingestion time where the
  vendor schema lacks real `usage` columns.
- Cost projection for CLI usage keeps using `ModelPriceRate`; when no rate
  matches the model, fall back to the reference flat rate
  **USD 9.5 / 1M tokens** and mark the value as estimated in the UI.

**Out of scope:**

- New ingestion sources — `claude-code` JSONL, `copilot` workspaceStorage,
  `devin-desktop` ACP NDJSON, Devin API, DEVA-AI budget panel. These are
  file-based/remote readers owned by a future `cli-file-reader` spec.
- ACP↔SQLite dedup (5-min window key) — only needed once ACP sources exist.
- Real-time updates / SSE on `/finops` (manual refresh only).
- Billing integration, per-user cost attribution, exporting reports.
- Changing `RunCostMetric`/`CliSessionMetric` schemas beyond the additive
  `TokensEstimated` flag (see RF-006) and DTO fields.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Application.Contracts` — extend `FinOpsDtos.cs` (token windows,
  distribution, sparkline bins, recent sessions, alerts) and
  `CliMetricsDtos.cs` (`Estimated` flag on `CliSessionMetricDto`).
- `Taskboard.Application` — `FinOpsService.GetSummaryAsync` computes the new
  blocks; `CliMetricsService` surfaces estimated-token provenance.
- `Taskboard.Domain` — `CliSessionMetric.TokensEstimated` (bool, default
  `true` for sources without real usage columns); `CliDailyUsageAggregate`
  carries the same flag when its inputs are all estimates.
- `Taskboard.Server` — `Program.cs` finops group gains `sessions`/`sources`
  reads (or the page composes existing `cli-metrics` endpoints — pick one,
  see RF-003).
- `Taskboard.Blazor` — `Components/Pages/FinOps.razor` rebuild +
  `TaskboardClient` methods.
- EF Core migration `AddCliTokensEstimatedFlag` (additive, default `false`).

**Files to read before implementing:**

- `src/Taskboard.Application/Harness/FinOpsService.cs` — current summary
  aggregation + `CliUsage` projection.
- `src/Taskboard.Blazor/Components/Pages/FinOps.razor` — current page.
- `src/Taskboard.Application.Contracts/Harness/Dtos/FinOpsDtos.cs` and
  `src/Taskboard.Application.Contracts/CliMetrics/CliMetricsDtos.cs`.
- `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs`,
  `CliMetricSource.cs`, `CliDailyUsageAggregate.cs`.
- `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` — ingestion
  path where `TokensEstimated` is set per extractor capability.
- `src/Taskboard.Server/Program.cs` (~lines 977-1049) — finops +
  cli-metrics endpoint groups, auth conventions.
- `.specs/SPEC-20260919-ade-observability-finops.md`,
  `.specs/SPEC-20260919-cli-metrics.md`,
  `.specs/SPEC-20260919-cli-db-reader.md`.

**Files to create or modify:**

```text
src/Taskboard.Application.Contracts/Harness/Dtos/FinOpsDtos.cs       [mod]
src/Taskboard.Application.Contracts/CliMetrics/CliMetricsDtos.cs     [mod]
src/Taskboard.Application/Harness/FinOpsService.cs                   [mod]
src/Taskboard.Application/CliMetrics/CliMetricsService.cs            [mod]
src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs         [mod]
src/Taskboard.Domain/Entities/CliMetrics/CliDailyUsageAggregate.cs   [mod]
src/Taskboard.EntityFrameworkCore/Migrations/*_AddCliTokensEstimatedFlag.cs [new]
src/Taskboard.Server/Program.cs                                      [mod]
src/Taskboard.Blazor/Components/Pages/FinOps.razor                   [mod — rebuild]
src/Taskboard.Blazor/Services/TaskboardClient.cs                     [mod]
tests/Taskboard.Tests.Unit/FinOps/*Tests.cs                          [new/mod]
tests/Taskboard.Tests.Integration/FinOps/*Tests.cs                   [new/mod]
```

## 4. Requirements

### RF-001: Token windows (24h / 7d / 30d)

- **Description:** summary response includes `TokenWindows { Last24h, Last7d,
  Last30d }` summing tokens (harness metrics + CLI aggregates) relative to
  `now` — anchor is always `now` in the UI (the reference `data` anchor is
  TUI-only).
- **Input → Output:** `period` param → `FinOpsSummaryDto.TokenWindows`.

### RF-002: Usage distribution by `Source · Model`

- **Description:** `ModelUsage` map keyed `"<source friendly name> · <model>"`
  → percentage of sessions (reference spec §9 step 5). Harness runs use
  source `Harness`; CLI rows use `AgentCliKind` display name.
- **Rules:** percentages over total session/run count in the period; models
  `null`/`(unset)`/`<synthetic>` group under `(unknown)`.

### RF-003: Activity sparkline bins

- **Description:** `ActivityBins` — per-bucket `{ start, sessions, messages,
  tokens }`. Bucket rule (reference spec §9): `hour` with 24 bins when the
  period span ≤ 2 days; otherwise `day` bins capped at 40.
- **Rules:** bins derive from `lastActivity` of CLI sessions and
  `RecordedAtUtc` of run metrics; empty bins are emitted (zero-filled) so the
  UI renders a continuous series.

### RF-004: Recent sessions table

- **Description:** `RecentSessions` — top 10 by `started` desc, tiebreak
  `lastActivity` desc, then status order `running < finished` (reference
  spec §9 ordering). Each row: `source`, `project|title`, `model`,
  `messages`, `tokens`, `costUsd?`, `estimated`, `status`.
- **Rules:** status derived — `running` when `now - lastActivity ≤ 1800 s`
  (constant `ActiveWindowSecs`, configurable via
  `Taskboard:FinOps:ActiveWindowSeconds`, default 1800 — mirrors env
  `DEVIN_ACTIVE_WINDOW` of the reference); there is no persisted status
  column. `take` fixed at 10.

### RF-005: Source health checklist

- **Description:** the page renders every `CliMetricSource` with its
  `Status`, `LastSyncUtc`, `RowCount` and `LastError` — including sources in
  `Missing` (checklist semantics, not just healthy ones).
- **Rules:** consume `GET /api/local/cli-metrics/sources` directly; no
  duplication inside the finops summary payload.

### RF-006: Estimated-token provenance

- **Description:** `CliSessionMetric.TokensEstimated` (new bool column,
  additive migration, default `false` for existing rows) is set at ingestion
  when the extractor produced tokens via `chars/4`-style estimation instead
  of vendor `usage` columns. DTOs expose `Estimated`; UI shows `~` badge.
- **Rules:** each `ICliDbExtractor` declares whether its token columns are
  real; estimate flag is per-row. `CliUsageSummaryDto` gains
  `EstimatedShare` (0-1) so the card can show "X% estimated".

### RF-007: Flat-rate fallback cost

- **Description:** when `ModelPriceRate` has no match for a CLI session's
  model, cost = `tokens / 1_000_000 * 9.5` USD (reference `USD_PER_MTOKEN`),
  flagged as estimated. Existing `FinOpsAggregationService` applies the
  fallback during its costing pass.
- **Rules:** flat rate is a named constant
  `FinOpsPricing.FallbackUsdPerMTok = 9.5m`; fallback-flagged costs carry
  the same `~` badge in the UI.

### RF-008: Alerts strip

- **Description:** `Alerts: [{ severity: warn|crit, message, atUtc }]` —
  emitted when: (a) zero running sessions in the window; (b) ≥1 source in
  `Error` or `SchemaDrifted`; (c) a run hit `BudgetExceeded` in the period;
  (d) latest day cost > 2× period daily average (and average > 0).
- **Rules:** computed server-side in `GetSummaryAsync`; UI renders dismissible
  `Alert` components (warn → `Warning`, crit → `Danger`).

### RF-009: Page composition

- **Description:** `/finops` loads `finops/summary` + `cli-metrics/sources`
  in parallel on init and on period change; the run-telemetry lookup box is
  preserved unchanged. All money values use `decimal`/`C4`; token counts
  `N0`.
- **Rules:** a failing `sources` call degrades to an empty checklist + toast,
  never blocks the summary; same for `sessions`-level detail.

**Business rules / invariants:**

- No new persistence of external content — everything derives from tables
  already owned by `cli-metrics`/`finops` plus the additive flag.
- Periods: `24h`, `last-7-days`, `last-30-days`, `all` (default
  `last-30-days`, backwards compatible with current values).
- N-Layer ABP: aggregation math in `Application`, contracts in
  `Application.Contracts`, UI stays thin.

## 5. API Contract

**Auth:** unchanged — cookie login + `ApiKeyAuthenticationHandler`, all
endpoints `RequireAuthorization`.

```http
GET /api/harness/finops/summary?period=last-30-days
→ 200 OK
{
  "totalCostUsd": 14.85,
  "totalTokens": 2450000,
  "runsCount": 42,
  "costByAgent": { "Claude": 9.20, "Codex": 4.10 },
  "costByModel": { "claude-3-7-sonnet": 9.20 },
  "dailyCosts": [ { "date": "2026-09-21", "costUsd": 1.20, "totalTokens": 80000 } ],
  "cliUsage": {
    "sessions": 87, "tokensInput": 900000, "tokensOutput": 300000,
    "tokensCached": 50000, "costUsd": 11.40, "estimatedShare": 0.62,
    "costByCli": { "Codex": 8.10, "Claude": 3.30 }
  },
  "tokenWindows": { "last24h": 120000, "last7d": 1400000, "last30d": 2450000 },
  "modelUsage": { "Harness · claude-3-7-sonnet": 40.0, "Codex · gpt-5.6": 35.0 },
  "activityBins": [ { "start": "2026-09-21T00:00:00Z", "sessions": 5, "messages": 210, "tokens": 60000 } ],
  "recentSessions": [
    { "source": "Codex", "title": "fix flaky tests", "model": "gpt-5.6",
      "status": "running", "messages": 12, "tokens": 31000,
      "costUsd": 0.29, "estimated": false,
      "startedAtUtc": "...", "lastActivityUtc": "..." }
  ],
  "alerts": [ { "severity": "warn", "message": "No active sessions in window", "atUtc": "..." } ]
}

GET /api/local/cli-metrics/sources     (existing — reused by the page)
→ 200 [ { "kind": "Codex", "sourceName": "logs_2.sqlite", "path": "~/.codex/...",
          "status": "Available", "schemaDrifted": false,
          "lastSyncUtc": "...", "lastError": null, "rowCount": 1234 } ]
```

**Expected errors:** `400` invalid period · `401/403` auth · `500` as
`{ error: { code, message } }` without internals. `period` values outside
`{24h, last-7-days, last-30-days, all}` → `400`.

## 6. Acceptance Criteria

- [x] **Given** CLI aggregates and run metrics in the period, **when**
  `GET /finops/summary` is called, **then** `tokenWindows` returns correct
  24h/7d/30d sums relative to `now`.
- [x] **Given** a summary response, **when** the page renders, **then** the
  `CliUsage` card shows sessions, in/out/cached tokens, cost and
  `CostByCli` — data that exists today but is not displayed.
- [x] **Given** sessions with mixed models, **when** `modelUsage` is
  computed, **then** keys are `"Source · Model"` and percentages sum to
  100 (± rounding).
- [x] **Given** period `24h`, **when** bins are built, **then** exactly 24
  hourly zero-filled bins are returned; **given** `last-30-days`, **then**
  day bins are returned (≤ 40).
- [x] **Given** a session with `lastActivity` 10 min ago, **when** the
  recent-sessions list renders, **then** its status is `running`; at 40 min
  it is `finished`.
- [x] **Given** a source in `SchemaDrifted`, **when** the checklist renders,
  **then** it shows the drift badge + reason and an alert is present in
  `alerts`.
- [x] **Given** an extractor without real usage columns, **when** its
  sessions are ingested, **then** rows persist `TokensEstimated = true` and
  the UI shows the `~` badge.
- [x] **Given** a CLI session whose model has no `ModelPriceRate`, **when**
  the costing pass runs, **then** cost = tokens × 9.5/1M and the row is
  flagged estimated.
- [x] **Given** `/cli-metrics/sources` failing, **when** the page loads,
  **then** the summary still renders and a toast reports the checklist
  failure.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Empty period data | fresh DB | all blocks render empty-state; alert "no active sessions" |
| `all` period | years of rows | day bins capped at 40; oldest days outside bins are excluded from the sparkline but counted in totals |
| Model `(unset)`/`<synthetic>` | sparse rows | grouped as `(unknown)` in distribution |
| Cost exactly at 2× average | day cost == 2× avg | no alert (strictly greater) |
| Migration on populated table | existing rows | `TokensEstimated` defaults `false`; no data rewrite |

## 7. Task Plan

- [x] **T1 — Contracts + flag:** `TokensEstimated` on `CliSessionMetric` +
  migration; DTO extensions (`TokenWindows`, `ModelUsage`, `ActivityBins`,
  `RecentSessions`, `Alerts`, `EstimatedShare`). Tests: mapping + defaults.
- [x] **T2 — Ingestion provenance:** extractors report whether tokens are
  real; `CliMetricsService` persists the flag. Unit tests per extractor
  capability (`Dado_Quando_Entao`).
- [x] **T3 — Aggregation:** `FinOpsService` computes windows, distribution,
  bins, recent sessions, alerts; `FinOpsAggregationService` flat-rate
  fallback. Unit tests on the math (window sums, bucket rule, alert
  thresholds, fallback pricing).
- [x] **T4 — UI rebuild:** `FinOps.razor` sections (KPIs + windows strip,
  CLI usage card, distribution, sparkline, sessions table, checklist,
  alerts) + `TaskboardClient` methods. Breadcrumb/labels en-us.
- [x] **T5 — Validation:** `dotnet build` (warnings=errors), `dotnet test`
  green, coverage ≥ gate (ratchet — never below current 77%), docs
  en/pt-br, `Status = Done` + PR.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use
  `feature/devin-20260922-finops-dashboard-detail`.
- **Workflows:** do not modify `.github/workflows/**`.
- **Security:** session metadata only — no prompt/message bodies in DTOs,
  logs or UI; `LastError` surfaces schema metadata only; endpoints remain
  authenticated.
- **Architecture:** N-Layer ABP — aggregation in `Application`, contracts in
  `Application.Contracts`; the Blazor page composes existing endpoints
  rather than adding vendor-specific logic.
- **Money:** `decimal` everywhere; flat fallback rate is a named constant,
  not a magic literal.
- **Specs:** changes to `FinOpsSummaryDto`/`CliMetricsDtos` update this
  SPEC; new ingestion sources belong to a future `cli-file-reader` spec —
  do not sneak them in here.

## 9. Definition of Done

- [x] RF-001…RF-009 implemented; ACs covered by tests.
- [x] `dotnet build` clean (`TreatWarningsAsErrors`); `dotnet test` green;
  coverage ≥ gate.
- [x] Migration additive and reversible; existing rows keep
  `TokensEstimated = false`.
- [x] `/finops` renders all new sections and degrades gracefully when
  `cli-metrics` endpoints fail.
- [x] Docs updated (en + pt-br); `Status = Done` + PR open.

## Open Questions / Pending Ambiguity

- Whether `recentSessions` should merge Harness `AgentRun` entries with CLI
  sessions in one list or keep two columns — spec assumes a single merged
  list keyed by recency (reference spec behavior); revisit at T4 if the UI
  gets crowded.
