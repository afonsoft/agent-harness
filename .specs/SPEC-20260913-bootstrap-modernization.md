# SPEC-20260913-bootstrap-modernization

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | bootstrap-modernization |
| Type | Frontend / Refactor |
| Stack | .NET 10 / Blazor Server / Blazor.Bootstrap 4.0.0 / Bootstrap 5.3 |
| Repository | taskboard-ai |
| Branch | `feature/devin-20260913-bootstrap-modernization` |
| Ticket | N/A |
| Status | Done |

## 1. User Story

**As a** Taskboard user,
**I want** the Blazor UI rebuilt on Bootstrap 5.3 via the Blazor.Bootstrap component library, with a modern shell (collapsible sidebar, top bar, native dark/light theme),
**So that** the application has a consistent, responsive, professional look, fewer styling systems to maintain, and no MudBlazor/Tailwind dependency.

**Problem context:**
The current UI stacks three overlapping styling systems: Tailwind CSS 3.4 built through an `npm`/`build:css` step, `bootstrap-grid.min.css` via CDN (grid only), and a ~800-line hand-written component layer in `site.css`. MudBlazor 9.9.0 is referenced solely for dialogs, tabs, and snackbars. This creates:

- A Node.js/npm build dependency inside the .NET build (`BuildTailwindCss` target in `Taskboard.Server.csproj`).
- Visual fragmentation (Tailwind utilities + custom token classes + MudBlazor components).
- A runtime CDN dependency for `bootstrap-grid` on a product that is meant to be local-first.
- Two "darks": `data-theme`/`class="dark"` tokens in `site.css` plus `MudThemeProvider` — which can disagree.

This SPEC supersedes the styling layer defined in `SPEC-20260911-tailwind-theme-refresh` (Tailwind + bootstrap-grid CDN). The migration replaces all of it with a single system: **Bootstrap 5.3 + Blazor.Bootstrap 4.0.0 + a thin `site.css` token/theme layer mapped to Bootstrap CSS variables**.

## 2. Scope

**In scope:**

- Add NuGet package `Blazor.Bootstrap` **4.0.0** (TFM `net10.0`, published 2026-08-30) to `Taskboard.Blazor` via `Directory.Packages.props` (central package management).
- Vendor Bootstrap assets into `src/Taskboard.Server/wwwroot/lib/` (no runtime CDN):
  - `bootstrap.min.css` 5.3.x
  - `bootstrap.bundle.min.js` 5.3.x
  - `bootstrap-icons` font + css 1.13.x
- Remove **MudBlazor** entirely: package reference, `AddMudServices()`, `MudThemeProvider`, `MudDialogProvider`, `MudSnackbarProvider`, every `Mud*` component, `IDialogService`, `ISnackbar`.
- Remove **Tailwind** entirely: `package.json`, `package-lock.json`, `BuildTailwindCss` target in `Taskboard.Server.csproj`, `tailwind.input.css`, generated `tailwind.css`, the `bootstrap-grid` CDN link, and every Tailwind utility class in `.razor` markup.
- Rewrite `site.css` as a thin layer: `data-bs-theme` overrides for `--bs-*` variables plus the few Taskboard-specific components Bootstrap does not provide (kanban columns, priority/status swatches).
- Rewrite all 16 `.razor` files (shell + pages + dialogs + shared components) with Bootstrap 5.3 markup and Blazor.Bootstrap components.
- Modernize the layout: collapsible sidebar (Bootstrap `offcanvas` on `< lg` breakpoints), sticky top bar with page context, Bootstrap Icons replacing inline SVGs, refined kanban visuals (column accent bars, count badges, hover states).
- Theme: light-only default Bootstrap look. `data-bs-theme` dark/light toggle was **removed on 2026-09-13 by user request** — the app ships the standard Bootstrap light theme; `/api/settings` still persists `theme`, always saved as `"light"`.
- Preserve all behavior: HTML5 drag-and-drop in `KanbanBoard`, `Virtualize` usage, `TaskboardClient` calls, login/logout forms, clipboard interop in `Prompts`.

**Out of scope:**

