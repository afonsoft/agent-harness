# SPEC-20261003-a11y-baseline: accessibility baseline across all screens

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Accessibility Baseline |
| Product / System | agent-harness |
| Module / Bounded Context | Blazor (UI) |
| Change type | Bugfix / Frontend |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-a11y-baseline` |
| Technical owner | afonsoft |
| Status | Draft |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20260930-mobile-responsive-ui, SPEC-20261003-i18n-consistency |
| Gap keys | GAP-impl-topbar-page-titles, GAP-a11y-skip-link, GAP-a11y-focus-ring-chat, GAP-a11y-keyboard-card, GAP-a11y-aria-coverage, GAP-theme-meta |

---

## 1. Executive Summary

### Problem

Gap-analysis-20261003 found accessibility regressions concentrated on
navigation chrome and the less-visited pages:

- The topbar shows **"Board" on /finops, /cockpit, /jobs and /specs**
  because `PageTitles` lacks those routes (`MainLayout.razor:66-77`).
- No **skip-to-content link** — keyboard/mobile users must tab through
  the offcanvas on every page.
- `.ai-chat-select:focus` sets `outline: none; box-shadow: none`
  (`site.css:2165`) — the chat selects lose their focus indicator.
- `Workflow.razor:90` renders a `div[role=button] @onclick` with no
  keyboard handler or `tabindex` — unreachable by keyboard.
- Twelve pages/dialogs have **zero ARIA** attributes (Agents, FinOps,
  Login, Prompts, Skills, Specs, CockpitRun, AgentInstallDialog,
  AgentModelConfigDialog, RunAgentDialog, VscodeEditor, ProjectsRedirect);
  e.g. the FinOps period toggle `24h/7 days/30 days/All` has no
  `aria-pressed`/group semantics.
- `index.html` has no `theme-color` meta nor `color-scheme` — mobile
  chrome and native form controls do not match app theming.

### Objective

Bring every screen to the baseline defined by
`web-design-guidelines` (icon buttons labelled, keyboard-reachable
interactives, visible focus, labelled dialogs), and fix the topbar
page-title map.

## 2. Scope

**In scope:**
- Add missing `PageTitles` entries: `finops`, `cockpit`, `jobs`, `specs`.
- Skip-to-content link in `MainLayout.razor` targeting `<main>` (styled `.visually-hidden-focusable`, localized label).
- Restore a visible `:focus-visible` indicator on `.ai-chat-select` and audit `.ai-chat-input:focus` for the same regression.
- `Workflow.razor` card headers: convert to real `<button>` (or add `tabindex="0"` + `@onkeydown` Enter/Space), following the `Skills.razor:55` pattern.
- ARIA pass on the 12 pages/dialogs above: `role="dialog"` + `aria-labelledby`/`aria-modal` on dialogs, `aria-pressed` (or `role="group"` + `aria-current`) on toggle groups like FinOps period buttons and Specs status filters, `aria-label` on icon-only buttons.
- `<meta name="theme-color">` + `color-scheme` on `index.html` matching the app background.
- Regression tests where the repo already tests components (bUnit or markup assertions if present); otherwise manual checklist per AC.

**Out of scope:**
- Full localization/i18n — covered by SPEC-20261003-i18n-consistency (`lang` attribute lives there).
- Lighthouse score runs (no public URL).
- Visual redesign of any screen.

## 3. Technical Context

Layout shell: `src/Taskboard.Blazor/Layout/MainLayout.razor` (offcanvas-lg
sidebar, topbar `_pageTitle`, Toasts, ErrorBoundary) and `NavMenu.razor`.
Page components under `src/Taskboard.Blazor/Components/Pages/` and
dialogs under `Components/` subfolders. Shared CSS in
`src/Taskboard.Client/wwwroot/css/site.css`.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Layout/MainLayout.razor` (`PageTitles` map lines 66-77)
- `src/Taskboard.Blazor/Components/Pages/Workflow.razor` (line ~90 card header)
- `src/Taskboard.Blazor/Components/Pages/FinOps.razor` (period toggle)
- `src/Taskboard.Blazor/Components/Pages/Specs.razor` (status filter)
- `src/Taskboard.Blazor/Components/Pages/{Login,Agents,Prompts,Skills,CockpitRun}.razor`
- `src/Taskboard.Blazor/Components/Pages/{AgentInstallDialog,AgentModelConfigDialog,RunAgentDialog}.razor`
- `src/Taskboard.Client/wwwroot/css/site.css` (focus rules ~2116-2170)
- `src/Taskboard.Client/wwwroot/index.html`

