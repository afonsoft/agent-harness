# SPEC-20261003-perf-pass: WASM bundle evaluation + query hot spots

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Performance Pass |
| Product / System | agent-harness |
| Module / Bounded Context | Blazor client + EF Core |
| Change type | Refactor / Infra |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-perf-pass` |
| Technical owner | afonsoft |
| Status | Done |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20261003-ops-hardening |
| Gap keys | GAP-perf-wasm-bundle, GAP-perf-getlatest-perissue |

---

## 1. Executive Summary

### Problem

Gap-analysis-20261003 measured:

- **WASM bundle:** publish `wwwroot` 33MB, `_framework` 47MB on disk
  (with .br/.gz variants; ~1.2MB br for the runtime wasm). No
  `RunAOTCompilation`/trimming-related props anywhere. Compression +
  fingerprinted immutable caching + segmented ICU already in place —
  the remaining lever is AOT and/or documenting a bundle budget.
- **`GetLatestPerIssueAsync`** (`EfCoreAgentRunRepository.cs:62-76`):
  `GroupBy(IssueId).Select(g => OrderByDescending.Select(Id).First())`
  then `Contains` — 2 queries with a fragile translation pattern;
  EF Core group-`First` translation is provider-dependent and can
  silently become a full scan.
- **`GetStaleActiveRunsAsync`** loads all queued/running rows then
  filters the cutoff in memory (documented as deliberate for small
  sets — verify the assumption holds or bound it).
- Jobs page re-renders + `Reverse()`es the whole log every 10s poll —
  bounded but worth an incremental append instead.

### Objective

Reduce cold-load cost and DB work where measurable: evaluate (and
either apply or document why not) WASM AOT/trim, replace the
latest-per-issue query with an always-translatable MAX+join pattern,
and bound stale-run memory filtering.

## 2. Scope

**In scope:**
- Evaluate `<RunAOTCompilation>` for `Taskboard.Client` publish: build time + size delta + perceived improvement; adopt it OR document the decision in `docs/` (or the csproj comment) if rejected.
- Document a bundle budget (target numbers for `_framework` download size) wherever the evaluation lands.
- Rewrite `GetLatestPerIssueAsync` as `GroupBy → Max(StartedAt)` + join, which translates reliably on SQLite.
- `GetStaleActiveRunsAsync`: compare StartedAt as a translatable form (e.g. ticks string or server-side range) or bound the in-memory set (`Take`) with a comment on expected cardinality.
- Jobs page: append-only log render (or `Virtualize`) instead of full `Reverse()` per poll — small.

**Out of scope:**
- Server-side response caching, CDN work — none needed (self-hosted single user).
- Re-measurement via Lighthouse (no public URL — deferred).
- Any query not listed above.

## 3. Technical Context

EF repos: `src/Taskboard.EntityFrameworkCore/Agents/EfCoreAgentRunRepository.cs`.
Indexes already exist on `AgentRuns.IssueId` (`AgentRunConfiguration.cs:26`).
Client csproj: `src/Taskboard.Client/Taskboard.Client.csproj` (Blazor WASM).
Jobs UI: `src/Taskboard.Blazor/Components/Pages/Jobs.razor` (poll ~10s, `job.Log.AsEnumerable().Reverse()`).

**Files to read before implementing:**
- `src/Taskboard.EntityFrameworkCore/Agents/EfCoreAgentRunRepository.cs`
- `src/Taskboard.EntityFrameworkCore/Agents/AgentRunConfiguration.cs`
- `src/Taskboard.Client/Taskboard.Client.csproj`
- `src/Taskboard.Blazor/Components/Pages/Jobs.razor`

**Files to create or modify:**
```text
src/Taskboard.EntityFrameworkCore/Agents/EfCoreAgentRunRepository.cs
src/Taskboard.Client/Taskboard.Client.csproj     (AOT props if adopted)
docs/technologies.md (+pt-br) or csproj comment  (bundle budget/decision)
src/Taskboard.Blazor/Components/Pages/Jobs.razor (optional incremental log)
tests/Taskboard.Tests.Integration/…              (query behavior regression)
```

## 4. Requirements

### RF-001: AOT evaluation + decision record
- **Description:** publish with `RunAOTCompilation=true`; record size/time deltas; adopt if the win is real for warm pages (Terminal, AI Code), otherwise document rejection.
- **Rules:** decision and numbers committed in docs or csproj comment.

### RF-002: translatable latest-per-issue
- **Description:** rewrite `GetLatestPerIssueAsync` to a `MAX(StartedAt)` + join (or equivalent) that EF Core reliably translates to SQL on SQLite — single round-trip preferred.
- **Rules:** results identical to today; integration test proves SQL translation (or at least identical output on seeded data).

### RF-003: bounded stale-run scan
- **Description:** `GetStaleActiveRunsAsync` filters cutoff in SQL when the representation allows, or caps the scanned set with an explicit bound + comment.
- **Rules:** behavior unchanged for existing tests.

### RF-004: jobs log rendering
- **Description:** avoid reversing the full log on each poll cycle; incremental append or virtualization.
- **Rules:** same UI result.

**Business rules / invariants:**
- No behavior change in repository outputs.
- No new NuGet dependencies without PR justification.

## 5. API Contract

N/A.

## 6. Acceptance Criteria

- [ ] **Given** seeded AgentRuns (2 issues × N runs) **when** `GetLatestPerIssueAsync` runs **then** returns the newest run per issue — same as before, in one SQL round-trip (verify via EF logging or test).
- [ ] **Given** AOT evaluation **when** complete **then** committed doc/comment states adopted-or-rejected with the numbers.
- [ ] **Given** stale runs **when** `GetStaleActiveRunsAsync` runs **then** identical results to the previous implementation on the same dataset.
- [ ] **Given** the Jobs page **when** a poll cycle fires **then** it does not re-enumerate the entire log to render.

## 7. Task Plan

- [ ] **T1 — Discovery:** baseline bundle sizes + EF translation check of current queries.
- [ ] **T2 — Implementation:** repository rewrite, stale-scan bound, Jobs render, AOT eval.
- [ ] **T3 — Verification:** integration tests on seeded data; publish delta recorded.
- [ ] **T4 — Validation:** build/test/format.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

Standard: feature branch, no workflow edits, warnings as errors, tests required.

## 9. Definition of Done

- [ ] RF-001..RF-004; ACs green; decision recorded.

## Open Questions / Pending Ambiguity

- AOT adoption depends on measured build-time cost — the SPEC allows either outcome as long as it's documented.
