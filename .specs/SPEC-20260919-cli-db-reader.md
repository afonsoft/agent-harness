# SPEC-20260919-cli-db-reader

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cli-db-reader` |
| Type | `Feature` (Infra — data access layer) |
| Stack | `.NET 10 / Microsoft.Data.Sqlite / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-cli-db-reader` |
| Ticket | `[A DEFINIR]` |
| Status | `Draft` |
| Capability map | `.specs/CAPABILITY-MAP-cli-metrics.md` (module `cli-db-reader`) |

## 1. User Story

**As a** Harness engineer
**I want** a safe, read-only access layer over the SQLite databases that managed
agent CLIs keep on the host
**So that** metrics features can extract session and usage data without risking
corruption of a live CLI database, leaking credentials, or breaking when a
vendor changes its schema.

**Problem context:**
The 13 CLIs managed by `AgentCliMap` persist local state in SQLite. Verified on
this host (2026-09-19): Codex (`~/.codex/*_N.sqlite`, WAL mode), OpenCode
(`~/.local/share/opencode/opencode.db`), Devin CLI
(`~/.local/share/devin/cli/sessions.db`), Antigravity
(`~/.gemini/antigravity-cli/conversations/*.db` + `conversation_summaries.db`),
Cline (`~/.cline/data/db/*.db`), and Claude's context-mode plugin
(`~/.claude/context-mode/sessions/*.db`). These databases are owned by external
tools: they can be locked (WAL), their schemas change without notice, and some
contain credential tables (`opencode.db: credential, account`; `cline:
connectors.db`). Today nothing in the codebase reads them — every metrics
feature would otherwise re-implement unsafe ad-hoc access.

## 2. Scope

**In scope:**
- `CliDatabaseMap` (Domain.Shared): declarative registry mapping
  `AgentCliKind` → list of `CliDbSource` descriptors (path pattern relative to
  `$HOME`, glob support, whitelisted tables, denied tables, experimental flag).
- `ICliDatabaseLocator`: resolves sources to existing absolute paths (expands
  `~`, applies globs, reports missing files).
- `ICliDatabaseReader`: opens connections with `Mode=ReadOnly`; when the source
  is WAL-mode or busy/locked, copies `db`+`db-wal`+`db-shm` to a temp directory
  and reads the copy; temp copies are always deleted.
- Schema fingerprinting: `PRAGMA user_version`, `PRAGMA application_id` and
  `sqlite_master` table set per source → `CliDbSchemaFingerprint`; mismatch →
  source status `SchemaDrifted`, extractor skipped gracefully.
- `ICliDbExtractor` per supported CLI: whitelisted SQL only, producing
  normalized records — `CliSessionRecord` (`Source`, `ExternalId`, `Title?`,
  `StartedAtUtc`, `EndedAtUtc?`, `MessageCount?`, `ModelName?`, `Tokens*?`) and
  `CliUsageRecord` when the vendor schema exposes token columns.
- `CliDbSourceStatus` per source: `Available | Missing | Locked(copied) |
  SchemaDrifted | Error`.
- Read budget: row `LIMIT` caps, per-query timeout, file size cap.

**Out of scope:**
- Any write/DDL/PRAGMA-modifying statement against an external database.
- Persistence into the Harness database, scheduling, HTTP endpoints, UI — owned
  by module `cli-metrics` (SPEC-20260919-cli-metrics).
- Extraction of message/prompt content (privacy boundary — approved: session
  metadata + aggregates only).
- Non-SQLite stores (JSONL session logs such as `~/.claude/projects/`,
  `~/.codex/sessions/` rollouts) — a future `cli-file-reader` module.
- OpenClaw databases (`~/.openclaw/**`) — out of v1 per approved scope.
- Cursor `ai-code-tracking.db` — Cursor is not a managed `AgentCliKind`.

## 3. Technical Context

**Where the change happens:**
- `Taskboard.Domain.Shared` — `CliDatabaseMap`, `CliDbSource`, status enums
  (next to `AgentCliMap`, which already owns per-CLI knowledge).
- `Taskboard.Application.Contracts` — `ICliDatabaseLocator`,
  `ICliDatabaseReader`, `ICliDbExtractor`, normalized record DTOs.
- `Taskboard.Integrations/CliDb/` — implementations. `Microsoft.Data.Sqlite`
  is already in the dependency graph via the EF Core SQLite provider.
- Registered DI in `Taskboard.Server/Program.cs` (pattern: existing
  `Integrations` service registration block).

**Files to read before implementing:**
- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs` — `AgentCliKind`,
  `AgentCliSpec` (extend or sibling map; do not break existing callers).
- `src/Taskboard.Integrations/Agents/AgentCliStatusService.cs` — probe pattern
  (`PATH`/version/auth) to mirror for DB probing.
- `src/Taskboard.Integrations/Workspace/WorkspaceService.cs` — `ClampToHome`,
  `EnsureRoot` for path safety.
- `src/Taskboard.Server/Program.cs` — DI registration + auth conventions.
- `.specs/CAPABILITY-MAP-cli-metrics.md` — verified source inventory.

**Files to create or modify:**
```text
src/Taskboard.Domain.Shared/Agents/CliDatabaseMap.cs              [new]
src/Taskboard.Domain.Shared/Agents/CliDbSource.cs                 [new]
src/Taskboard.Domain.Shared/Agents/CliDbSourceStatus.cs           [new]
src/Taskboard.Application.Contracts/CliDb/ICliDatabaseLocator.cs  [new]
src/Taskboard.Application.Contracts/CliDb/ICliDatabaseReader.cs   [new]
src/Taskboard.Application.Contracts/CliDb/ICliDbExtractor.cs      [new]
src/Taskboard.Application.Contracts/CliDb/CliDbRecords.cs         [new]
src/Taskboard.Integrations/CliDb/CliDatabaseLocator.cs            [new]
src/Taskboard.Integrations/CliDb/SqliteCliDatabaseReader.cs       [new]
src/Taskboard.Integrations/CliDb/CliDbSchemaFingerprint.cs        [new]
src/Taskboard.Integrations/CliDb/Extractors/*.cs                  [new — one per CLI]
src/Taskboard.Server/Program.cs                                   [mod: DI]
tests/Taskboard.Tests.Unit/CliDb/*Tests.cs                        [new]
```

## 4. Requirements

### RF-001: Declarative source registry
- **Description:** `CliDatabaseMap` maps each `AgentCliKind` to zero or more
  `CliDbSource` entries. v1 registers the verified inventory from the
  capability map; kinds without a detected DB register an empty list.
- **Rules:** paths are stored relative to `$HOME`; glob entries (e.g.
  `conversations/*.db`) are first-class; each entry declares `WhitelistTables`
  and `DeniedTables`.

### RF-002: Read-only, corruption-safe access
- **Description:** all connections use `Mode=ReadOnly`. When the file is
  WAL-mode (`journal_mode=wal`, detected via `-wal`/`-shm` siblings or a failed
  ro open) or returns `SQLITE_BUSY`, the reader copies `db`/`db-wal`/`db-shm`
  into a temp dir and reads the copy.
- **Rules:** never open rw, never create journal files on the source, never run
  `PRAGMA` that mutates; connections use `Pooling=false` so file handles are
  released immediately; temp copies deleted in `finally`; a torn/corrupt copy
  (live writer mid-copy) retries once before reporting `Error`; paths resolved
  under `$HOME` via `ClampToHome`-equivalent checks — absolute paths outside
  `$HOME` are rejected.

### RF-003: Schema fingerprint + drift detection
- **Description:** before extraction, compute `CliDbSchemaFingerprint`
  (`user_version`, `application_id`, sorted whitelisted table set + column
  names from `PRAGMA table_info`). Compare with the fingerprint recorded when
  the extractor was last verified.
- **Rules:** mismatch → status `SchemaDrifted`, extractor returns empty result
  with reason, never throws into callers; fingerprint diffs are logged (schema
  names only, no row data).

### RF-004: Whitelisted normalized extraction
- **Description:** each `ICliDbExtractor` executes only queries against
  `WhitelistTables`/`WhitelistColumns` declared in `CliDatabaseMap`, yielding
  `CliSessionRecord` / `CliUsageRecord`.
- **Rules:** tables in `DeniedTables` (`credential`, `account`,
  `account_state`, `control_account`, `permission`, `connectors`) are never
  queried — a query referencing one fails validation before execution. Column
  lists that look like secrets (name matches `token|secret|key|credential|password`,
  case-insensitive) are excluded from selection even when whitelisted.

### RF-005: Read budget
- **Description:** every query carries `LIMIT` (default 10 000 rows), a command
  timeout (default 5 s), and sources larger than a configurable size cap
  (default 512 MB) are reported `Error` instead of opened.

### RF-006: Source status reporting
- **Description:** `ICliDatabaseLocator.GetStatus()` returns per-kind,
  per-file status (`Available`, `Missing`, `SchemaDrifted`, `Error`, plus
  `CopiedToTemp` marker when the WAL-copy path was used).
- **Input → Output:** scan request → `IReadOnlyList<CliDbSourceStatus>`.

**Business rules / invariants:**
- The reader never blocks longer than the per-source timeout; one broken
  source never affects others.
- Extraction is append-style: extractors expose `ExtractSinceAsync(cursor)`
  accepting an opaque watermark cursor defined by the extractor (rowid or
  timestamp) so `cli-metrics` can ingest incrementally.
- No row content leaves the Integrations layer except through the normalized
  records — vendor SQL never reaches Application/Server.

## 5. API Contract

N/A — internal service layer. Diagnostics surface through the `cli-metrics`
endpoints (`GET /api/local/cli-metrics/sources`, see SPEC-20260919-cli-metrics).

## 6. Acceptance Criteria

- [ ] **Given** a WAL-mode Codex database open by another process, **when** the
  reader extracts sessions, **then** it reads a temp copy, the source file gains
  no journal files, and no `SQLITE_BUSY` reaches the caller.
- [ ] **Given** `opencode.db`, **when** the OpenCode extractor runs, **then**
  only whitelisted tables are queried and no column from `credential`/`account*`
  appears in any result or log.
- [ ] **Given** a schema whose whitelisted table lost a mapped column, **when**
  fingerprinting runs, **then** the source reports `SchemaDrifted` and
  extraction returns empty without throwing.
- [ ] **Given** a missing database (CLI not installed), **when** locating
  sources, **then** status is `Missing` — no directory or file is created.
- [ ] **Given** `ExtractSinceAsync(cursor)` with a cursor mid-table, **when**
  invoked twice with the same cursor, **then** results are identical
  (deterministic ordering).
- [ ] **Given** a query against a `DeniedTables` entry, **when** validated,
  **then** it is rejected before execution.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `-wal` present but stale | DB closed cleanly, leftover wal | ro open succeeds; copy path still works |
| DB > size cap | 600 MB file | `Error` status with reason; no open |
| Permission denied | `chmod 000` file | `Error` status; other sources unaffected |
| Corrupt header | text file renamed `.db` | `Error` status; no crash |
| Glob matches 200 files | antigravity `conversations/*.db` | per-file budget applies; aggregate capped |

## 7. Task Plan

- [ ] **T1 — Registry + contracts:** `CliDbSource`, `CliDatabaseMap` (v1
  inventory), `CliDbSourceStatus`, DTO records, `ICliDatabaseLocator` /
  `ICliDatabaseReader` / `ICliDbExtractor`. Unit tests for map completeness
  vs `AgentCliMap`.
- [ ] **T2 — Locator + reader:** `~` expansion, glob resolution, ro open,
  WAL-copy fallback, temp cleanup, budgets. Unit tests with fixture DBs
  (including a WAL fixture with a live writer connection).
- [ ] **T3 — Fingerprint + drift:** fingerprint computation, comparison,
  drift reporting. Tests: drift on missing column/table, stable on equal
  schema.
- [ ] **T4 — Extractors v1:** Codex, OpenCode, Devin, Antigravity, Cline
  (+ Claude context-mode experimental). Tests per extractor against fixture
  DBs replicating the verified schemas (anonymized).
- [ ] **T5 — Validation:** `dotnet build` (warnings=errors), `dotnet test`,
  coverage ≥ current gate, docs en/pt-br.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; use
  `feature/devin-20260919-cli-db-reader`.
- **Workflows:** do not modify `.github/workflows/**`.
- **Security:** external DBs are untrusted input — whitelist-only queries, no
  string interpolation of paths/table names, parameterized queries, denied
  tables enforced pre-execution, secret-named columns excluded. Never log row
  content; log schema metadata only.
- **Safety:** strictly read-only — no write, VACUUM, checkpoint, or
  journal-creating operation on a source file. Temp copies deleted on all
  paths.
- **Architecture:** N-Layer ABP — contracts in `Application.Contracts`, SQLite
  specifics only in `Integrations`, registry in `Domain.Shared`. `cli-metrics`
  must not bypass this layer.
- **Specs:** any new source/extractor must update `CliDatabaseMap` and the
  inventory table in `CAPABILITY-MAP-cli-metrics.md`.

## 9. Definition of Done

- [ ] RF-001…RF-006 implemented; ACs covered by tests.
- [ ] `dotnet build` clean (`TreatWarningsAsErrors`); `dotnet test` green;
  coverage ≥ gate.
- [ ] Fixture DBs contain no real user data; no secrets in tests.
- [ ] `Status = Done` + PR open.

## Open Questions / Pending Ambiguity

- Token columns per vendor schema are best-effort in v1 — extractors map known
  column names and report `null` when absent; exact mapping validated against
  live DBs during T4 (recorded in the extractor's fingerprint).
