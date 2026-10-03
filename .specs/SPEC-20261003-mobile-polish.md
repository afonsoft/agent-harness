# SPEC-20261003-mobile-polish: residual mobile rendering fixes

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Mobile Polish Residual |
| Product / System | agent-harness |
| Module / Bounded Context | Blazor (UI) |
| Change type | Bugfix / Frontend |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-mobile-polish` |
| Technical owner | afonsoft |
| Status | Draft |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20260930-mobile-responsive-ui (Done via #424), SPEC-20261003-a11y-baseline |
| Gap keys | GAP-impl-mobile-board-overflow |

---

## 1. Executive Summary

### Problem

The mobile shell works (offcanvas-lg + hamburger verified live at
~330px), but a live sweep found residual rendering issues:

- **Board page:** the "Priority Level" legend/filter row overflows
  horizontally offscreen at ~330px.
- Long content on dense pages (Settings config table, FinOps source
  health table, Specs list) depends on per-page overflow handling —
  the config table scrolls inside its container, but the Board
  legend proves some rows were not covered.

### Objective

Close residual horizontal-overflow issues at phone widths
(320–414px) on every screen, building on
SPEC-20260930-mobile-responsive-ui.

## 2. Scope

**In scope:**
- Board priority-legend row: wrap or horizontal-scroll within the viewport at ≤576px.
- Sweep every page at ~320-360px for `offscreen` horizontal overflow: Board, FinOps, Jobs, Specs, Skills, Settings (all tabs), Workflow, Cockpit, CockpitRun, Agents, Prompts, Gantt, Terminal, AiChat, VscodeEditor.
- Ensure every `<table>` lives in `.table-responsive` (or equivalent) — FinOps "Source health" and Settings config table already do; verify the rest.

**Out of scope:**
- a11y semantics — SPEC-20261003-a11y-baseline.
- Touch-gesture redesign; virtual keybar already exists.
- Tablet-specific layouts (>576px fine already).

## 3. Technical Context

Verified environment: server on localhost:47823, Chrome shrunk to
~330px CSS width. `site.css` already contains the mobile media-query
blocks added by SPEC-20260930-mobile-responsive-ui.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` (priority legend ~line 38, 95)
- `src/Taskboard.Blazor/Components/Pages/{Board? → whichever hosts KanbanBoard,FinOps,Jobs,Specs}.razor`
- `src/Taskboard.Client/wwwroot/css/site.css` (mobile sections)

**Files to create or modify:**
```text
src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor
src/Taskboard.Client/wwwroot/css/site.css
(any page found overflowing during the sweep)
```

## 4. Requirements

### RF-001: no horizontal page overflow ≤ 414px
- **Description:** no page scrolls horizontally at 320–414px except inside an intentional scroll container (`.table-responsive`, terminal, code blocks).
- **Rules:** legend/filter rows wrap (`flex-wrap`) or become scrollable strips.

### RF-002: tables responsive
- **Description:** every data table is inside a horizontal-scroll wrapper at small widths.
- **Rules:** audit all `<table>` in `Components/`.

### RF-003: live sweep evidence
- **Description:** implementer must visually check every route at ~330px and record pass/fail per page in the PR.

## 5. API Contract

N/A.

## 6. Acceptance Criteria

- [ ] **Given** viewport 330px **when** Board renders **then** the priority legend is fully reachable (wraps or scrolls within the viewport).
- [ ] **Given** 330px **when** each route renders **then** `document.documentElement.scrollWidth <= viewport width` (no page-level horizontal scroll).
- [ ] **Given** a table wider than the viewport **when** rendered **then** it scrolls inside `.table-responsive`, not the page.

## 7. Task Plan

- [ ] **T1 — Discovery:** run the live sweep, list every overflowing element.
- [ ] **T2 — Implementation:** CSS/markup fixes.
- [ ] **T3 — Verification:** re-sweep, record per-page results.
- [ ] **T4 — Validation:** build + format.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

Standard: feature branch, no workflow edits, warnings as errors.

## 9. Definition of Done

- [ ] RF-001..RF-003 with per-page sweep evidence in the PR.

## Open Questions / Pending Ambiguity

- None.
