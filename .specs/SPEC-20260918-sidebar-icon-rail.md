# SPEC-20260918 — Sidebar Icon Rail: collapsible desktop sidebar, topbar title e padronização de espaçamento de botões

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `sidebar-icon-rail` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly + Blazor.Bootstrap / Bootstrap 5 offcanvas` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260918-sidebar-icon-rail` |
| Ticket | N/A |
| Status | `Approved` — aprovada pelo usuário (2026-09-18) |

Origin: replicate the icon/layout polish implemented in
`afonsoft/LangGraph-UI` (SPEC-20260918-ui-layout-polish, PRs #123 + #124)
on this repository, adapted to its stack (Blazor.Bootstrap `Icon`
components + Bootstrap `offcanvas-lg` instead of a custom drawer, and the
`lg` 992px breakpoint instead of `md` 768px).

## 1. User Story

**As a** Harness user on desktop,
**I want** to collapse the sidebar into an icon-only rail that persists
across sessions, see the current page title in the topbar, and have
consistent spacing between action buttons,
**so that** I get more working area, clearer navigation affordances and a
more polished UI — while the mobile offcanvas drawer keeps working
exactly as today.

Problem context: the sidebar is always 16.25rem on desktop with no way to
reclaim horizontal space; the topbar carries a boilerplate "About" link
and no page context; and while most action groups already use `gap-*`,
the pattern is not enforced everywhere. In LangGraph-UI the same polish
required two extra lessons that are already covered here: nav icons must
not depend on an icon font that is not loaded (the `fa-*` regression —
this repo already renders inline SVGs via Blazor.Bootstrap `Icon`), and
any service-worker-style fetch handler must use a parsed `URL` object
(this repo has no service worker — nothing to port).

## 2. Scope

### In scope

- Desktop sidebar (≥992px, `lg`) collapses to a **~72px icon-only rail**:
  labels hidden, icons centered, CSS tooltip with the item label on
  hover/focus (`data-label` + `::after`), active-state styling preserved.
- **Collapse toggle** button inside `.app-sidebar-brand` (desktop only,
  `d-none d-lg-inline-flex`), chevron-bar-left/right icon reflecting
  state, `aria-expanded`/`aria-label` wired.
- **Persistence**: `localStorage["harness.sidebar.collapsed"]`, restored
  without layout flash via `html[data-sidebar-collapsed]` set by a small
  bootstrap script (see RF-002).
- Mobile (<992px) offcanvas drawer is **untouched**: same
  `data-bs-toggle`/`data-bs-dismiss` mechanics, same
  `taskboard.closeSidebar` on nav click, rail rules never apply below
  `lg`.
- **Topbar**: hamburger (mobile) + current page title on the left;
  Settings, Logout kept on the right; the boilerplate **"About" link is
  removed**.
- **Button spacing audit**: every action group uses `d-flex gap-2`
  (general) or `d-inline-flex gap-1` (dense/inline cells); remaining
  loose groups are normalized; interactive targets ≥44px on touch
  viewports.
- Transitions respect `prefers-reduced-motion`; rail width transition
  ≤200ms.

### Out of scope

- Dark mode / new color tokens (the app already uses `--bs-*` adaptive
  tokens).
- PWA, service worker, manifest — not requested for this repo.
- Icon set changes: keep Blazor.Bootstrap `IconName` components as-is.
- Page content redesigns, new features, API or server changes.
- `.github/workflows/**` changes.

## 3. Technical Context

Relevant existing code:

- `src/Taskboard.Blazor/Layout/MainLayout.razor` — `app-shell`, the
  `offcanvas-lg offcanvas-start app-sidebar` div, `.app-sidebar-brand`
  (Kanban icon + "Harness" + `btn-close` for mobile), `.app-topbar` with
  the mobile `data-bs-toggle="offcanvas"` hamburger, Settings button,
  **"About" link** (`href` to the GitHub repo) and the Logout form.
- `src/Taskboard.Blazor/Layout/NavMenu.razor` — `nav nav-pills flex-column
  gap-1 p-3` with `NavLink`s already using distinct
  `<Icon Name="IconName.*" />` per item and `CloseSidebarAsync` calling
  `taskboard.closeSidebar`.
- `src/Taskboard.Client/wwwroot/css/site.css` — `.app-shell`,
  `.app-sidebar` (16.25rem), `.app-sidebar-brand`, `.app-sidebar
  .nav-link` (flex + `gap: .75rem`), `.app-topbar`, `.app-main` (lines
  ~45-123); `prefers-reduced-motion` block already exists.
- `src/Taskboard.Client/wwwroot/js/taskboard.js` — `window.taskboard`
  helpers (`closeSidebar`); new sidebar helpers belong here.
- `src/Taskboard.Client/wwwroot/index.html` — host page; head is the
  right place for the pre-paint collapse-restore script.
- Button-spacing reference (already conforming): `KanbanBoard.razor`
  (`d-flex gap-2`), `TaskDetailDialog.razor` (`gap-2`/`gap-1`),
  `Settings.razor` (`flex-wrap gap-2`), `Agents.razor` (`d-inline-flex
  gap-1`).

Files to create/modify:

- `src/Taskboard.Blazor/Layout/MainLayout.razor` — toggle wiring, topbar
  title, About removal, collapse state + JS interop.
- `src/Taskboard.Blazor/Layout/NavMenu.razor` — `data-label` attributes
  and `.nav-label` spans for rail mode.
- `src/Taskboard.Client/wwwroot/css/site.css` — `.app-sidebar` rail
  rules inside `@media (min-width: 992px)`, tooltip, transitions,
  reduced-motion guard.
- `src/Taskboard.Client/wwwroot/js/taskboard.js` —
  `taskboard.getSidebarCollapsed`/`setSidebarCollapsed` helpers
  (localStorage + `document.documentElement.dataset`).
- `src/Taskboard.Client/wwwroot/index.html` — inline pre-paint script
  that mirrors `localStorage` into `html[data-sidebar-collapsed]` before
  Blazor renders.
- Loose button groups found in the audit (pages/dialogs) — normalize to
  `gap-2`/`gap-1`.

## 4. Requirements

| # | Requirement |
|---|---|
| RF-001 | On viewports ≥992px the sidebar collapses to a ~72px icon-only rail: `.nav-label` text hidden, `NavLink`s centered (`justify-content: center`), brand text hidden, toggle remains visible and centered. |
| RF-002 | Collapse state persists in `localStorage["harness.sidebar.collapsed"]` and is applied pre-paint by an inline `index.html` script that sets `html[data-sidebar-collapsed]`; all rail CSS is gated on `html[data-sidebar-collapsed] .app-sidebar` inside `@media (min-width: 992px)` — no flash, and the attribute is inert below `lg`. |
| RF-003 | Collapsed items expose a CSS tooltip: `.nav-link[data-label]::after` shown on `:hover`/`:focus-visible`, positioned right of the rail, using existing `--bs-*` tokens (dark bg, rounded, shadow). `data-label` is present on every nav item. |
| RF-004 | The offcanvas mobile drawer (<992px) is unchanged: hamburger opens it, `btn-close`/`taskboard.closeSidebar` close it, no rail styling applies, and the collapse toggle is hidden (`d-none d-lg-inline-flex`). |
| RF-005 | The topbar shows the current page title at the left (resolved from `NavigationManager.Uri` against the known routes: Board, Gantt, Workflow, AI Chat, CLI Agents, VS Code, Terminal, Settings, Skills, Prompts); the "About" external link is removed. |
| RF-006 | Icons stay as Blazor.Bootstrap `<Icon Name>` (inline SVG — no font dependency). Every nav item keeps a distinct icon; in rail mode the icon renders at ~1.25rem centered. |
| RF-007 | Every group of sibling action buttons is wrapped in `d-flex gap-2` (or `d-inline-flex gap-1` for dense/inline contexts); nav and rail items keep ≥44px effective touch targets; transitions are disabled under `prefers-reduced-motion: reduce`. |
| RF-008 | No regressions: `dotnet build` clean with `TreatWarningsAsErrors`, existing test suite green, `dotnet format --verify-no-changes` passes. |

## 5. API Contract

N/A — no API surface. New client-side JS helpers only:

```text
window.taskboard.getSidebarCollapsed()  → bool (localStorage read)
window.taskboard.setSidebarCollapsed(v) → writes localStorage +
                                          document.documentElement.dataset
```

## 6. Acceptance Criteria

- **AC-1** *Given* a ≥1280px viewport, *when* I click the sidebar
  collapse toggle, *then* the sidebar shrinks to the ~72px rail, labels
  hide, and hovering a nav item shows its tooltip.
- **AC-2** *Given* the rail is collapsed, *when* I reload the page,
  *then* it renders collapsed on first paint (no expand-then-collapse
  flash).
- **AC-3** *Given* a 375px viewport, *when* I open the menu, *then* the
  offcanvas drawer + close button behave exactly as today and the rail
  toggle is not visible.
- **AC-4** *Given* any page, *when* rendered, *then* the topbar shows
  that page's title and no "About" link exists.
- **AC-5** *Given* `/agents`, `/settings` or a dialog action row, *when*
  inspected at 375px, *then* sibling buttons have ≥8px separation and
  ≥44px touch targets.
- **AC-6** *Given* `prefers-reduced-motion: reduce`, *when* collapsing
  the sidebar, *then* no width/position transition animates.
- **AC-7** *Given* the collapsed rail, *when* a nav item receives
  keyboard focus, *then* the tooltip appears (focus parity with hover).

**Edge cases:**

- `localStorage` unavailable (private mode) → toggle still works for the
  session; no exception escapes the JS interop call.
- Viewport resized across the `lg` boundary while collapsed → drawer
  mode is unaffected; the `html` attribute stays but is inert below
  `lg`.
- Tooltip overflow near the right edge → tooltip uses absolute
  positioning `left: calc(100% + 12px)`; acceptable to clip at viewport
  edge (matches LangGraph-UI behavior).
- First visit (no stored value) → sidebar starts expanded.

## 7. Task Plan

- **T1 — JS + CSS foundation**: `taskboard.js` helpers, `index.html`
  pre-paint script, rail/tooltip/reduced-motion rules in `site.css`
  gated on `html[data-sidebar-collapsed]` + `min-width: 992px`.
- **T2 — NavMenu**: `.nav-label` spans + `data-label` on every `NavLink`;
  keep `Icon` components unchanged.
- **T3 — MainLayout**: collapse toggle button, `IJSRuntime` interop
  (read on `OnAfterRenderAsync`, write on toggle), topbar page-title map
  + `NavigationManager.LocationChanged`, About removal.
- **T4 — Button spacing audit**: sweep `Components/Pages/*` and
  `Components/GitHub|Shared/*` for sibling buttons lacking a `gap-*`
  flex container; normalize; verify ≥44px targets.
- **T5 — Verification**: `dotnet build`, `dotnet test`, `dotnet format
  --verify-no-changes`; manual check at 375px / 992px / 1280px.
- **T6 — Done + delivery**: DoD complete → `Status = Done` →
  commit/push/PR → squash merge → host re-deploy
  (`dotnet publish` + `systemctl --user restart taskboard-server`).

**7.1 Validation strategy (Frontend/.NET):**

- `dotnet build` — 0 warnings (`TreatWarningsAsErrors`).
- `dotnet test` — existing suite green (no new server behavior).
- `dotnet format --verify-no-changes` — pass.
- Manual: collapse/expand + reload at 1280px; drawer at 375px; tooltip
  on hover and keyboard focus; `localStorage` key present.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-sidebar-icon-rail` off `main`; PR →
  squash merge; never commit to `main`/`develop` directly.
- No changes under `.github/workflows/`.
- No secrets; `localStorage` stores only a UI boolean — no user data.
- Tests are not strictly required for CSS/markup-only changes, but any
  JS helper logic that can fail (storage access) must be
  exception-guarded; existing suite must stay green.

## 9. Definition of Done

- [ ] All RF-001..RF-008 implemented.
- [ ] AC-1..AC-7 verified at 375px / 992px / 1280px.
- [ ] Edge cases handled (storage unavailable, resize across `lg`,
      first visit).
- [ ] `dotnet build` clean, `dotnet test` green, `dotnet format
      --verify-no-changes` pass.
- [ ] Spec status → `Done`; PR merged; host redeployed and smoke-checked
      (rail toggles, persists, drawer works on mobile).