- Backend/API contract changes (`/api/settings`, `/api/skills`, `/api/login`, `/api/logout` unchanged).
- New pages, routes, or features.
- Authentication flow changes.
- `Taskboard.Maui` and any non-Blazor frontends.
- Replacing the `Virtualize` drag model or adding keyboard-based drag-and-drop.
- Sass/custom Bootstrap builds — we consume the distributed CSS plus token overrides.
- bUnit/component test infrastructure (no test project for Blazor exists today).

## 3. Technical Context

**Where the change happens:**
Presentation layer only — `Taskboard.Blazor` (components) and `Taskboard.Server` (`Program.cs`, `wwwroot`, `.csproj` build target).

**Files to read before implementing:**

- `CLAUDE.md`, `.claude/rules/global-rules.md`
- `.specs/SPEC-20260911-tailwind-theme-refresh.md` (superseded styling approach)
- `.specs/SPEC-20260911-kanban-smartsheet-layout.md`, `.specs/SPEC-20260911-settings-ux-redesign.md`, `.specs/SPEC-20260911-skills-ux-redesign.md` (layout intent to preserve)
- `src/Taskboard.Server/Program.cs` (`AddMudServices`, `MapRazorComponents<App>`)
- `src/Taskboard.Server/Taskboard.Server.csproj` (`BuildTailwindCss` target)
- `src/Taskboard.Server/wwwroot/css/site.css`, `css/tailwind.input.css`
- `src/Taskboard.Blazor/App.razor`, `_Imports.razor`, `Routes.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`, `Layout/NavMenu.razor`
- `src/Taskboard.Blazor/Components/BoardView.razor`, `TaskCard.razor`
- `src/Taskboard.Blazor/Components/Pages/{Login,Settings,Skills,Prompts,AiChat,GitHubBoard}.razor`
- `src/Taskboard.Blazor/Components/GitHub/{KanbanBoard,AgentSelectionModal,NewTaskDialog,RepositorySelector,TaskDetailDialog,TaskLogTab}.razor`
- `src/Taskboard.Blazor/Components/Shared/{Loading,EmptyState,SkillDetailDialog}.razor`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`
- `Directory.Packages.props`

**Files to create or modify:**

```text
Directory.Packages.props                                   # + Blazor.Bootstrap 4.0.0, - MudBlazor
src/Taskboard.Blazor/Taskboard.Blazor.csproj               # + PackageReference Blazor.Bootstrap, - MudBlazor
src/Taskboard.Server/Taskboard.Server.csproj               # - BuildTailwindCss target
src/Taskboard.Server/package.json                          # DELETED
src/Taskboard.Server/package-lock.json                     # DELETED
src/Taskboard.Server/wwwroot/lib/bootstrap/bootstrap.min.css            # NEW (vendored)
src/Taskboard.Server/wwwroot/lib/bootstrap/bootstrap.bundle.min.js      # NEW (vendored)
src/Taskboard.Server/wwwroot/lib/bootstrap-icons/...                    # NEW (vendored)
src/Taskboard.Server/wwwroot/css/site.css                  # rewritten: --bs-* theme layer + taskboard components
src/Taskboard.Server/wwwroot/css/tailwind.input.css        # DELETED
src/Taskboard.Server/wwwroot/css/tailwind.css              # DELETED
src/Taskboard.Blazor/App.razor                             # local lib links, data-bs-theme, blazor.bootstrap assets
src/Taskboard.Blazor/_Imports.razor                        # + @using BlazorBootstrap, - MudBlazor
src/Taskboard.Blazor/Layout/MainLayout.razor               # Bootstrap shell + Toasts + data-bs-theme toggle
src/Taskboard.Blazor/Layout/NavMenu.razor                  # nav-pills style links + Bootstrap Icons
src/Taskboard.Blazor/Components/Pages/Login.razor          # centered card, form-floating or form-control
src/Taskboard.Blazor/Components/Pages/Settings.razor       # cards + form-switch + ToastService
src/Taskboard.Blazor/Components/Pages/Skills.razor         # table + input search + Modal
src/Taskboard.Blazor/Components/Pages/Prompts.razor        # cards + copy + ToastService
src/Taskboard.Blazor/Components/Pages/AiChat.razor         # chat layout with Bootstrap
src/Taskboard.Blazor/Components/Pages/GitHubBoard.razor    # wrapper page
src/Taskboard.Blazor/Components/BoardView.razor            # kanban board
src/Taskboard.Blazor/Components/TaskCard.razor             # card + badges
src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor   # kanban + Modal/ToastService wiring
src/Taskboard.Blazor/Components/GitHub/RepositorySelector.razor
src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor       # -> Bootstrap Modal
src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor    # -> Bootstrap Modal
src/Taskboard.Blazor/Components/GitHub/AgentSelectionModal.razor # -> Bootstrap Modal
src/Taskboard.Blazor/Components/GitHub/TaskLogTab.razor          # MudTabs -> Tabs/Tab
src/Taskboard.Blazor/Components/Shared/Loading.razor             # Spinner
src/Taskboard.Blazor/Components/Shared/EmptyState.razor          # icon + text
src/Taskboard.Blazor/Components/Shared/SkillDetailDialog.razor   # -> Bootstrap Modal
src/Taskboard.Server/Program.cs                            # - AddMudServices, + AddBlazorBootstrap
```

**Component mapping (MudBlazor → Blazor.Bootstrap / Bootstrap 5.3):**

| Current | Replacement |
| --- | --- |
| `MudThemeProvider` | `data-bs-theme` attribute on `<html>` (Bootstrap 5.3 native color modes) |
| `MudSnackbarProvider` + `ISnackbar` | `<Toasts class="p-3" />` in `MainLayout` + injected `ToastService` |
| `MudDialogProvider` + `IDialogService.ShowAsync<T>` | `<Modal @ref>` components; parameters passed via `Dictionary<string, object>` + `Modal.ShowAsync<T>` |
| `MudDialog` / `MudDialogInstance` | `Modal` with `OnHidden`/`OnShown` callbacks, `ModalReference` results |
| `MudText`, `MudButton`, `MudCard`, `MudLink`, `MudAlert` | `h*`/`p`, `Button`/`btn`, `Card`/`card`, `a`, `Alert`/`alert` |
| `MudTextField`, `MudSelect`, `MudSelectItem`, `MudForm` | `form-control`, `form-select`, `EditForm` + `DataAnnotationsValidator` |
| `MudTabs`/`MudTabPanel` | `Tabs`/`Tab` (Blazor.Bootstrap) |
| `MudProgressCircular` | `Spinner` (`SpinnerType.Border`) |
| Inline SVG icons | `Icon`/`BootstrapIcons` (`bi-*`) |
| Tailwind utilities (`flex`, `p-6`, `text-sm`, `mb-2`, …) | Bootstrap utilities (`d-flex`, `p-4`, `small`, `mb-2`, …) — mostly mechanical |
| Custom classes (`btn-secondary`, `card`, `input`, `nav-link`, `kanban-*`, `task-card`, `toggle`, `table`, `alert`, `badge`, `chip`) | Bootstrap natives where they exist (`btn btn-outline-secondary`, `card`, `form-control`, `nav-link`, `table`, `alert`, `badge`, `form-switch`); kanban/status/priority classes stay in `site.css` rebuilt on `--bs-*` tokens |

## 4. Requirements

### RF-001: Bootstrap is vendored locally — no runtime CDN

- **Description:** `App.razor` loads `lib/bootstrap/bootstrap.min.css`, `lib/bootstrap-icons/bootstrap-icons.min.css`, `_content/Blazor.Bootstrap/blazor.bootstrap.css`, `css/site.css`, then `bootstrap.bundle.min.js` + `_content/Blazor.Bootstrap/blazor.bootstrap.js`.
- **Rules:**
  - No `cdn.jsdelivr.net` / `cdn.tailwindcss.com` references remain.
  - App renders fully offline.
- **Input → Output:** `grep -r "cdn\." src/Taskboard.Blazor src/Taskboard.Server/wwwroot` returns no asset links.

### RF-002: `site.css` is rebuilt on Bootstrap variables

- **Description:** `site.css` drops the custom `--color-*` palette and defines light/dark overrides for `--bs-body-bg`, `--bs-body-color`, `--bs-border-color`, `--bs-emphasis-color`, etc., under `:root` and `[data-bs-theme="dark"]`. Taskboard-only classes kept: `kanban-board`, `kanban-column`, `kanban-column-header` (+ `status-*` modifiers), `kanban-card`, `task-card` (+ `priority-*` modifiers), `task-priority-swatch`, `app-shell` helpers.
- **Rules:**
  - Status/priority colors defined as CSS vars with dark overrides.
  - No class duplicates a Bootstrap component name with different semantics (`.card`, `.table`, `.alert`, `.badge` must be Bootstrap's).
- **Input → Output:** `site.css` ≤ ~400 lines; `grep "var(--color-" src/` returns nothing.

### RF-003: `MainLayout` is a Bootstrap shell with collapsible sidebar

- **Description:** Fixed sidebar (`d-flex flex-column`, width 260px) + top bar + scrollable `main`. Below `lg` the sidebar becomes a Bootstrap `offcanvas` toggled from the top bar. Contains `<Toasts />` for notifications. Theme toggle button swaps `data-bs-theme` via JS interop.
- **Rules:**
  - Theme read from `TaskboardClient.GetSettingsAsync()`; fallback `dark`.
  - `ErrorBoundary` preserved around `@Body`.
  - Top bar keeps settings link, About link, logout form (`method="post" action="/api/logout"`).
- **Input → Output:** Sidebar visible ≥992px, offcanvas hamburger <992px; toggle flips `data-bs-theme` instantly.

### RF-004: `NavMenu` uses Bootstrap nav + icons

- **Description:** `nav nav-pills flex-column` links with `bi-*` icons; `NavLink` active state via Bootstrap `active` class.
- **Rules:** all routes and `aria-label`s preserved.

### RF-005: `Login` is a centered Bootstrap card

- **Description:** Centered `card` (`max-width` ~400px), `form-label` + `form-control`, `btn btn-primary w-100`, keeps `method="post" action="/api/login"` and anti-forgery.
- **Rules:** WCAG AA contrast and focus rings (Bootstrap defaults); labels associated to inputs.

### RF-006: `BoardView`/`TaskCard` are Bootstrap kanban

- **Description:** Header strip as a `card` with `row`/`col` fields; columns via `d-flex overflow-x-auto` + `kanban-column`; cards via `task-card` with `badge` metadata. `Virtualize`/`@key` preserved.
- **Rules:** no drag-and-drop added; `Loading`/`EmptyState` preserved.

### RF-007: `KanbanBoard` keeps drag-and-drop, uses Modal + Toasts

- **Description:** HTML5 `draggable`/`ondrop` events unchanged; `ISnackbar` → `ToastService`; `IDialogService.ShowAsync<…>` → `<Modal>` instances with parameter dictionaries; columns/cards share the `kanban-*` skin.
- **Rules:**
  - `OnDropAsync` behavior and agent-launch flow identical (snackbars become toasts with `ToastType.Success`/`Danger`).
  - `AgentSelectionModal`, `NewTaskDialog`, `TaskDetailDialog` render inside Bootstrap `Modal`s and still return their results to `KanbanBoard`.

### RF-008: Dialog components become Bootstrap modals

- **Description:** `AgentSelectionModal`, `NewTaskDialog`, `TaskDetailDialog`, `SkillDetailDialog` drop `MudDialog`/`MudDialogInstance` and become components shown via `Modal.ShowAsync<DialogType>(parameters)`. `TaskLogTab` `MudTabs` → Blazor.Bootstrap `Tabs`.
- **Rules:**
  - Each dialog exposes the same callback/`DialogResult` semantics (parent receives selected agent / created task / nothing).
  - Focus returns correctly on close; `Esc`/backdrop close behavior preserved.

### RF-009: `Settings`, `Skills`, `Prompts`, `AiChat`, `RepositorySelector` restyled

- **Description:**
  - `Settings`: `card` sections, `form-switch` for theme, `form-control`/`type="password"` for token, `table` for agents, sticky action bar; `Snackbar` → `ToastService`.
  - `Skills`: `card` + `table` + search `form-control`; row click opens `SkillDetailDialog` modal.
  - `Prompts`: card layout; copy button keeps `navigator.clipboard` interop; success toast.
  - `AiChat`: Bootstrap chat layout (list group or flex column + input group).
  - `RepositorySelector`: `form-control` token input + `ToastService`.
- **Rules:** all `TaskboardClient` calls and save flows unchanged; secrets never rendered in plain text.

### RF-010: Tailwind and MudBlazor fully removed

- **Description:** `Taskboard.Blazor.csproj` drops `MudBlazor` and adds `Blazor.Bootstrap`; `Directory.Packages.props` swaps the versions; `Program.cs` `AddMudServices()` → `AddBlazorBootstrap()`; `Taskboard.Server.csproj` drops `BuildTailwindCss`; `package*.json` and tailwind CSS files deleted.
- **Rules:**
  - `grep -ri "mudblazor\|tailwind" src/ Directory.Packages.props` returns only this spec's references or none.
  - `dotnet restore` works without Node.js.
- **Input → Output:** Build succeeds on a machine without npm installed.

### RF-011: Theme uses `data-bs-theme` end-to-end

- **Description:** `App.razor` renders `<html data-bs-theme="dark">` by default; `MainLayout`/`Settings` toggle writes `data-bs-theme` and persists via `PUT /api/settings` (`Theme: "dark"|"light"`). No `.dark` class or `data-theme` remains.
- **Rules:** unknown values fall back to `dark`; MudBlazor's parallel dark mode no longer exists.

### RF-012: Accessibility and responsiveness

- **Description:** Functional 360px → 4K. Sidebar offcanvas on mobile. Focus rings visible. Color is never the sole state indicator (badges include text).
- **Rules:** no `!important` overrides that break Bootstrap modals; heading hierarchy not skipped.

**Business rules / invariants:**

- No business logic in markup; Blazor stays presentation-only.
- No secrets logged or rendered.
- Drag-and-drop, virtualization, clipboard interop, and the `/api/settings` round-trip behave exactly as before.

## 5. API Contract

No API changes. Consumed endpoints unchanged: `GET/PUT /api/settings`, `GET /api/skills`, `POST /api/login`, `POST /api/logout`, plus existing board/GitHub endpoints already used by `TaskboardClient`.

New package contract:

```xml
<PackageVersion Include="Blazor.Bootstrap" Version="4.0.0" />
```

Static asset layout:

```text
wwwroot/lib/bootstrap/bootstrap.min.css        (5.3.x)
wwwroot/lib/bootstrap/bootstrap.bundle.min.js  (5.3.x)
wwwroot/lib/bootstrap-icons/bootstrap-icons.min.css
wwwroot/lib/bootstrap-icons/fonts/*.woff2
```

## 6. Acceptance Criteria

- [ ] **Given** the repo is built on a machine without Node.js **when** `dotnet build src/Taskboard.Server/Taskboard.Server.csproj -c Release` runs **then** it succeeds with 0 errors/warnings and no npm step executes.
- [ ] **Given** the app runs with no internet access **when** any page loads **then** Bootstrap CSS/JS/icons resolve from `wwwroot/lib` and no CDN request is made.
- [ ] **Given** the user is on `/login` **when** the page renders **then** the form sits in a centered Bootstrap card, and login still works.
- [ ] **Given** the user is on `/` **when** the board loads **then** kanban columns/cards render with the `kanban-*`/`task-card` classes on Bootstrap tokens, with correct priority colors.
- [ ] **Given** the user is on `/github-board` **when** they drag an issue between columns **then** the drop logic runs and a success toast (not MudBlazor snackbar) appears; the agent-selection flow still opens as a Bootstrap modal.
- [ ] **Given** the user opens task details, new task, agent selection, or skill details **when** the modal shows **then** it is a Bootstrap modal (backdrop, `Esc` close, focus return) and its result reaches the caller.
- [ ] **Given** the user toggles the theme **when** the toggle fires **then** `<html data-bs-theme>` flips, all Bootstrap components follow, and the choice persists after refresh via `/api/settings`.
- [ ] **Given** the viewport is <992px **when** the user taps the hamburger **then** the sidebar opens as an offcanvas and closes on navigation.
- [ ] **Given** `Settings`, `Skills`, `Prompts`, `AiChat` **when** saved/copied/searched **then** Bootstrap-styled controls work and toasts confirm actions.
- [ ] **Given** a codebase search **when** checking for leftovers **then** no `Mud*` component, `IDialogService`, `ISnackbar`, Tailwind class (`flex`, `text-*` utilities outside `site.css` usage), `data-theme`, or `.dark` class remains in `.razor`/`.css` files.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Settings API down at init | `GetSettingsAsync` throws | `data-bs-theme="dark"` fallback |
| `Theme = "auto"` or unknown | settings payload | treated as `dark`; normalized to `dark` on save |
| Modal closed via `Esc`/backdrop | user presses Esc | same result as cancel; no dangling state |
| Very long task titles | 500-char title | card truncates with ellipsis, no layout break |
| 360px viewport | mobile | offcanvas nav, horizontal kanban scroll, no overlap |
| Missing icon font | fonts blocked/deleted | layout still works, icons degrade to empty squares without breaking |

## 7. Task Plan

- [ ] **T1 — Discovery:** read section-3 files; confirm `TaskboardClient` settings shape, dialog parameter/result flow, and `Program.cs` wiring.
- [ ] **T2 — Assets:** download Bootstrap 5.3.x (`bootstrap.min.css`, `bootstrap.bundle.min.js`) and bootstrap-icons into `wwwroot/lib/`; remove tailwind files, `package*.json`, `BuildTailwindCss` target, and the grid CDN link.
- [ ] **T3 — Packages:** add `Blazor.Bootstrap` 4.0.0, remove `MudBlazor` (props + csproj); `Program.cs`: `AddBlazorBootstrap()`; `_Imports.razor`: `@using BlazorBootstrap`.
- [ ] **T4 — Theme layer:** rewrite `site.css` on `--bs-*` variables + `data-bs-theme`; port `kanban-*`, `task-card`, `status-*`, `priority-*`, `task-priority-swatch` classes.
- [ ] **T5 — Shell:** `App.razor` asset links; `MainLayout.razor` Bootstrap shell + `<Toasts />` + `data-bs-theme` toggle + offcanvas sidebar; `NavMenu.razor` with `bi-*` icons.
- [ ] **T6 — Login:** Bootstrap card rewrite.
- [ ] **T7 — Board:** `BoardView` + `TaskCard` rewrite.
- [ ] **T8 — GitHub board + dialogs:** `KanbanBoard` (ToastService + Modals), `RepositorySelector`, `AgentSelectionModal`, `NewTaskDialog`, `TaskDetailDialog`, `TaskLogTab` (Tabs).
- [ ] **T9 — Remaining pages:** `Settings`, `Skills`, `Prompts`, `AiChat`, `SkillDetailDialog`, `Loading`, `EmptyState`.
- [ ] **T10 — Verification:** `dotnet build` Release 0 warnings; smoke-test all routes in both themes at desktop + 360px; offline check (block CDN) ; verify no `mudblazor|tailwind` leftovers.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
| --- | --- |
| Frontend / .NET | `dotnet build -c Release` clean; manual smoke-test of every route in light+dark; drag-drop and modal result flows exercised; offline load verified |

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop`; work on `feature/devin-20260913-bootstrap-modernization`.
- **Workflows:** do not modify `.github/workflows` without human approval.
- **Dependencies:** only `Blazor.Bootstrap` 4.0.0 added (justify in PR description); Bootstrap assets vendored — no new runtime CDN.
- **Security:** no secrets in markup/logs; token inputs stay `type="password"`.
- **Scope:** no backend contracts, no new endpoints, no domain changes.
- **Architecture:** presentation-only changes in `Taskboard.Blazor` + static assets in `Taskboard.Server`.

## 9. Definition of Done

- [ ] All RF-001…RF-012 implemented.
- [ ] All acceptance criteria verified (build + smoke tests, both themes, mobile width, offline).
- [ ] `dotnet build src/Taskboard.Server/Taskboard.Server.csproj -c Release` → 0 warnings, 0 errors.
- [ ] `grep -ri "mudblazor|tailwind|data-theme" src/` → clean (spec docs excepted).
- [ ] Guardrails respected; PR opened on the feature branch.

**Next action after DoD:** set `Status: Done` and open the PR.

## Open Questions / Pending Ambiguity

- N/A. Decisions settled with the user: Blazor.Bootstrap library, full MudBlazor removal, full Tailwind removal, all pages in scope. Vendored assets chosen because the product is local-first.
