# SPEC-20260914-stale-spec-status

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `stale-spec-status` |
| Type | `Docs` |
| Stack | `Docs` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/{AgentLLM}-20260914-stale-spec-status` |
| Ticket | `GAP-documentation-stale-specs` |
| Status | `Done` |

## 1. User Story

**As a** maintainer of taskboard-ai
**I want** SPEC statuses and acceptance criteria to reflect reality
**So that** future gap-analysis and orchestrator runs don't audit against dead contracts.

**Problem context:**
The 2026-09-14 gap-analysis found four divergences where docs trail merged code:

| SPEC | Divergence |
| --- | --- |
| `SPEC-20260913-board-repo-selector` | Merged via commit `c6dc888` / PR #51; status still `Approved`. |
| `SPEC-20260911-tailwind-theme-refresh` | Superseded by `SPEC-20260913-bootstrap-modernization` (self-declared); status still `Approved`. |
| `SPEC-20260910-frontend-performance` + `SPEC-20260910-ui-login-settings-skills` | Acceptance criteria still require MudBlazor components/theme — MudBlazor was fully removed by bootstrap-modernization. Behavior is implemented via Blazor.Bootstrap equivalents. |
| `SPEC-20260911-devin-antigravity-harness` | AC requires `.devin/skills/` + `.agent/skills/` directories; both were removed by consolidation commit `62dbf81` — `.devin/config.json` now points to `.claude/skills`. |

## 2. Scope

**In scope:**
- Update `Status` fields: board-repo-selector → `Done`; tailwind-theme-refresh → `Deprecated (superseded by SPEC-20260913-bootstrap-modernization)`.
- Rewrite MudBlazor-specific ACs in the two 2026-09-10 specs to the Blazor.Bootstrap/Bootstrap 5.3 equivalents.
- Update devin-antigravity-harness AC to match the consolidated `.claude/skills` layout + `.devin/config.json`.
- Add a `Superseded by` note where applicable.

**Out of scope:**
- Changing any code.
- Creating new SPECs.

## 3. Technical Context

**Files to modify:**
```text
.specs/SPEC-20260913-board-repo-selector.md
.specs/SPEC-20260911-tailwind-theme-refresh.md
.specs/SPEC-20260910-frontend-performance.md
.specs/SPEC-20260910-ui-login-settings-skills.md
.specs/SPEC-20260911-devin-antigravity-harness.md
```

## 4. Requirements

### RF-001: Statuses reflect merge state
- **Description:** Every implemented spec is `Done`/`Completed`; superseded specs are explicitly marked.
- **Input → Output:** `grep Status .specs/*.md` → no `Approved` status on merged/superseded work.

### RF-002: No dead-framework ACs
- **Description:** No acceptance criterion may reference a dependency that no longer exists (MudBlazor, Tailwind).
- **Input → Output:** `grep -i mudblazor\|tailwind .specs/SPEC-*.md` → only in historical/superseded context.

## 6. Acceptance Criteria

- [ ] **Given** the five specs listed in scope **when** a reader checks `Status` **then** it matches the git reality (merged → `Done`; superseded → `Deprecated` with pointer).
- [ ] **Given** `SPEC-20260911-devin-antigravity-harness` **when** its ACs are read **then** they describe `.devin/config.json` + `.claude/skills` consolidation.
- [ ] `markdownlint`/docs review passes; no code touched.

## 7. Task Plan

- [ ] **T1 — Discovery:** re-read each spec's AC section.
- [ ] **T2 — Implementation:** edit statuses and stale ACs.
- [ ] **T3 — Verification:** `grep` checks per RF-001/RF-002.
- [ ] **T4 — Done + PR.**

## 9. Definition of Done

- [ ] No spec claims `Approved` for merged or superseded work.
- [ ] No AC references removed dependencies.
