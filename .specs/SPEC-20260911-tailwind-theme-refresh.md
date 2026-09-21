# SPEC-20260911-tailwind-theme-refresh

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | tailwind-theme-refresh |
| Type | Frontend |
| Stack | .NET 10 / Blazor Server / Tailwind CSS 4.x CDN / MudBlazor (providers only) |
| Repository | agent-harness |
| Branch | `feature/devin-20260911-tailwind-theme-refresh` |
| Ticket | N/A |
| Status | Deprecated — superseded by SPEC-20260913-bootstrap-modernization |

## 1. User Story

**As a** Taskboard admin/user,
**I want** a polished, responsive Blazor UI styled with Tailwind CSS and a persistent light/dark theme,
**So that** the application looks modern and consistent from login through the board, settings, and skills pages.

**Problem context:**
The current UI relies on hand-written CSS in `site.css` and MudBlazor components. It has no unified design token system, the theme is not consistent across the shell, and the existing MudBlazor-only components create a fragmented experience. A previous implementation already added settings and skills screens; this SPEC supersedes/refines their styling layer by replacing the CSS system with Tailwind and CSS custom properties.

## 2. Scope

**In scope:**
- Add Tailwind CSS 4.x via CDN plus Bootstrap 5.x `bootstrap-grid` only (no Bootstrap components).
- Define a light/dark design token layer in `site.css` using CSS custom properties + `data-theme`.
- Refactor `MainLayout.razor` into a Tailwind shell with a fixed sidebar, top bar, and theme toggle.
- Refactor `NavMenu.razor` to use Tailwind-styled links.
- Refactor `Login.razor` into a centered, accessible Tailwind card.
- Refactor `BoardView.razor` and `TaskCard.razor` into a Tailwind kanban.
- Refactor `GitHubBoard.razor` and `KanbanBoard.razor` into the same Tailwind visual language.
- Refactor `Settings.razor` and `Skills.razor` into Tailwind forms/tables.
- Keep MudBlazor as a provider for dialogs and snackbars only (`MudDialogProvider`, `MudSnackbarProvider`, `MudThemeProvider`).
- Persist the selected theme through the existing `/api/settings` endpoint.
- Verify the build and the public endpoint `https://task.afonsoft.dev`.

**Out of scope:**
- Replacing MudBlazor dialog/snackbar logic with custom components.
- Adding new backend API endpoints or changing contracts (`/api/settings` and `/api/skills` stay as-is).
- Mobile native applications.
- Tailwind CLI build pipeline or custom PostCSS (the team chose the CDN path).
- Removing/rewriting authentication (`/api/login`, `/api/logout`).
- Refactoring `AiChat.razor` and `RepositorySelector.razor` visuals.

## 3. Technical Context

**Where the change happens:**
Changes are isolated to the presentation layer (`Taskboard.Blazor`) and the static assets served by `Taskboard.Server`.

