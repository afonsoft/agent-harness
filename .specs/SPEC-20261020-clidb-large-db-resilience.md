# SPEC-20261020-clidb-large-db-resilience

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `clidb-large-db-resilience` |
| Type | `Bugfix` |
| Stack | `.NET` |
| Repository | `afonsoft/agent-harness` |
| Branch | `devin/20261020-prod-oom-hardening` |
| Ticket | [#578](https://github.com/afonsoft/agent-harness/issues/578) |
| Status | `Approved` |

## 1. User Story

**As a** Harness operator
**I want** the CliDb extractors to keep working (degraded but functional) on CLI databases larger than the 4GB file-size cap, and to stop spamming identical warnings every cycle
**So that** Devin session sync is not permanently dead once `sessions.db` grows past the cap, and the journal is not flooded (377 identical stack warnings in 7 days on prod).

**Problem context:** Prod evidence 2026-10-20 — `~/.local/share/devin/cli/sessions.db` is **5.6GB > `CliDbReadOptions.MaxFileSizeBytes` (4GiB)**. `SqliteCliDatabaseReader.OpenAsync` throws `CliDbReadException` on the size check before opening; `CliDbExtractorBase.ExtractPathAsync` logs a Warn **with full stack** every ~15min forever. The cap exists to prevent copying multi-GB DBs (+WAL) to `/tmp` — correct for the temp-copy path, but it also blocks *in-place* incremental reads that would be cheap (rowid cursor + `LIMIT` already implemented).

## 2. Scope

**In scope:**
- Split the size cap: temp-copy path keeps `MaxFileSizeBytes`; in-place read-only open gets a separate (larger) limit or per-source override.
- For oversized WAL-mode DBs: open in place read-only (`Mode=ReadOnly`); if that fails on shm/WAL access, retry with `immutable=1` and mark the extraction result as possibly-stale (ignores uncommitted WAL frames) — never temp-copy a file over the copy cap.
- Warn dedup: identical per-path extraction failures log Warn once per process boot, then Debug; a periodic summary line states counts.
- Per-source `MaxFileSizeBytes` override on `CliDbSource` (optional, null = global default).

**Out of scope:**
- Rotating/vacuuming the Devin `sessions.db` (ops task, different repo's file).
- Changing the incremental cursor/watermark logic (already correct).
- Secret-column filtering / whitelist rules (unchanged).

## 3. Technical Context

**Where the change happens:** `Taskboard.Integrations.CliDb` — reader options, `OpenAsync` decision tree, connection surface reporting; `CliDbExtractorBase` for failure dedup.

**Files to read before implementing:**
- `src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs` — size check (lines ~61–66), `OpenCopy` temp-copy path (~91–122), `OpenReadOnly` (~124–134)
- `src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs` — `ExtractPathAsync` warn path (~225–236)
- `src/Taskboard.Integrations/CliDb/CliDatabaseLocator.cs` — source → path resolution
- `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` — the failing extractor
- `CliDbReadOptions`, `CliDbSource`, `CliExtractionResult` definitions (Application.Contracts)
- Tests under `tests/` covering the reader and extractors

**Files to create or modify:**
```text
src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs   (modify — split caps, in-place/immutable open)
src/Taskboard.Integrations/CliDb/CliDbExtractorBase.cs        (modify — warn dedup)
src/Taskboard.Application.Contracts/CliDb/*                   (modify — options/source fields, stale flag)
tests/Taskboard.Tests.Unit/Integrations/CliDb/*               (new/updated tests)
```

## 4. Requirements

### RF-001: Split size limits
- **Description:** `CliDbReadOptions` gains `MaxCopyFileSizeBytes` (replaces the current `MaxFileSizeBytes` semantics for the temp-copy path; default unchanged 4GiB) and `MaxInPlaceFileSizeBytes` (default e.g. 32GiB). `CliDbSource` may override either per source.
- **Rules:** file > `MaxInPlaceFileSizeBytes` → fail as today; `MaxCopy` < file ≤ `MaxInPlace` → in-place open only, temp-copy path skipped entirely.

### RF-002: In-place open for oversized DBs
- **Description:** Files over the copy cap open via `Mode=ReadOnly` in place. If the open/probe fails due to WAL `-shm` access, retry once with `immutable=1` (static committed view); the resulting `ICliDbConnection` exposes `ReadStaleSnapshot=true` and the extractor records it in the source status/reasons.
- **Rules:** never `File.Copy` anything over `MaxCopyFileSizeBytes`; unchanged `IsUnderHome`, whitelist, secret-column and cursor semantics.

### RF-003: Warning dedup
- **Description:** `ExtractPathAsync` logs a full Warn+stack only on the first failure per (source, path, error-kind) per process lifetime; repeats log Debug. A per-cycle summary may aggregate reasons as today.
- **Input → Output:** same failure N times → 1 Warn + N-1 Debug entries.

### RF-004: Extraction continues
- **Description:** With an in-place open succeeding, `ExtractSourceAsync` runs normally; `CliExtractionResult` status = `Available` (or `CopiedToTemp`→new `ReadStale`/`Degraded` status when immutable fallback engaged — add enum member, do not reuse `CopiedToTemp`).

**Business rules / invariants:**
- Read-only guarantee preserved everywhere — never write to foreign CLI DBs.
- Immutable fallback must be visibly marked in status (data may lag by the WAL tail).
- No per-cycle Warn storms; journal noise ≤1 Warn per distinct failure per boot.

## 5. API Contract

No REST changes. `CliDbSourceStatus` enum gains a degraded-read member — surfaces wherever statuses are serialized (verify JSON consumers tolerate unknown enums or add `JsonStringEnumConverter`-safe handling).

## 6. Acceptance Criteria

- [ ] **Given** a 5.6GB WAL-mode `sessions.db`, **when** the Devin extractor runs, **then** sessions extract via in-place read (or stale-snapshot) instead of throwing `exceeds size cap`.
- [ ] **Given** the same DB failing repeatedly, **when** 3 cycles run, **then** journal shows 1 Warn + 2 Debug for that path.
- [ ] **Given** a file > `MaxInPlaceFileSizeBytes`, **when** opened, **then** `CliDbReadException` still throws (hard ceiling).
- [ ] **Given** a DB under `MaxCopy`, **when** WAL files exist, **then** the existing temp-copy path is used unchanged.
- [ ] **Given** immutable fallback, **when** results serialize, **then** status reflects degraded/stale read.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| WAL db, shm readable | >4GB file | `Mode=ReadOnly` in place, fresh data |
| WAL db, shm not writable | >4GB file | `immutable=1` fallback, `ReadStale` status |
| Corrupt/locked file | any | same exception types as today, deduped logging |

## 7. Task Plan

- [ ] **T1 — Discovery:** read section 3 files; confirm `CliDbSourceStatus` consumers; check `Microsoft.Data.Sqlite` connection-string support for `immutable` (URI `?immutable=1` via `Mode=ReadOnly` + filename URI or `SqliteConnectionStringBuilder` path handling).
- [ ] **T2 — Implementation:** split options + source overrides; `OpenAsync` decision tree; `ReadStale` surface; warn dedup in extractor base.
- [ ] **T3 — Verification:** unit tests with fixture DBs (oversized file via sparse file or mocked `FileInfo` seam — keep tests fast, no real 5GB file); dedup test; immutable fallback test.
- [ ] **T4 — Validation:** `dotnet build`, `dotnet test`, coverage gate.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- Branch `devin/20261020-prod-oom-hardening`; never `main`/`master`/`develop`.
- Read-only semantics preserved — security boundary of the CliDb subsystem.
- Do not log row contents, paths containing secrets, or PII.
- Ops follow-up (not code): document `sqlite3 sessions.db "PRAGMA wal_checkpoint(TRUNCATE); VACUUM"` or Devin DB rotation in `docs/` runbook section.

## 9. Definition of Done

- [ ] All RFs implemented.
- [ ] Acceptance criteria covered by tests.
- [ ] Prod evidence re-check: after deploy, journal shows ≤1 size-cap Warn per boot and Devin sessions extract again (or degrade cleanly).
- [ ] SPEC Status → `Done`; PR opened.

## Open Questions / Pending Ambiguity

- Exact `immutable=1` wiring on `Microsoft.Data.Sqlite` 10.x (URI `file:` path vs `Mode`) — resolve during T1 against the actual package version.
