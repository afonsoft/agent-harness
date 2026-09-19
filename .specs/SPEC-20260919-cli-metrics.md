# SPEC-20260919-cli-metrics

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cli-metrics` |
| Type | `Feature` (API + Infra + Frontend) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / EF Core SQLite / Blazor WASM` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-cli-metrics` |
| Ticket | `[A DEFINIR]` |
| Status | `Draft` |
| Capability map | `.specs/CAPABILITY-MAP-cli-metrics.md` (module `cli-metrics`) |

## 1. User Story

**As a** Harness operator
**I want** usage metrics extracted from the agent CLIs' local SQLite databases —
sessions per day, message counts, token usage when the vendor records it, last
activity — aggregated into the Harness database
**So that** the `/agents` page shows real per-CLI usage, the FinOps dashboard
(SPEC-20260919-ade-observability-finops) gets a data source for historical
consumption, and I can audit which agents are actually being used and when.

**Problem context:**
Harness spawns agent CLIs (one-shot orchestration, interactive sessions,
terminal) but has no visibility into what those CLIs do outside Harness runs —
a developer using `claude` or `codex` directly leaves usage data only inside
the vendor's local database. The FinOps spec (Draft) defines `RunCostMetric`
and budget caps for Harness-originated runs, but has no source for external
CLI activity. This module ingests that external telemetry — metadata only —
into first-class Harness entities.

## 2. Scope

**In scope:**
- Domain entities: `CliMetricSource` (kind, path, fingerprint, status,
  watermark cursor, last sync), `CliSessionMetric` (normalized session rows,
  deduped by source+external id), `CliDailyUsageAggregate` (per kind/model/day
  rollups).
- `CliMetricsSyncService` (`BackgroundService`, pattern
  `SkillsSyncHostedService` + `PeriodicTimer`): interval configurable via
  `Taskboard:CliMetrics:SyncIntervalMinutes` (default 15), feature flag
  `Taskboard:CliMetrics:Enabled` (default `true` — read-only ingestion).
- Manual trigger: `POST /api/local/cli-metrics/sync` (admin auth), idempotent.
- Incremental ingestion: per-source watermark via
  `ICliDbExtractor.ExtractSinceAsync`; skip when file mtime+size unchanged;
  upsert dedupe on `(SourceId, ExternalId)`.
- REST: `GET /api/local/cli-metrics/sources`,
  `GET /api/local/cli-metrics/summary?period=`,
  `GET /api/local/cli-metrics/sessions?kind=&from=&to=&take=`.
- `/agents` page: per-row metrics (sessions last 7d, last activity, tokens
  when known) + "Sync now" button + per-source status badge.
- FinOps feed: `ICliUsageMetricsProvider` contract consumed by
  `ade-observability-finops` (contract-first — integration lands with that
  spec; this spec ships the provider).
- EF Core migration; retention cleanup (configurable, default 90 days for raw
  session rows — aggregates kept indefinitely).

**Out of scope:**
- Reading external DBs — that is `cli-db-reader` (dependency).
- Rich analytics dashboards/charts — owned by `ade-observability-finops` and
  `ade-cockpit-hitl`; `/agents` shows compact metrics only.
- Message/prompt content storage (privacy boundary).
- File-system watchers / real-time updates (polling sync only).
- JSONL/non-SQLite sources.
- Cross-host aggregation.

## 3. Technical Context

**Where the change happens:**
- `Taskboard.Domain` — entities `CliMetricSource`, `CliSessionMetric`,
  `CliDailyUsageAggregate` (aggregates rooted at `CliMetricSource`); value
  objects reuse `AgentCliKind`/`AgentType`.
- `Taskboard.EntityFrameworkCore` — configurations + migration
  `AddCliMetrics`; indexes on `(SourceId, ExternalId)` unique, `StartedAtUtc`,
  `(Kind, Day)` for aggregates.
- `Taskboard.Application.Contracts` — `ICliMetricsService`,
  `ICliUsageMetricsProvider`, DTOs.
- `Taskboard.Application` — `CliMetricsService` (MediatR commands/queries per
  repo convention).
- `Taskboard.Server` — `CliMetricsSyncService` (hosted), endpoints in
  `Program.cs`, `RequireAuthorization` on all.
- `Taskboard.Blazor` — `/agents` metrics section + sync button.

**Files to read before implementing:**
- `src/Taskboard.Integrations/CliDb/*` (module `cli-db-reader` — dependency)
- `src/Taskboard.Server/Services/SkillsSyncHostedService.cs` — hosted-service
  pattern (never block/fail startup).
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` —
  `BackgroundService` + DI pattern.
- `src/Taskboard.Blazor/Components/Pages/Agents.razor` — table to extend.
- `src/Taskboard.Server/Program.cs` — `agent-clis` endpoints (~line 1464),
  auth, SSE.
- `src/Taskboard.Application.Contracts/Operations/OperationLog.cs` — ring
  buffer pattern for sync progress reporting.
- `.specs/SPEC-20260919-ade-observability-finops.md` — `RunCostMetric`,
  `IFinOpsService` (feed target).
- `.specs/SPEC-20260918-cli-agents-expansion.md` — `/agents` page states.

**Files to create or modify:**
```text
src/Taskboard.Domain/Entities/CliMetrics/CliMetricSource.cs        [new]
src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs       [new]
src/Taskboard.Domain/Entities/CliMetrics/CliDailyUsageAggregate.cs [new]
src/Taskboard.EntityFrameworkCore/Configurations/CliMetric*.cs     [new]
src/Taskboard.EntityFrameworkCore/Migrations/*_AddCliMetrics.cs    [new]
src/Taskboard.Application.Contracts/CliMetrics/ICliMetricsService.cs        [new]
src/Taskboard.Application.Contracts/CliMetrics/ICliUsageMetricsProvider.cs  [new]
src/Taskboard.Application.Contracts/CliMetrics/CliMetricsDtos.cs   [new]
src/Taskboard.Application/CliMetrics/CliMetricsService.cs          [new]
src/Taskboard.Server/Services/CliMetricsSyncService.cs             [new]
src/Taskboard.Server/Program.cs                                    [mod: endpoints + DI + hosted service]
src/Taskboard.Blazor/Components/Pages/Agents.razor                 [mod]
src/Taskboard.Blazor/Services/TaskboardClient.cs                   [mod]
tests/Taskboard.Tests.Unit/CliMetrics/*Tests.cs                    [new]
tests/Taskboard.Tests.Integration/CliMetrics/*Tests.cs             [new]
```

## 4. Requirements

### RF-001: Scheduled + manual sync
- **Description:** `CliMetricsSyncService` runs on
  `Taskboard:CliMetrics:SyncIntervalMinutes` (default 15, min 1) when
  `Enabled=true`; `POST /api/local/cli-metrics/sync` triggers an immediate run.
- **Rules:** runs never overlap (per-run `SemaphoreSlim`); a second manual
  trigger during a run returns `202` with the in-flight status; the hosted
  service never blocks or fails startup (pattern `SkillsSyncHostedService`).

### RF-002: Incremental ingestion with watermark
- **Description:** per source file, store the extractor's opaque cursor plus
  file `mtime`/`size`. Skip extraction when mtime+size are unchanged; otherwise
  call `ExtractSinceAsync(watermark)` and advance the cursor.
- **Rules:** watermark updates only after the batch is persisted (at-least-once
  + dedupe, never at-most-once).

### RF-003: Idempotent upsert
- **Description:** `CliSessionMetric` rows are deduped by
  `(CliMetricSourceId, ExternalId)`; re-ingested rows update mutable fields
  (`EndedAtUtc`, `MessageCount`, tokens) instead of duplicating.
- **Rules:** optimistic concurrency `Version` per repo standard; unique index
  enforced at the DB level.

### RF-004: Aggregates
- **Description:** after each sync, roll session rows into
  `CliDailyUsageAggregate` (`Kind`, `Day`, `SessionsCount`, `MessagesCount`,
  `Tokens*`, `ModelsUsed[]`).
- **Input → Output:** session rows → per-day upserted aggregates.

### RF-005: REST surface
- **Description:** sources status, summary aggregates, and session listing —
  all `RequireAuthorization`, all errors `{ error: { code, message } }`.
- **Rules:** `sessions` supports `kind`, `from`, `to`, `take` (max 500);
  `summary` supports `period` (`7d|30d|90d|all`, default `30d`).

### RF-006: `/agents` UI section
- **Description:** each CLI row shows sessions (7d), last activity timestamp,
  and token total when available; a "Sync now" button triggers RF-001 manual
  sync and refreshes the row; sources in `SchemaDrifted`/`Error` get a badge
  with the reason.

### RF-007: FinOps feed
- **Description:** `ICliUsageMetricsProvider` exposes per-kind/per-model token
  and session aggregates over a date range so `ade-observability-finops` can
  merge external CLI usage into `RunCostMetric` rollups.
- **Rules:** provider is contract-only here; no direct reference to
  `FinOpsService` (that spec is Draft — integration lands when it does).

### RF-008: Failure isolation + retention
- **Description:** a failing source never aborts the sync of others; failures
  are recorded on `CliMetricSource.LastError` (schema metadata only). Retention
  job deletes `CliSessionMetric` rows older than
  `Taskboard:CliMetrics:RetentionDays` (default 90); aggregates are retained.

**Business rules / invariants:**
- Only `cli-db-reader` touches external files — this module never builds a
  SQLite connection string for a vendor DB.
- Extracted data is metadata only; no message/prompt body columns exist in the
  schema (enforced by record shape, not by filtering after the fact).
- All endpoints authenticated; sync is admin-level.

## 5. API Contract

**Auth:** existing (cookie login + `ApiKeyAuthenticationHandler`); all
endpoints `RequireAuthorization`; `sync` requires admin.

```http
POST /api/local/cli-metrics/sync
→ 202 { "state": "Running" } | 200 { "state": "Running", "inFlight": true }

GET /api/local/cli-metrics/sources
→ 200 [ { "kind": "Codex", "path": "~/.codex/logs_2.sqlite",
          "status": "Available", "schemaDrifted": false,
          "lastSyncUtc": "...", "lastError": null, "rowCount": 1234 } ]

GET /api/local/cli-metrics/summary?period=30d
→ 200 { "period": "30d",
        "totals": { "sessions": 87, "messages": 4210, "tokens": 1250000 },
        "byKind": { "Codex": { "sessions": 40, "tokens": 900000 }, ... },
        "byDay": [ { "day": "2026-09-19", "sessions": 5, "tokens": 60000 } ] }

GET /api/local/cli-metrics/sessions?kind=Codex&take=50
→ 200 [ { "externalId": "...", "title": "...", "startedAtUtc": "...",
          "endedAtUtc": "...", "messageCount": 12, "model": "gpt-5.6",
          "inputTokens": 3100, "outputTokens": 800 } ]
```

**Expected errors:** `400` validation · `401/403` auth · `409` sync overlap
handled as `200 inFlight` · `500` surfaced as `{ error }` without internals.

## 6. Acceptance Criteria

- [ ] **Given** a fresh install, **when** the first sync runs, **then** all
  detected sources are ingested and `sources` reports their status.
- [ ] **Given** a synced source whose file is unchanged (same mtime+size),
  **when** the next sync runs, **then** extraction is skipped (no DB open).
- [ ] **Given** a synced source with 5 new sessions, **when** sync runs,
  **then** only the new rows are read (watermark) and totals increase by 5.
- [ ] **Given** re-ingested rows with same `ExternalId`, **when** persisted,
  **then** no duplicates exist (unique index) and mutable fields update.
- [ ] **Given** one corrupt source, **when** sync runs, **then** other sources
  sync normally and the corrupt one shows `Error` + reason.
- [ ] **Given** `Enabled=false`, **when** the server runs, **then** no
  background sync happens and `sync` returns `404`.
- [ ] **Given** rows older than retention, **when** the retention pass runs,
  **then** raw rows are deleted but daily aggregates remain.
- [ ] **Given** `/agents` loaded, **when** a CLI has metrics, **then** the row
  shows sessions(7d) + last activity + tokens badge.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| CLI DB appears after startup | new file mid-run | next interval picks it up (`Missing → Available`) |
| Watermark beyond end | vendor truncated table | cursor resets to table end; logged |
| Clock skew | session `StartedAtUtc` in future | clamp to `now`, flag in row |
| Manual sync during scheduled run | POST while running | `200 { inFlight: true }`, single run continues |
| Huge backfill | first sync, 50k rows | chunked batches (500/commit), sync reports progress |

## 7. Task Plan

- [ ] **T1 — Domain + migration:** entities, EF configs, unique indexes,
  `AddCliMetrics` migration. Tests: mapping, uniqueness.
- [ ] **T2 — Ingestion service:** `CliMetricsService` — watermark logic,
  chunked upsert, aggregate rollup, retention. Unit tests with in-memory
  SQLite + fake extractors (`Dado_Quando_Entao`).
- [ ] **T3 — Hosted service + flag:** `CliMetricsSyncService` (PeriodicTimer,
  overlap guard, startup-safe), config binding, flag behavior tests.
- [ ] **T4 — Endpoints:** `sync`, `sources`, `summary`, `sessions` + auth +
  error format. Integration tests.
- [ ] **T5 — UI:** `/agents` metrics columns + badge + sync button;
  `TaskboardClient` methods.
- [ ] **T6 — FinOps provider:** `ICliUsageMetricsProvider` implementation +
  contract tests (consumed later by ade-observability-finops).
- [ ] **T7 — Validation:** `dotnet build` 0 warnings, `dotnet test` green,
  coverage ≥ gate (ratchet), docs en/pt-br, `Status = Done` + PR.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use
  `feature/devin-20260919-cli-metrics`.
- **Workflows:** do not modify `.github/workflows/**`.
- **Security:** session metadata only — no prompt/message bodies in schema,
  queries, logs or API responses; `LastError` stores schema metadata, never
  row content; endpoints authenticated.
- **Architecture:** N-Layer ABP — domain entities in `Domain`, DTOs/contracts
  in `Application.Contracts`, orchestration in `Application`/`Server`, zero
  vendor-SQL knowledge outside `Integrations`.
- **Scope:** no dashboards beyond the `/agents` section; no content
  extraction; no file watchers.
- **Specs:** contract changes here update this SPEC; `RunCostMetric`
  integration belongs to the finops spec's next revision.

## 9. Definition of Done

- [ ] RF-001…RF-008 implemented; ACs covered by tests.
- [ ] `dotnet build` clean (`TreatWarningsAsErrors`); `dotnet test` green;
  coverage ≥ gate.
- [ ] Migration applied and reversible; unique dedupe index verified.
- [ ] Flag off → zero ingestion and `sync` 404.
- [ ] Docs updated (en + pt-br); `Status = Done` + PR open.

## Open Questions / Pending Ambiguity

- Cost attribution for external CLI usage depends on the `ModelPriceRate`
  table landing in `ade-observability-finops` — until then tokens are stored
  without a USD estimate.