**Files to read before implementing:**
- `CLAUDE.md`
- `.claude/rules/global-rules.md`
- `docs/superpowers/plans/2026-09-11-tailwind-theme-refresh.md`
- `.specs/SPEC-20260910-ui-login-settings-skills.md`
- `src/Taskboard.Blazor/App.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor`
- `src/Taskboard.Blazor/Layout/NavMenu.razor`
- `src/Taskboard.Blazor/Components/Pages/Login.razor`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Components/Pages/Skills.razor`
- `src/Taskboard.Blazor/Components/BoardView.razor`
- `src/Taskboard.Blazor/Components/TaskCard.razor`
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor`
- `src/Taskboard.Blazor/Components/Shared/Loading.razor`
- `src/Taskboard.Blazor/Components/Shared/EmptyState.razor`
- `src/Taskboard.Server/wwwroot/css/site.css`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs`

**Files to create or modify:**

```text
src/Taskboard.Server/wwwroot/css/site.css                 # design tokens + component utilities (no @apply)
src/Taskboard.Blazor/App.razor                            # Tailwind + Bootstrap grid CDN
src/Taskboard.Blazor/Layout/MainLayout.razor              # shell, top bar, sidebar, theme toggle
src/Taskboard.Blazor/Layout/NavMenu.razor                 # Tailwind nav links
src/Taskboard.Blazor/Components/Pages/Login.razor         # Tailwind login card
src/Taskboard.Blazor/Components/Pages/Settings.razor      # Tailwind settings form/agents table
src/Taskboard.Blazor/Components/Pages/Skills.razor        # Tailwind skills table
src/Taskboard.Blazor/Components/BoardView.razor           # Tailwind kanban columns
src/Taskboard.Blazor/Components/TaskCard.razor            # Tailwind task card
src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor  # Tailwind GitHub kanban
src/Taskboard.Blazor/Components/Shared/Loading.razor       # Tailwind loading indicator
src/Taskboard.Blazor/Components/Shared/EmptyState.razor    # Tailwind empty state
```

## 4. Requirements

### RF-001: Tailwind CSS and Bootstrap grid are loaded from CDN
- **Description:** `App.razor` must load Tailwind 4.x CDN, a small `tailwind.config` block, and the Bootstrap 5 `bootstrap-grid` CSS only. The existing MudBlazor JS/CSS includes stay unchanged.
- **Rules:**
  - No Bootstrap components or JavaScript may be loaded.
  - No build-time Tailwind CLI step is required.
- **Input → Output:** `App.razor` contains the CDN links and the Blazor app still renders.

### RF-002: Design tokens are defined in `site.css`
- **Description:** `site.css` defines CSS custom properties under `:root` and `[data-theme="dark"]`. The tokens cover primary, background, surface, text, border, and accent colors. Component classes (`btn-primary`, `btn-secondary`, `card`, `input`, `nav-link`, `kanban-column`, `task-card`) are declared in a custom `components` layer using plain CSS (no `@apply`) because the CDN does not process arbitrary CSS files.
- **Rules:**
  - Tokens must update when `html` has `data-theme="dark"`.
  - Component classes must be usable across all target `.razor` files.
- **Input → Output:** `class="card"` renders a panel with `background-color: var(--color-surface-elevated)` and correct light/dark values.

### RF-003: `MainLayout.razor` provides a fixed sidebar and top bar
- **Description:** Replace the existing `page`/`sidebar` CSS with a Tailwind flex layout. The layout is full height, with a fixed 240px sidebar, a top bar on the right, and a scrollable main area.
- **Rules:**
  - Keep `MudThemeProvider`, `MudDialogProvider`, and `MudSnackbarProvider`.
  - Theme is applied to `document.documentElement` via JS interop: `data-theme` and `class="dark"`.
  - Theme is read from `TaskboardClient.GetSettingsAsync()` on init.
- **Input → Output:** User sees a sidebar, top bar, theme toggle, settings link, and logout form.

### RF-004: `NavMenu.razor` renders Tailwind navigation links
- **Description:** Navigation links use the Tailwind-based `nav-link` class and SVG icons. The active link receives a visual active state.
- **Rules:**
  - All links keep their `aria-label` and route `href`.
  - Mobile: the menu remains in the sidebar.

### RF-005: `Login.razor` is a centered accessible card
- **Description:** Replace the MudBlazor form with a Tailwind card, plain HTML inputs, and a Tailwind submit button. Keep `method="post" action="/api/login"` and anti-forgery behavior.
- **Rules:**
  - Labels are associated with inputs.
  - Focus states and contrast pass WCAG 2.1 AA.
  - Width is constrained to `max-w-sm` and centered.

### RF-006: `BoardView.razor` and `TaskCard.razor` are Tailwind kanban
- **Description:** Replace `board`/`column`/`task-card` CSS classes with Tailwind kanban columns. Each status column is a `kanban-column` component with a header, task count, and scrollable task list.
- **Rules:**
  - Keep `Virtualize` and `@key` usage.
  - Keep `Loading` and `EmptyState` shared components.
  - No drag-and-drop is added.

### RF-007: `GitHubBoard.razor` and `KanbanBoard.razor` share the same Tailwind skin
- **Description:** The GitHub kanban uses the same `kanban-column`/`kanban-card` classes and visual tokens. `Loading` and `MudAlert` error banners are converted to Tailwind equivalents.
- **Rules:**
  - Drag-and-drop events and dialog calls must remain functional.
  - MudBlazor dialogs (`NewTaskDialog`, `TaskDetailDialog`) are unchanged.

### RF-008: `Settings.razor` uses Tailwind forms and tables
- **Description:** Replace MudBlazor cards, switch, and table with Tailwind `card`, `input`, checkbox, and a plain HTML table. Keep the `SaveAsync` flow and `Client.SaveSettingsAsync` call.
- **Rules:**
  - Theme checkbox triggers an immediate `data-theme` update.
  - GitHub token input is a password field.
  - The agents table lists `Name`, `Version`, and `Enabled`.

### RF-009: `Skills.razor` uses a Tailwind table with search
- **Description:** Replace MudBlazor table with a Tailwind table inside a `card`. Search is a plain HTML input bound with `@bind`.
- **Rules:**
  - Keep `/api/skills` contract.
  - Keep the `Filter` logic.

### RF-010: Theme persistence is preserved through `/api/settings`
- **Description:** No backend contract changes. `MainLayout` loads theme from `GetSettingsAsync`; `Settings.razor` saves it with `SaveSettingsAsync` and applies it immediately.
- **Rules:**
  - The `Theme` string is `"dark"` or `"light"`.
  - Fallback is `dark` if the API is unavailable.

### RF-011: Responsive and accessible behavior
- **Description:** Pages must remain functional from 360px to 4K. Interactive elements have visible focus rings and `aria-label`s.
- **Rules:**
  - No `!important` overrides that break MudBlazor dialogs.
  - Bootstrap grid classes may be used alongside Tailwind, but the two should not conflict on the same element.

**Business rules / invariants:**
- No secrets (token, password) are logged or rendered in plain text unless the user explicitly reveals them.
- No business logic is placed in Blazor markup; it remains in the backend application services.
- `MudBlazor` is kept only for dialogs and snackbars.

## 5. API Contract

No new API. The feature consumes the existing settings and skills endpoints.

**Endpoint:** `GET /api/settings`
**Auth:** Cookie (already authenticated for `/settings`, but `MainLayout` reads it after login).

**Response (success):**
```json
{
  "settings": {
    "theme": "dark",
    "gitHubToken": null,
    "agents": []
  }
}
```

**Endpoint:** `PUT /api/settings`
**Auth:** Cookie

**Request:**
```json
{
  "theme": "light",
  "gitHubToken": "ghp_***",
  "agents": []
}
```

**Endpoint:** `GET /api/skills`
**Auth:** Cookie

**Response (success):**
```json
{
  "skills": [
    { "name": "manage-taskboard", "description": "...", "source": "claude", "path": "..." }
  ]
}
```

## 6. Acceptance Criteria

- [ ] **Given** the user is on `/login` **when** the page loads **then** the form is rendered inside a Tailwind-styled card with a dark background and the title is readable.
- [ ] **Given** the user is on `/` **when** the board loads **then** columns are rendered with Tailwind `kanban-column` styling and task cards match `task-card` styling.
- [ ] **Given** the user is on `/github-board` and selects a repository **when** the GitHub kanban loads **then** columns and cards use the same Tailwind visual language.
- [ ] **Given** the user is on `/settings` **when** the page loads **then** the theme checkbox, GitHub token input, and agents table are styled with Tailwind.
- [ ] **Given** the user toggles the theme in `MainLayout` or `/settings` **when** the toggle is activated **then** the `data-theme` attribute and the `dark` class are updated on `<html>` and the UI reflects the new theme immediately.
- [ ] **Given** the user saves settings **when** `PUT /api/settings` succeeds **then** the theme preference persists after a page refresh.
- [ ] **Given** the user is on `/skills` **when** they type in the search box **then** the Tailwind table filters instantly and still lists matching skills.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| API unavailable on init | `MainLayout` `OnInitializedAsync` throws | fallback to `data-theme="dark"` |
| Theme value is neither `dark` nor `light` | `settings.Theme = "auto"` | fallback to `dark` and write `dark` on save |
| CDN unavailable | network failure at runtime | App still renders because `site.css` and MudBlazor CSS are local; Tailwind classes will be unstyled |
| Mobile viewport | 360px width | layout remains scrollable and usable |

## 7. Task Plan

- [ ] **T1 — Discovery:** read all files in section 3 and confirm the existing settings API shape and MudBlazor providers.
- [ ] **T2 — Static assets:** update `site.css` with CSS custom properties and Tailwind-style component classes (no `@apply`); update `App.razor` to load Tailwind and Bootstrap grid CDNs.
- [ ] **T3 — Shell:** rewrite `MainLayout.razor` and `NavMenu.razor` with Tailwind; wire theme toggle to `data-theme` + `dark` class.
- [ ] **T4 — Login:** rewrite `Login.razor` as a Tailwind card with plain HTML form.
- [ ] **T5 — Board kanban:** rewrite `BoardView.razor` and `TaskCard.razor` with Tailwind columns and cards.
- [ ] **T6 — GitHub kanban:** rewrite `KanbanBoard.razor` (and any GitHub board wrapper) with the same Tailwind classes; keep dialog calls.
- [ ] **T7 — Settings:** rewrite `Settings.razor` with Tailwind card, checkbox, password input, and table; ensure theme applies immediately on save.
- [ ] **T8 — Skills:** rewrite `Skills.razor` with Tailwind search and table.
- [ ] **T9 — Shared components:** rewrite `Loading.razor` and `EmptyState.razor` to Tailwind.
- [ ] **T10 — Verification:** run `dotnet build src/Taskboard.Server/Taskboard.Server.csproj -c Release` with no errors; start locally or on `https://task.afonsoft.dev` and smoke-test login, board, GitHub board, settings, and skills pages plus light/dark toggle.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
| --- | --- |
| Frontend / .NET | `dotnet build` with `TreatWarningsAsErrors`; visual smoke-test on `https://task.afonsoft.dev`; no new unit-test requirement unless a layout component is extracted. |

