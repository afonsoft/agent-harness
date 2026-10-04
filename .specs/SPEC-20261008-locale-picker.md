# SPEC-20261008 — Locale picker (pt-BR/en/es) + string normalization

- Status: In progress
- Author: devin (requested by afonsoft: "crie combo de tradução na parte
  superior direito. Revise as traduções.")
- Base: `main` (P1–P3 + #460/#461/#467/#468 merged)
- Related: SPEC-20261003-i18n-consistency (pt-BR product language, culture pin)

## Context

SPEC-20261003 set pt-BR as the product language (`<html lang="pt-BR">`,
WASM culture pinned) but no mechanism to switch language exists and copy is
mixed: chrome (MainLayout/NavMenu/Login) is authored in English while
GitHub components and several dialogs are authored in pt-BR. This SPEC adds a
selectable UI locale — pt-BR (default, stays the product language), en, es —
with a top-right combo, and normalizes the mixed strings into a shared
string table. No `.github/workflows/**` changes.

## Requirements

### RF-001 — Locale service + string table

- `src/Taskboard.Blazor/Localization/UiStrings.cs`: static dictionaries for
  `pt-BR`, `en`, `es` keyed by dotted ids (`nav.board`, `topbar.logout`,
  `common.cancel`, …). Lookup falls back pt-BR → en → the key itself, so a
  missing translation can never blank the UI.
- `ILocaleService` (scoped, `Taskboard.Blazor/Services`): `Culture`,
  `Options` (`pt-BR`/`en`/`es` with short labels), `T(key)` and
  `T(key, args)` (string.Format), `InitializeAsync()` (reads persisted
  choice), `SetCultureAsync(culture)` (persists, sets
  `CultureInfo.DefaultThreadCurrentCulture/UICulture`, updates
  `document.documentElement.lang`, fires `Changed`).
- `taskboard.js`: `getLocale`/`setLocale` (localStorage `harness.locale`,
  same try/catch pattern as `getSidebarCollapsed`) and `setHtmlLang`.
- Registered in `Taskboard.Client/Program.cs`; the pt-BR default pin stays
  (it is now just the initial value — `SetCultureAsync` overrides).

### RF-002 — Top-right locale combo

`LocalePicker.razor` in `Layout/`: compact `<select>` with the three
locales, globe icon, placed in `MainLayout` topbar between the page title
and the settings gear. On change → `SetCultureAsync`. MainLayout subscribes
to `Changed` → `StateHasChanged` (child components re-render with the new
strings) and re-resolves the page title.

### RF-003 — Localized surfaces (translation review pass)

All strings below move to `L.T()` keys with pt-BR/en/es values:

- **Layout**: MainLayout (skip link, sidebar toggles, settings aria-label,
  logout, error-boundary message, page titles), NavMenu (nav labels +
  aria-labels + repo warning), MinimalLayout footer if present.
- **Login**: headings, labels, button, placeholders.
- **Shared**: Loading default, EmptyState defaults, RedirectToLogin.
- **GitHub components** (the pt-BR outlier sweep — "revise as traduções"):
  KanbanBoard, MoveIssueModal, NewTaskDialog, TaskDetailDialog, TaskLogTab,
  IssueCommentsTab, AgentSelectionModal, AgentConfigTab.

Pages not listed keep their authored strings for now — the mechanism is in
place for incremental per-page migration.

## Acceptance

- [ ] Combo renders top-right on every authed page; switching applies
      immediately without reload and persists across restarts.
- [ ] `document.documentElement.lang` follows the selection.
- [ ] Number/date formats follow the selection (DefaultThreadCurrentCulture).
- [ ] Missing key never renders empty (fallback chain).
- [ ] Unit tests: dictionary key-set parity across cultures; service
      fallback + persistence + Changed; razor-source check that MainLayout
      hosts the picker.
