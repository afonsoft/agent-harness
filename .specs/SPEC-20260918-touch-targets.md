# SPEC-20260918 — Touch Targets: alvos de toque ≥44px em viewports móveis

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `touch-targets` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly / Bootstrap 5` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260918-touch-targets` |
| Ticket | `GAP-implementation-touch-targets-touch-viewports` (gap-analysis-20260918) |
| Status | `Approved` — aprovada pelo usuário (2026-09-18) |

Origin: gap-analysis-20260918 — `SPEC-20260918-sidebar-icon-rail` scope item
"interactive targets ≥44px on touch viewports" (e AC-5) não foi implementado:
os botões de ação `btn-sm` (~31px) em `/settings` e `/agents` permanecem
abaixo do mínimo de toque em 375px.

## 1. User Story

**As a** Harness user on a phone or tablet,
**I want** action buttons in Settings, Agents and dialog rows to be at least
44px tall on touch viewports,
**so that** taps land reliably without hitting the wrong control.

## 2. Scope

### In scope

- CSS-only fix preferred: a `@media (hover: none) and (pointer: coarse)` or
  `max-width` rule that raises interactive targets (`.btn-sm` inside action
  groups, icon buttons, table action cells) to `min-height: 44px` /
  `min-width: 44px` on touch/small viewports.
- Cover the concrete rows flagged by the audit: `Settings.razor` config-table
  action cells (`d-inline-flex gap-1` + `btn-sm`, lines ~370-381) and Agents
  action rows.
- Keep desktop appearance unchanged (rule gated to coarse pointer / small
  viewport).

### Out of scope

- Redesign of layouts or new components.
- Changing the `gap-1` dense-cell convention (see INCONCLUSIVO item in the
  gap report — decided separately).
- `.github/workflows/**`.

## 3. Technical Context

- `src/Taskboard.Client/wwwroot/css/site.css` — existing precedent:
  `.task-card-menu` (`min-width/height: 44px`, ~line 1033), `.filter-chip`
  (`min-height: 44px`, ~line 1101), `.agent-toggle` (partial, ~line 1095).
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — `btn-sm` buttons
  in `d-inline-flex gap-1` cells (lines ~370-381) and standalone `btn-sm`
  Refresh buttons (lines ~137, ~239).
- `src/Taskboard.Blazor/Components/Pages/Agents.razor` — action rows with
  `d-inline-flex gap-1` + `btn-sm`.
- Bootstrap 5: `btn-sm` computed height ≈ 31px; coarse-pointer media query
  is the standard gate for touch-target rules.

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | On coarse-pointer (touch) or ≤767.98px viewports, interactive elements inside action groups (`.btn-sm` in `d-inline-flex`, icon buttons, table action cells) render with `min-height` and `min-width` ≥44px. |
| RF-002 | Desktop (fine pointer) rendering is unchanged — no visual difference at ≥992px with mouse. |
| RF-003 | The rule lives in `site.css` alongside the existing "Touch targets + mobile layout" section and reuses the established pattern. |
| RF-004 | `prefers-reduced-motion` and existing reduced-motion rules are unaffected. |

## 5. Acceptance Criteria

- **AC-1** *Given* a 375px touch viewport on `/settings`, *when* the config
  table renders, *then* each action button's hit area is ≥44×44px.
- **AC-2** *Given* a 375px touch viewport on `/agents`, *when* action rows
  render, *then* sibling buttons have ≥44px touch targets.
- **AC-3** *Given* a ≥992px desktop viewport with mouse, *when* any affected
  page renders, *then* button sizes are visually unchanged.
- **AC-4** *Given* the stylesheet, *when* inspected, *then* the rule sits in
  the "Touch targets + mobile layout" section and is gated to coarse pointer
  or small viewport.

## 6. DoD

- [ ] RF-001..004 implemented.
- [ ] `dotnet build` clean; site.css served in production contains the rule.
- [ ] Visual check at 375px: no layout breakage, no overlapping targets.
- [ ] SPEC → `Status: Done`; PR merged; `taskboard-server` redeployed.