**Files to create or modify:**
```text
src/Taskboard.Blazor/Layout/MainLayout.razor
src/Taskboard.Blazor/Components/Pages/Workflow.razor
src/Taskboard.Blazor/Components/Pages/FinOps.razor
src/Taskboard.Blazor/Components/Pages/Specs.razor
src/Taskboard.Blazor/Components/Pages/Login.razor
src/Taskboard.Blazor/Components/Pages/Agents.razor
src/Taskboard.Blazor/Components/Pages/Prompts.razor
src/Taskboard.Blazor/Components/Pages/Skills.razor
src/Taskboard.Blazor/Components/Pages/CockpitRun.razor
src/Taskboard.Blazor/Components/Pages/AgentInstallDialog.razor
src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor
src/Taskboard.Blazor/Components/Pages/RunAgentDialog.razor
src/Taskboard.Blazor/Components/Pages/VscodeEditor.razor
src/Taskboard.Client/wwwroot/css/site.css
src/Taskboard.Client/wwwroot/index.html
```

## 4. Requirements

### RF-001: topbar page titles
- **Description:** `PageTitles` must map every `@page` route to a human title; unknown routes may keep the `Board` fallback.
- **Rules:** add `finops`→FinOps, `cockpit`→Cockpit, `jobs`→Jobs, `specs`→Specs; keep prefix matching.
- **Input → Output:** navigate `/finops` → topbar shows "FinOps".

### RF-002: skip link
- **Description:** first focusable element on every page is a skip link that moves focus to `<main>`.
- **Rules:** visible only on focus; works with offcanvas open or closed.

### RF-003: focus indicators
- **Description:** no interactive element may have `outline: none` (or `outline: 0`) without a `:focus-visible` replacement.
- **Rules:** fix `.ai-chat-select:focus`; audit all 3 `outline: none` occurrences in site.css.

### RF-004: keyboard-operable cards/headers
- **Description:** every `@onclick` region acting as a control must be a `<button>` or carry `tabindex="0"` + Enter/Space handling.
- **Rules:** Workflow card headers fixed; scrims/overlays used only for click-outside dismissal are exempt (`role="presentation"`).

### RF-005: dialog and toggle semantics
- **Description:** dialogs expose `role="dialog"`, `aria-modal="true"`, `aria-labelledby`; toggle groups expose pressed/current state.
- **Rules:** applies to the 12 audited files; icon-only buttons get `aria-label`.

### RF-006: theme metadata
- **Description:** `index.html` declares `theme-color` (app background) and `color-scheme` matching supported themes.

**Business rules / invariants:**
- No visual regression on desktop; changes are semantics/CSS only.
- Follow Blazor.Bootstrap conventions where a component already provides ARIA (prefer its parameters over hand-written attributes).

## 5. API Contract

N/A — UI-only change.

## 6. Acceptance Criteria

- [ ] **Given** the app on `/finops` **when** the page renders **then** the topbar shows "FinOps" (same for `/cockpit`, `/jobs`, `/specs`).
- [ ] **Given** keyboard-only navigation **when** pressing Tab once on any page **then** a skip link appears and focuses `<main>` on activation.
- [ ] **Given** the AI Code page **when** tabbing to a mode/select control **then** a visible focus ring is rendered.
- [ ] **Given** the Workflow page **when** a collapsed workflow card header receives focus and Enter is pressed **then** it toggles.
- [ ] **Given** any modal dialog (install agent, run agent, move issue) **when** inspected **then** it exposes `role="dialog"`/`aria-modal`/`aria-labelledby`.
- [ ] **Given** the FinOps period buttons **when** a period is active **then** it is exposed via `aria-pressed`/`aria-current`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Unknown route | `/nonexistent` | Topbar fallback still "Board" (or NotFound page) |
| Mobile offcanvas | hamburger → nav | Focus order remains sane; skip link still first |

## 7. Task Plan

- [ ] **T1 — Discovery:** read files in section 3; inventory remaining `role="button"`/`@onclick` non-button regions.
- [ ] **T2 — Implementation:** topbar map, skip link, focus rules, keyboard handlers, ARIA sweep, theme meta.
- [ ] **T3 — Verification:** keyboard walkthrough of every page; verify each AC.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet format --verify-no-changes`; UI smoke at ~330px and desktop widths.
- [ ] **T5 — Done + PR:** set `Status = Done`, open PR.

## 8. Organization Guardrails

- Branch `feat/devin-20261003-a11y-baseline`; never push to `main`/`develop`.
- No `.github/workflows/**` changes.
- Tests accompany behavior changes where a test harness exists.
- `dotnet build` with `TreatWarningsAsErrors`.

## 9. Definition of Done

- [ ] All RF-001..RF-006 implemented.
- [ ] All acceptance criteria verified.
- [ ] Build + format green; Docker `test` stage unaffected.
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- Exact label texts for pt-BR vs en (resolves with i18n SPEC decision; use existing copy language per page until then).
