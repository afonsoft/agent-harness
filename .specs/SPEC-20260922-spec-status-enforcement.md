# SPEC-20260922-spec-status-enforcement

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `spec-status-enforcement` |
| Type | `Infra` (process automation + housekeeping) |
| Stack | `.NET 10 / shell / GitHub Actions (aditivo) / Markdown` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-spec-status-enforcement` |
| Ticket | [#319](https://github.com/afonsoft/agent-harness/issues/319) — Epic [#318](https://github.com/afonsoft/agent-harness/issues/318) |
| Status | `Done` |
| Gap | `GAP-automation-spec-status-enforcement` (3ª recorrência do drift de status de SPECs) |

## 1. User Story

**As a** Harness maintainer
**I want** mechanical enforcement that a delivered SPEC gets `Status: Done` in
the same PR that ships it — plus a one-time correction of the statuses that
went stale on 2026-09-22
**So that** `.specs/` stays trustworthy as the source of truth and
`gap-analysis` runs stop rediscovering the same drift every cycle.

**Problem context:**

- Convention exists: `.claude/rules/global-rules.md:28` — "PR que entrega uma
  SPEC marca `Status: Done`". It is a **soft rule with no enforcement**.
- Evidence of failure (this audit, 2026-09-22): `SPEC-20260922-flaky-integration-tests`,
  `SPEC-20260922-coverage-ratchet-bump-77`, `SPEC-20260922-harness-home-rename`
  all declare `Status: Approved` while their PRs (#311, #312, #313) are merged
  and the code is in `main` (`COVERAGE_THRESHOLD: 77` in `dotnet.yml:44`;
  `HARNESS_*` binding in `Program.cs:91`).
- This is the **third recurrence**: `SPEC-20260914-stale-spec-status` and
  `SPEC-20260919-stale-spec-status` already corrected prior drift and
  registered the convention — documentation alone is not holding.

## 2. Scope

**In scope:**

- Script `scripts/check-spec-status.sh` (or equivalent dev-time check) that
  scans `.specs/SPEC-*.md` and fails when a spec referenced by a merged PR —
  or declared `Approved` while its ticket Issue is closed — still reads
  `Approved`/`Draft`. Implemented as a repo-local check runnable by agents
  and humans (`shellcheck`-clean).
- Wire the check where it can run without touching `.github/workflows/**`:
  the `orchestrator`/`gap-analysis` flow already lists "verify spec status"
  steps — the script gives them a deterministic tool. If a CI wiring is
  wanted later, it requires human approval per Hard Rules (documented here,
  not done).
- Housekeeping: set `Status: Done` (with PR reference) on the 3 stale specs
  listed above.
- `SPEC-20260922-finops-dashboard-detail` keeps `Approved` — genuinely not
  implemented (Issue #316 open); the script must not flag it (its Issue is
  open and no delivery PR references it).

**Out of scope:**

- Editing `.github/workflows/**` (protected — needs human approval; record
  as recommendation only).
- Changing spec content beyond the `Status` line + PR reference.
- Retro-scanning closed specs for historical drift beyond the 3 listed.

## 3. Technical Context

**Where the change happens:**

- `scripts/check-spec-status.sh` [new] — repo-local check.
- `.specs/SPEC-20260922-{flaky-integration-tests,coverage-ratchet-bump-77,harness-home-rename}.md` — status bump.
- `.claude/rules/global-rules.md` — reference the script next to the
  existing convention (line 28).

**Detection rule (script contract):**

1. For each `.specs/SPEC-*.md` with `Status` in `{Draft, Approved,
   In implementation}`: extract the `Ticket`/`Refs` issue number when present.
2. `gh issue view <n> --json state,closed` — Issue `CLOSED` ⇒ flag stale.
3. Additionally flag when `git log --grep="SPEC-<id>\|#<issue>"` on `main`
   shows a merged delivery PR mentioning the spec id and the status is still
   non-terminal.
4. Exit non-zero with the stale list when any flag fires; `--fix` bumps the
   status line to `Done` + appends the merged PR reference.

**Files to read before implementing:**

- `.claude/rules/global-rules.md` (line 28 — existing convention)
- `.specs/SPEC-20260919-stale-spec-status.md` (RF-004 — prior convention spec)
- `AGENTS.md` hard rules (workflows protected)
- `.specs/` metadata format variants (`| Status | \`X\``, `- **Status**: X`)

## 4. Requirements

### RF-001: Status-drift detector

- **Description:** `scripts/check-spec-status.sh` lists every spec whose
  declared status is non-terminal while evidence shows delivery (closed
  ticket Issue or merged PR referencing the spec id).
- **Rules:** handles both metadata formats (`| Status | \`X\`` and
  `- **Status**: X`); no `gh` available → degrade to git-log-only detection
  with a warning; exit code 0 = clean, 1 = drift found (prints list),
  2 = environment error.
- **Input → Output:** repo scan → stale-spec list on stdout.

### RF-002: `--fix` mode

- **Description:** `--fix` rewrites the stale `Status` line to `Done` and
  appends the merged PR reference (`— delivered in PR #NNN`) when known.
- **Rules:** only touches the Status line; `sed`-safe against the two
  formats; prints each file changed.

### RF-003: Housekeeping — bump the 3 stale specs

- **Description:** mark `SPEC-20260922-flaky-integration-tests`,
  `SPEC-20260922-coverage-ratchet-bump-77`,
  `SPEC-20260922-harness-home-rename` as `Done` with PR refs #311/#312/#313.
- **Rules:** do not bump `SPEC-20260922-finops-dashboard-detail` (Issue #316
  open, no delivery PR).

### RF-004: Convention wiring

- **Description:** reference the script in `.claude/rules/global-rules.md`
  next to the lifecycle rule so every agent loop runs it pre-PR.
- **Rules:** documentation change only; no workflow edits in this spec.

## 5. API Contract

N/A — local tooling + doc changes.

## 6. Acceptance Criteria

- [ ] **Given** the repo state today, **when** `check-spec-status.sh` runs,
  **then** it lists exactly the 3 stale specs and exits 1.
- [ ] **Given** `--fix`, **when** run, **then** the 3 specs read
  `Status: Done` with their PR references and a re-run exits 0.
- [ ] **Given** `SPEC-20260922-finops-dashboard-detail` (Approved + open
  Issue #316), **when** the script runs, **then** it is NOT flagged.
- [ ] **Given** a spec with `- **Status**: Done` format, **when** parsed,
  **then** it is recognized (no false positive).
- [ ] **Given** `gh` absent from PATH, **when** the script runs, **then** it
  warns and uses git-log detection only.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Spec with no Ticket | `Approved`, no issue ref | git-log detection only |
| Closed Issue unrelated to delivery | Issue closed as won't-do | flagged for human review (script lists, `--fix` still applies status only after PR evidence) |
| Malformed status line | missing Status row | reported as `unparseable`, not flagged stale |

## 7. Task Plan

- [x] **T1 — Script:** `scripts/check-spec-status.sh` + shellcheck clean +
  fixture tests (temp dir with sample spec files).
- [x] **T2 — Housekeeping:** bump the 3 stale specs with PR refs.
- [x] **T3 — Convention:** update `.claude/rules/global-rules.md` line 28 to
  reference the script.
- [x] **T4 — Validation:** run script clean; `dotnet build`/`test`
  unaffected (no code change); docs en/pt-br if applicable.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260922-spec-status-enforcement`; never
  `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/**` untouched — CI wiring is a
  documented recommendation, not part of this spec.
- **Scope:** status line + rule reference only; no spec content rewrites.
- **Determinism:** script is read-only by default; mutations only via
  explicit `--fix`.

## 9. Definition of Done

- [ ] RF-001…RF-004 implemented; ACs covered.
- [ ] Script exits 0 on clean tree, 1 on drift; shellcheck clean.
- [ ] The 3 stale specs bumped; finops spec untouched.
- [ ] `Status = Done` + PR open.
