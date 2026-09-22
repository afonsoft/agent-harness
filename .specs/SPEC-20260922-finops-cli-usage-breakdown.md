# SPEC-20260922-finops-cli-usage-breakdown

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `finops-cli-usage-breakdown` |
| Type | `Feature` (Frontend + API; driver is a reported gap) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / EF Core SQLite / Blazor WASM / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-finops-cli-usage-breakdown` |
| Ticket | [#323 — E18](https://github.com/afonsoft/agent-harness/issues/323) |
| Status | `Done` — delivered in PR #328 |
| Referência | SPEC-20260919-cli-metrics, SPEC-20260919-cli-db-reader, SPEC-20260920-harness-recurring-jobs (RF-004), SPEC-20260922-finops-dashboard-detail |

## 1. User Story

**As a** Harness operator
**I want** the "External CLI usage" card on `/finops` to show a per-CLI table
with Sessions, Tokens in, Tokens out, Tokens cached and Cost for **every**
managed CLI — not only OpenCode — with token estimates when the vendor
database does not report usage
**So that** I can see the real footprint of each external CLI (Devin, Codex,
Claude, Antigravity, Cline, OpenCode) instead of a card that appears to only
track OpenCode.

**Problem context:**

1. Sessions are ingested for all managed CLIs, but only `opencode.db`
   (`session.tokens_*`, `session.cost`) exposes vendor usage columns. Every
   other extractor emits `TokensInput/Output/Cached = null` +
   `TokensEstimated = true`, so the card's totals and the `CostByCli` table
   effectively reflect only OpenCode (verified on host: Devin 404 sessions /
   0 tokens, Codex 39/0, Claude 25/0, Antigravity 5/0, OpenCode 155/real).
2. Vendor sources do carry usable estimation signals that are currently
   discarded: Codex `threads.tokens_used` (real per-thread total), Devin
   `message_nodes.chat_message` length, Cline `hub_events.envelope_json`
   length, Claude context-mode `session_meta.event_count`, Antigravity
   `conversation_summaries` text lengths + `step_count`.
3. Already-ingested rows keep `null` tokens forever: watermark cursors have
   advanced past them and there is no re-extraction trigger.
4. The per-CLI UI shows only cost (`CostByCli` dict + progress bar); sessions
   and token columns are not broken down per CLI.

## 2. Scope

**In scope:**

- New `CliUsageByCliDto` list on `CliUsageSummaryDto` (`ByCli`) carrying
  per-CLI `Sessions`, `TokensInput`, `TokensOutput`, `TokensCached`,
  `CostUsd`, `EstimatedShare`. `CostByCli` is kept unchanged for backward
  compatibility.
- Token estimation in extractors whose vendor schema lacks usage columns
  (chars/4 over `length()` scalars and vendor totals; never extracting
  message/prompt content — privacy boundary preserved).
- Devin source gains the `message_nodes` table (whitelist + new schema
  fingerprint baseline) to estimate `length(chat_message)` per session.
- Re-ingestion trigger: `ICliDbExtractor.DataVersion` persisted on
  `CliMetricSource` (additive EF migration); a version bump clears the
  watermark cursor and forces one full re-extract (upserts are idempotent).
- `/finops` "External CLI usage" card: totals strip stays on top; the
  cost-only table is replaced by a full per-CLI table
  (`CLI | Sessions | Tokens in | Tokens out | Tokens cached | Cost`)
  ordered by Sessions desc, with `~`/`no data` badges.
- `CliMetricsOptions` gains `EstimatedCharsPerToken` (default `4`) and
  `EstimatedTokensPerEvent` (default `1000`).

**Out of scope:**

- File-based/remote readers (claude-code `~/.claude/projects/*.jsonl`,
  copilot `workspaceStorage`, devin-desktop ACP NDJSON, Devin API) — owned by
  a future `cli-file-reader` spec.
- Real vendor cost for subscription/free-tier CLIs (the data does not exist);
  cost keeps the existing `ModelPriceRate`/fallback-rate projection and is
  shown as estimated (`~`).
- Extracting or persisting message/prompt content — only `length()` scalars
  and counters leave the vendor databases.
- Changes to OpenCode extraction (already real vendor data).
- Per-model drill-down inside the CLI table; new period filters.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Application.Contracts` — `FinOpsDtos.cs` gains
  `CliUsageByCliDto` + `CliUsageSummaryDto.ByCli`; `CliMetricsOptions.cs`
  gains estimation knobs; `ICliDbExtractor` gains `DataVersion`;
  `CliMetricSourceStateDto` gains `ExtractorDataVersion`.
- `Taskboard.Domain` — `CliMetricSource` gains `ExtractorDataVersion`
  (int, default `1`) and a `ResetWatermark(now)` method.
- `Taskboard.Integrations/CliDb/Extractors` — estimation per extractor
  (RF-002); `CliDbExtractorBase` exposes `virtual int DataVersion => 1`.
- `Taskboard.Domain.Shared` — `CliDatabaseMap`: `devin-sessions` whitelist
  gains `message_nodes` (and the Devin extractor baseline fingerprint is
  recaptured).
- `Taskboard.Application` — `CliMetricsService.SyncExtractorAsync` compares
  stored vs. declared `DataVersion` and resets the cursor on mismatch;
  `FinOpsService.GetSummaryAsync` builds `ByCli` from `cliRows` grouped by
  `Kind`.
- `Taskboard.EntityFrameworkCore` — `EfCoreCliMetricsRepository`
  (`CliMetricSourceStateDto`/`SaveSourceStateAsync` carry
  `ExtractorDataVersion`); new additive migration
  `AddCliSourceExtractorDataVersion`.
- `Taskboard.Blazor` — `FinOps.razor` per-CLI table.
- EF Core migration: `CliMetricSources.ExtractorDataVersion INTEGER NOT NULL
  DEFAULT 1` (additive; `1` means already-ingested sources do not rescan for
  extractors still at `DataVersion = 1`).

**Files to read before implementing:**

- `src/Taskboard.Application/Harness/FinOpsService.cs` — `CliUsage`
  projection (~lines 136-159).
- `src/Taskboard.Application.Contracts/Harness/Dtos/FinOpsDtos.cs` —
  `CliUsageSummaryDto`.
- `src/Taskboard.Application/CliMetrics/CliMetricsService.cs` — sync loop,
  watermark flow (`GetSourceStateAsync`/`SaveSourceStateAsync`).
- `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` +
  `Extractors/*.cs` — per-CLI extraction.
- `src/Taskboard.Domain/Entities/CliMetrics/CliMetricSource.cs`,
  `CliSessionMetric.cs`, `CliDailyUsageAggregate.cs`.
- `src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs` — whitelists.
- `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs`
  — `RecomputeAggregatesAsync` already sums tokens; no aggregate change
  needed.
- `src/Taskboard.Blazor/Components/Pages/FinOps.razor` (~lines 108-148).
- `tests/Taskboard.Tests.Unit/Harness/FinOpsServiceTests.cs`,
  `tests/Taskboard.Tests.Unit/CliDb/*` — existing patterns.

**Files to create or modify:**

```text
src/Taskboard.Application.Contracts/Harness/Dtos/FinOpsDtos.cs              [modify]
src/Taskboard.Application.Contracts/CliMetrics/CliMetricsOptions.cs         [modify]
src/Taskboard.Application.Contracts/CliMetrics/CliMetricsDtos.cs            [modify — CliMetricSourceStateDto]
src/Taskboard.Application.Contracts/CliDb/ICliDbExtractor.cs                [modify — DataVersion]
src/Taskboard.Application.Contracts/CliDb/CliTokenEstimator.cs              [new — chars/4 + per-event helpers]
src/Taskboard.Domain/Entities/CliMetrics/CliMetricSource.cs                 [modify — ExtractorDataVersion + ResetWatermark]
src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs                        [modify — devin whitelist +message_nodes]
src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs                      [modify — virtual DataVersion]
src/Taskboard.Integrations/CliDb/Extractors/CodexCliDbExtractor.cs          [modify — tokens_used + DataVersion=2]
src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs          [modify — message_nodes estimate + fingerprint + DataVersion=2]
src/Taskboard.Integrations/CliDb/Extractors/ClaudeContextModeCliDbExtractor.cs [modify — event_count estimate + DataVersion=2]
src/Taskboard.Integrations/CliDb/Extractors/AntigravityCliDbExtractor.cs    [modify — text-length estimate + DataVersion=2]
src/Taskboard.Integrations/CliDb/Extractors/ClineCliDbExtractor.cs          [modify — envelope_json length estimate + DataVersion=2]
src/Taskboard.Application/CliMetrics/CliMetricsService.cs                   [modify — DataVersion reset]
src/Taskboard.Application/Harness/FinOpsService.cs                          [modify — ByCli projection]
src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs  [modify — source state field]
src/Taskboard.EntityFrameworkCore/Configurations/CliMetricsConfigurations.cs [modify — new column]
src/Taskboard.EntityFrameworkCore/Migrations/<ts>_AddCliSourceExtractorDataVersion.cs [new]
src/Taskboard.Blazor/Components/Pages/FinOps.razor                          [modify — per-CLI table]
tests/Taskboard.Tests.Unit/Harness/FinOpsServiceTests.cs                    [modify]
tests/Taskboard.Tests.Unit/CliDb/*                                          [new/modify — extractor + rescan tests]
docs/api.md / docs/api.pt-br.md                                             [modify — DTO + endpoint notes]
docs/features.md / docs/features.pt-br.md                                   [modify — cli-metrics section]
```

## 4. Requirements

### RF-001: `ByCli` per-CLI projection on `CliUsageSummaryDto`

- **Description:** `CliUsageSummaryDto` gains
  `IReadOnlyList<CliUsageByCliDto> ByCli` with one row per `AgentCliKind`
  present in the period's `CliDailyUsageAggregate` rows — including kinds
  whose tokens/cost are all zero.
- **Rules:**
  - `CliUsageByCliDto(string Cli, int Sessions, long TokensInput, long
    TokensOutput, long TokensCached, decimal CostUsd, double
    EstimatedShare)`; `Cli` uses `AgentCliMap.GetSpec(kind)?.DisplayName`.
  - `EstimatedShare` = share (0-1) of the kind's sessions in
    `TokensEstimated` buckets (`cliRows.Where(kind && TokensEstimated)
    .Sum(SessionsCount) / Sum(SessionsCount)`).
  - Ordering is contractual: `Sessions` desc, then `CostUsd` desc, then `Cli`
    asc — the UI renders the list as received.
  - `CostByCli` is kept and stays consistent (`ByCli` cost per CLI keyed the
    same way).
- **Input → Output:** `CliDailyUsageAggregate` rows in period → ordered
  `ByCli` list (all kinds present, zeros allowed).

### RF-002: Token estimation for vendors without usage columns

- **Description:** Extractors populate token fields with estimates when the
  vendor schema lacks real usage counters, keeping `TokensEstimated = true`.
  Estimates are derived only from scalar functions (`length()`, counters,
  vendor totals) — message/prompt **content is never extracted**.
- **Rules (per extractor):**
  - **Codex**: `threads.tokens_used` → `TokensInput` (vendor total; the
    in/out/cached split is unknown → estimated). Add `tokens_used` to the
    column list; it is already in the baseline fingerprint.
  - **Devin**: whitelist `message_nodes`; after advancing the `sessions`
    rowid cursor, for each touched `session_id` compute
    `SUM(length(chat_message))` and `COUNT(*)` → `TokensInput =
    ceil(chars / EstimatedCharsPerToken)`, `MessageCount = COUNT(*)`.
    Sessions without nodes keep `null` tokens.
  - **Claude (context-mode)**: `TokensInput = event_count ×
    EstimatedTokensPerEvent` when `event_count` is present.
  - **Antigravity**: `TokensInput = ceil(length(title) + length(preview) +
    length(raw_summary)) / EstimatedCharsPerToken)` (missing columns count
    as 0); `step_count` keeps feeding `MessageCount`.
  - **Cline**: `TokensInput = ceil(SUM(length(envelope_json)) /
    EstimatedCharsPerToken)` per `session_id` group; event count already
    feeds `MessageCount`.
  - New shared helper `CliTokenEstimator` (`FromChars(long chars)` →
    `ceil(chars/EstimatedCharsPerToken)`; `FromEvents(int events)` →
    `events × EstimatedTokensPerEvent`) reading the new
    `CliMetricsOptions` values; extractors receive the options (or the
    resolved constants) via constructor.
  - All estimated sessions keep `TokensEstimated = true`; existing
    `Update()`/re-cost flow is unchanged (estimated sessions get fallback-
    rate projected cost via `FinOpsAggregator`, surfaced with `~`).
  - **Privacy invariant:** only `length(...)` scalars, counts and vendor
    numeric columns are selected — no text column content enters a record,
    log or the Harness DB. The Devin `message_nodes` whitelist entry exists
    solely for `length(chat_message)`/`session_id`/`created_at`/`rowid`.
- **Input → Output:** vendor rows → `CliSessionRecord` with estimated
  `TokensInput` (+ `MessageCount` where available), `TokensEstimated = true`.

### RF-003: `DataVersion` re-ingestion trigger

- **Description:** already-ingested sessions (null tokens, watermark past
  the rows) must be re-extracted once when an extractor starts emitting
  estimates.
- **Rules:**
  - `ICliDbExtractor.DataVersion` (via `CliDbExtractorBase` virtual
    property, default `1`); every estimator-enabled extractor declares `2`.
  - `CliMetricSource.ExtractorDataVersion` (new column, default `1`); stored
    state is returned by `GetSourceStateAsync` and written by
    `SaveSourceStateAsync`.
  - In `SyncExtractorAsync`: when `state.ExtractorDataVersion <
    extractor.DataVersion` for a source → treat as changed (bypass the
    file-signature skip), call `ExtractSinceAsync(null)` (cursor reset),
    idempotent upsert re-writes rows, then persist the new
    `ExtractorDataVersion`.
  - Re-extraction failure must not regress the stored version — persist the
    new version only on a successful (`Available`/`CopiedToTemp`) pass.
- **Input → Output:** version mismatch → one full re-extract per source;
  subsequent syncs stay incremental.

### RF-004: Per-CLI table on `/finops`

- **Description:** the "External CLI usage" card keeps the totals strip
  (Sessions / Tokens in / Tokens out / Tokens cached / Cost + `~` badge) on
  top and replaces the cost-only `CostByCli` table with a full per-CLI
  table.
- **Rules:**
  - Columns: `CLI | Sessions | Tokens in | Tokens out | Tokens cached |
    Cost` — rows rendered in `ByCli` order (Sessions desc).
  - Cost cell: `C4` format; `~` badge when `EstimatedShare > 0` (tooltip:
    share of sessions with estimated tokens).
  - When a row has `Sessions > 0` and all token columns are `0`, append a
    muted badge `no usage data` (tooltip: "vendor does not report token
    usage").
  - The progress-bar cost table is removed; `BarWidth` remains for the other
    tables.
- **Input → Output:** `summary.CliUsage.ByCli` → rendered table.

### RF-005: Tests

- **Description:** cover the new projection, estimation and rescan logic.
- **Rules:**
  - `FinOpsServiceTests`: `ByCli` includes zero-cost kinds, share/ordering
    correct, `CostByCli` parity; existing `Dado_AgregadosCli_...` updated.
  - Extractor tests (existing `CliDb` test fixtures pattern): Codex maps
    `tokens_used`; Cline sums `length(envelope_json)`; Claude multiplies
    `event_count`; Antigravity sums text lengths; Devin session-node rollup
    (in-memory SQLite fixture).
  - `CliMetricsService`/repository test: `ExtractorDataVersion` bump clears
    the cursor and re-ingests (upsert idempotent — no duplicated sessions).
- **Input → Output:** failing tests first (red), then implementation
  (green).

**Business rules / invariants:**

- Estimation never violates the privacy boundary (scalars only).
- Upsert idempotency (`SourceId`+`ExternalId`) is preserved — re-extraction
  must not duplicate sessions.
- `TokensEstimated` semantics unchanged: `true` whenever token counts are
  not vendor-reported in/out/cached counters.
- Aggregates (`CliDailyUsageAggregate`) need no schema change —
  `RecomputeAggregatesAsync` already sums token fields.

## 5. API Contract

**Endpoint:** `GET /api/harness/finops/summary?period={24h|last-7-days|last-30-days|all}` (unchanged route)
**Auth:** existing dashboard auth (RequireAuthorization).

**Response (success) — delta only:**

```json
{
  "cliUsage": {
    "sessions": 628,
    "tokensInput": 342000000,
    "tokensOutput": 1900000,
    "tokensCached": 51700000,
    "costUsd": 2.4961,
    "costByCli": { "OpenCode": 2.4961 },
    "estimatedShare": 0.75,
    "byCli": [
      {
        "cli": "Devin",
        "sessions": 404,
        "tokensInput": 8200000,
        "tokensOutput": 0,
        "tokensCached": 0,
        "costUsd": 0.0779,
        "estimatedShare": 1.0
      },
      {
        "cli": "OpenCode",
        "sessions": 155,
        "tokensInput": 339272132,
        "tokensOutput": 1903428,
        "tokensCached": 51623068,
        "costUsd": 2.4182,
        "estimatedShare": 0.0
      }
    ]
  }
}
```

**Expected errors:** unchanged (`401` unauthenticated; generic error format).

## 6. Acceptance Criteria

- [x] **Given** aggregates exist for OpenCode and Devin **when**
  `GET /api/harness/finops/summary` runs **then** `cliUsage.byCli` contains
  one entry per kind — Devin included even when its cost is `0`.
- [x] **Given** a Codex thread with `tokens_used = 38190` **when** the
  extractor runs **then** the session record carries
  `TokensInput = 38190` and `TokensEstimated = true`.
- [x] **Given** a Devin session whose `message_nodes` total 40_000 chars
  **when** extraction runs **then** `TokensInput = 10_000`
  (`EstimatedCharsPerToken = 4`) and no message text is persisted anywhere.
- [x] **Given** stored `ExtractorDataVersion = 1` and an extractor at `2`
  **when** the next sync runs **then** the source re-extracts from a null
  cursor, updates existing rows in place, and stores version `2`.
- [x] **Given** the `/finops` page loads **when** `ByCli` has rows **then**
  the card shows the totals strip plus a table with the five per-CLI
  columns, ordered by Sessions desc, with `~`/`no usage data` badges.
- [x] **Given** no CLI aggregates **when** the summary is requested **then**
  `cliUsage` remains `null` (unchanged behavior).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| CLI with sessions, all tokens null | `Sessions=25, tokens=0` | Row shows `0`/`$0.00` + `no usage data` badge |
| Partially estimated kind | 3 real + 1 estimated sessions | `EstimatedShare = 0.25`, `~` badge |
| Estimate = 0 chars | empty text columns | `TokensInput = 0` (not null) only when the vendor row exists; session still counts |
| Re-extract throws mid-pass | extractor error | source keeps old `ExtractorDataVersion`, retries next tick |
| Codex `tokens_used` null | column exists but null | `TokensInput = null` → `no usage data` path |
| Period filter `24h` | aggregates outside window | `ByCli` only includes kinds with rows in-period |
| Devin `message_nodes` schema drift | table differs from new baseline | source reports `SchemaDrifted`, sessions table still extracted — drift in one source must not blank the kind |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read files in section 3; confirm fingerprints and
  `message_nodes` schema on the target host; capture the new Devin baseline.
- [x] **T2 — Red tests:** add failing tests per RF-005 (ByCli projection,
  per-extractor estimation, DataVersion rescan).
- [x] **T3 — Contracts + Domain:** `CliUsageByCliDto`, `ByCli`, options,
  `DataVersion`, `CliMetricSource.ExtractorDataVersion` + `ResetWatermark`,
  EF config + migration `AddCliSourceExtractorDataVersion`.
- [x] **T4 — Estimation:** `CliTokenEstimator`; update Codex, Devin
  (whitelist + fingerprint), Claude, Antigravity, Cline extractors.
- [x] **T5 — Sync + FinOps:** `CliMetricsService` DataVersion reset;
  `FinOpsService` `ByCli` projection.
- [x] **T6 — UI:** `FinOps.razor` per-CLI table (RF-004).
- [x] **T7 — Docs:** update `docs/api*.md`, `docs/features*.md` DTO/endpoint
  notes.
- [x] **T8 — Validation:** `dotnet build` (warnings as errors), `dotnet
  test`, coverage ≥ gate (77%), then fill DoD and open the PR.

**7.1 Validation strategy**

Per `.NET` + `Bugfix` rows of the standard table: unit tests for
projection/estimation rules; integration-style extractor tests over
in-memory SQLite fixtures; reproduction test for the missing-CLI gap before
the fix; coverage must not drop below the ratchet (`77%`).

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use
  `feature/devin-20260922-finops-cli-usage-breakdown`.
- **Workflows:** do not modify `.github/workflows/`.
- **Security:** never extract/persist/log message or prompt content —
  `length()` scalars and counters only; no secrets in code or commits.
- **Scope:** no new ingestion sources, no JSONL readers, no vendor-cost
  invention. Stop and ask on ambiguity.
- **Architecture:** no business logic in the Razor page or endpoints;
  projection stays in `FinOpsService`, estimation in extractors.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests.
- [x] Edge cases handled.
- [x] `dotnet build` clean (TreatWarningsAsErrors), `dotnet test` green,
  coverage ≥ ratchet gate.
- [x] Guardrails respected; no PII/tokens/message content in logs or DB.
- [x] `docs/api*.md` + `docs/features*.md` updated.

**Next action after DoD is complete:** set `Status = Done` and open the PR
on `feature/devin-20260922-finops-cli-usage-breakdown` referencing the
ticket.

## Open Questions / Pending Ambiguity

- `EstimatedTokensPerEvent` default (`1000`) is a tuning knob — adjust after
  measuring against real event volumes.
- Devin `message_nodes` full-scan cost on multi-GB databases: the per-
  session rollup is gated by the `sessions` watermark (only touched sessions
  are re-measured); if profiling shows it is still heavy, cap with the
  existing row/timeout budgets and document the truncation.