- Build must pass with `dotnet build src/Taskboard.Server/Taskboard.Server.csproj -c Release`.
- No warnings.
- Visual verification on the public endpoint for at least two themes and five screens.

## 8. Organization Guardrails

- **Branches:** never commit to `main`, `master`, or `develop`. Use `feature/devin-20260911-tailwind-theme-refresh`.
- **Workflows:** do not modify `.github/workflows` without human approval.
- **Registries:** use only approved CDNs. `cdn.tailwindcss.com` and `cdn.jsdelivr.net` are the chosen public CDNs.
- **Security:** do not log tokens, passwords, or connection strings; do not commit secrets.
- **Scope:** do not add new backend endpoints, new domain models, or new API contracts.
- **Architecture:** keep business logic in the backend; Blazor is presentation-only.

## 9. Definition of Done

- [ ] All requirements (section 4) are implemented.
- [ ] All acceptance criteria (section 6) are covered by visual verification or passing build/tests.
- [ ] Edge cases are handled (fallback themes, API unavailability).
- [ ] `dotnet build src/Taskboard.Server/Taskboard.Server.csproj -c Release` passes with 0 warnings and 0 errors.
- [ ] `https://task.afonsoft.dev/login`, `/`, `/github-board`, `/settings`, and `/skills` render correctly in both light and dark themes.
- [ ] Guardrails in section 8 are respected.
- [ ] No PII/tokens appear in logs or markup.

**Next action after DoD is complete:** set `Status` to `Done` in section 0 and open the PR on `feature/devin-20260911-tailwind-theme-refresh`.

## Open Questions / Pending Ambiguity

- N/A. The design tree was settled in the `write-specs` interview.
