# SPEC-20261003-sqlite-backup: backup/restore story for the data volume

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | SQLite Backup & Restore |
| Product / System | agent-harness |
| Module / Bounded Context | CLI (`taskctl`) + Docs + Compose |
| Change type | Feature / Infra |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-sqlite-backup` |
| Technical owner | afonsoft |
| Status | Draft |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | — |
| Gap keys | GAP-ops-sqlite-backup |

---

## 1. Executive Summary

### Problem

Everything durable lives in the `/data` volume: `harness.sqlite`
(board, runs, chat threads, settings overrides, FinOps telemetry) and
CLI credentials under `/data/home`. There is **no backup story**: no
`backup`/`restore` command in `taskctl`, no scheduled copy, nothing in
`docs/installation.md`. A corrupt DB or lost volume loses the whole
workbench state.

### Objective

Add `taskctl backup` (safe online SQLite backup via `VACUUM INTO` /
the SQLite backup API) and document a restore path + a cron/compose
example for scheduled copies.

## 2. Scope

**In scope:**
- `taskctl backup <dest-dir>` — consistent copy of `harness.sqlite` to `dest-dir/harness-backup-{utc}.sqlite` (or `.db`), safe while the server runs (SQLite backup API / `VACUUM INTO`).
- `taskctl restore <file>` guidance command or documented `cp` + restart flow (server must be stopped or handle cleanly — decide the simplest safe path).
- `docs/installation.md` (+ pt-br) section: backup, restore, and a scheduled example (`docker compose exec harness taskctl backup /data/backups` via host cron, or a sidecar).
- Retention flag `--keep N` (default 7) pruning old backups in the dest dir.

**Out of scope:**
- Backing up CLI credentials (`/data/home`) — document that a full-volume tarball covers them; tool focuses on the DB.
- Cloud/object storage upload.
- Encryption.

## 3. Technical Context

CLI: `src/Taskboard.Cli` (Spectre.Console.Cli); DB path resolution via
`CliConfigService`/`HARNESS_DATA_DIR`. Server uses EF Core `MigrateAsync`
at boot on `harness.sqlite`. `Microsoft.Data.Sqlite` is already a
dependency (EF Core SQLite provider).

**Files to read before implementing:**
- `src/Taskboard.Cli/Commands/` (command structure conventions)
- `src/Taskboard.Cli/Services/CliConfigService.cs` (data-dir resolution)
- `docs/installation.md`, `docs/installation.pt-br.md`
- `docker-compose.yml`

**Files to create or modify:**
```text
src/Taskboard.Cli/Commands/BackupCommand.cs          (new)
src/Taskboard.Cli/Commands/RestoreCommand.cs         (new or doc-only)
src/Taskboard.Cli/Program.cs                         (register)
tests/Taskboard.Tests.Unit/Cli/BackupCommandTests.cs (new)
docs/installation.md
docs/installation.pt-br.md
docker-compose.yml                                 (optional commented cron example)
```

## 4. Requirements

### RF-001: online consistent backup
- **Description:** `taskctl backup <dir>` produces a consistent DB copy while the server is running, using SQLite `VACUUM INTO` (single-transaction copy) or `SqliteConnection.BackupDatabase`.
- **Rules:** refuse if source DB missing; create dest dir; name `harness-backup-{yyyyMMdd-HHmmss}.sqlite`; return path on stdout.
- **Input → Output:** `taskctl backup /data/backups` → prints created file path, exit 0.

### RF-002: retention
- **Description:** `--keep N` (default 7) deletes oldest backups beyond N in the dest dir.
- **Rules:** only files matching the backup pattern are pruned.

### RF-003: restore path
- **Description:** documented restore: stop harness → replace `harness.sqlite` → start; optionally `taskctl restore <file>` performing exactly that with a confirmation flag.
- **Rules:** never overwrite a live DB without `--force`; verify file is a SQLite DB (header check) before replacing.

### RF-004: docs
- **Description:** backup/restore/cron sections in both installation docs (en + pt-br).

**Business rules / invariants:**
- Backup never blocks writers beyond SQLite's normal semantics (VACUUM INTO reads a consistent snapshot).
- No credentials/PII logged; output shows only paths and sizes.

## 5. API Contract

N/A — CLI surface. Suggest optionally a thin `POST /api/admin/backup` later; not required now.

## 6. Acceptance Criteria

- [ ] **Given** a running server **when** `taskctl backup /tmp/bk` runs **then** a valid sqlite file appears and opens with `sqlite3` integrity_check ok.
- [ ] **Given** 8 backups and `--keep 7` **when** backup runs **then** the oldest is removed.
- [ ] **Given** a corrupt file as restore input **when** `taskctl restore` runs **then** it refuses (non-sqlite header).
- [ ] **Given** the docs **when** followed **then** backup+restore round-trips on a fresh container.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Missing DB | fresh install no DB yet | clear error, exit ≠ 0 |
| Dest not writable | read-only dir | clear error, no partial file |
| Same-day multiple runs | repeated | distinct timestamps; keep-pruning still honored |

## 7. Task Plan

- [ ] **T1 — Discovery:** read CLI conventions, DB path resolution.
- [ ] **T2 — Implementation:** BackupCommand (+ RestoreCommand or doc path), retention, header check.
- [ ] **T3 — Verification:** unit tests (fake/tmp DB), live round-trip in compose.
- [ ] **T4 — Validation:** build/test/format; docs both languages.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- BDD test names in Portuguese; feature branch; `TreatWarningsAsErrors`.
- Never log connection strings or file contents.

## 9. Definition of Done

- [ ] RF-001..RF-004; ACs green; docs updated (en + pt-br).

## Open Questions / Pending Ambiguity

- `taskctl restore` as a command vs documented cp+restart — recommended: implement the command (cheap) AND document both.
