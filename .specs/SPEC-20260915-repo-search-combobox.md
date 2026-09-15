# SPEC-20260915-repo-search-combobox

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `repo-search-combobox` |
| Type | `Frontend / Feature` |
| Stack | `.NET 10 / Blazor Server / Blazor.Bootstrap / Octokit` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260915-repo-search-combobox` |
| Ticket | N/A |
| Status | `Done (merged via PR #78)` |

## 1. User Story

**As a** Taskboard user,
**I want** a single searchable repository picker — a dropdown listing every GitHub repository I can access where typing narrows the list — used both in Settings (to pick the skills source repository) and on the Board (to pick the repository whose issues are shown),
**So that** I can locate a repository quickly among dozens of entries instead of scrolling a plain select, and still type an `owner/repo` that is not in the list.

**Problem context:**

- `BoardView.razor` already uses `<input list>` + `<datalist>` for repos: it filters while typing but the datalist UX is limited (no descriptions, inconsistent rendering across browsers, no empty-state hint for free text).
- `SPEC-20260915-skills-repo-sync` adds the config key `Taskboard:Skills:Repository`, which needs a picker in Settings that lists all accessible repos (via `IGitHubService.GetRepositoriesAsync`) and accepts a typed `owner/repo`.
- No reusable searchable-select component exists in `Components/Shared`.

## 2. Scope

**In scope:**

- New reusable `RepositoryCombobox` component (`Components/Shared`): text input + filtered dropdown, keyboard navigation, free-text `owner/repo` fallback.
- Settings → Integrations: new "Skills source repository" field using the component, persisted via `PUT /api/configuration/Taskboard:Skills:Repository` (the existing generic configuration endpoint).
- Board `/`: replace the `input`+`datalist` with `RepositoryCombobox`, preserving existing behavior (auto-load first repo, `owner/repo` validation, warning states, disabled when token missing).

**Out of scope:**

- The sync engine itself (SPEC-20260915-skills-repo-sync owns it).
- Repo pickers on `/github-board` (`RepositorySelector`) — may adopt the component later; not required.
- Listing repos from organizations beyond what `GetRepositoriesAsync` already returns.
- Local-project entries inside the combobox (board stays repo-only, as today).

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Blazor` — new shared component; edits to `Settings.razor` and `BoardView.razor`; styles in `wwwroot` stylesheet already used by the board.
- Server/Contracts — none: the component consumes `IGitHubService` (server-side Blazor) and the existing `GET/PUT /api/configuration` endpoints through `TaskboardClient`.

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Blazor/Components/BoardView.razor`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` (`GetConfigurationEntriesAsync`, `SetConfigurationValueAsync`)
- `src/Taskboard.Application.Contracts/GitHub/IGitHubService.cs` (`RepositoryDto`)
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (key added by the sync SPEC)

**Files to create or modify:**

```text
src/Taskboard.Blazor/Components/Shared/RepositoryCombobox.razor     # NEW
src/Taskboard.Blazor/Components/Shared/RepositoryCombobox.razor.css # NEW (if scoped styles needed)
src/Taskboard.Blazor/Components/Pages/Settings.razor                # MOD — Integrations field
src/Taskboard.Blazor/Components/BoardView.razor                     # MOD — replace datalist
tests/Taskboard.*Tests/...                                          # component tests (bUnit if already used; else unit-testable filter helper)
```

## 4. Requirements

### RF-001: Filtered dropdown

- **Description:** Typing in the input filters the visible list by case-insensitive substring match on `FullName` (matching owner or repo). The dropdown opens on focus/typing and lists all repos when the input is empty. Results are capped (e.g., 50) with a "N more — keep typing" footer.
- **Input → Output:** query string → filtered `IReadOnlyList<RepositoryDto>`.

### RF-002: Selection and free text

- **Description:** Click or `Enter` on a highlighted item sets the value to its `FullName`. When the typed text is a valid `owner/repo` not present in the list and `AllowFreeText` is on, the dropdown offers a "Use 'owner/repo'" row; `Enter` with an exact-format value commits it even with no highlight.
- **Rules:** invalid formats (not `owner/repo`) never commit; the previous valid value is restored and the caller shows its validation message (existing board behavior preserved).

### RF-003: Keyboard and accessibility

- **Description:** `↓`/`↑` move the highlight, `Enter` commits, `Esc` closes and restores the committed value, `Tab` commits the highlight (or free text) and moves focus. ARIA: `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-activedescendant`; the list uses `role="listbox"` / `role="option"`; an `aria-live` region announces result counts.
- **Input → Output:** keyboard events → value/highlight state changes.

### RF-004: Loading, empty and token-missing states

- **Description:** The component exposes `IsLoading`, `Repositories`, `Disabled` parameters. While loading it shows a spinner row; with zero repos and free text allowed it shows "No repositories found — type owner/repo"; `Disabled` renders a read-only input (board token-missing path).

### RF-005: Settings integration — skills source repository

- **Description:** In `Settings.razor` → Integrations section, add a "Skills source repository" `RepositoryCombobox` bound to the effective value of `Taskboard:Skills:Repository` (default `afonsoft/skills`), populated with GitHub repos when a token is configured. Saving calls `SetConfigurationValueAsync("Taskboard:Skills:Repository", value)`; a "Reset" affordance calls `DeleteConfigurationValueAsync`. Save/reset feedback via `ToastService`, consistent with the Configuration table.
- **Rules:** field works without a GitHub token (free text only); validation delegated to the server catalog (`owner/repo` or `https://` URL).

### RF-006: Board integration

- **Description:** Replace `input`+`datalist` in `BoardView.razor` with `RepositoryCombobox`. Preserve: initial selection = first repo alphabetically, commit on change loads `KanbanBoard`, invalid input warning + revert, disabled state when token missing.
- **Rules:** no behavioral regression — the same warning strings and `EmptyState` messages remain.

**Business rules / invariants:**

- One shared component; no per-page forked implementations.
- The component never calls GitHub itself — the caller supplies `Repositories` (keeps it testable and reusable for non-GitHub lists).
- No persistence of last board selection (unchanged from today).

## 5. API Contract

No new endpoints. Reuses:

- `GET /api/configuration` → `{ entries: [...] }` (read effective `Taskboard:Skills:Repository`)
- `PUT /api/configuration/{key}` → `204` | `400` validation | `404` unknown key
- `DELETE /api/configuration/{key}` → `204`
- `IGitHubService.GetRepositoriesAsync()` (server-side injection)

## 6. Acceptance Criteria

- [ ] **Given** the board loads with a token configured **when** the user types `task` **then** only repos containing `task` in `owner/repo` remain in the dropdown.
- [ ] **Given** the dropdown is open **when** the user presses `↓` then `Enter` **then** the highlighted repo is committed and the kanban loads.
- [ ] **Given** a typed `me/private-repo` absent from the list **when** `Enter` is pressed **then** the value commits and the board attempts to load it.
- [ ] **Given** invalid text `just-a-name` **when** committed **then** the existing warning shows and the previous value is restored.
- [ ] **Given** no `GITHUB_TOKEN` **when** the board opens **then** the combobox is disabled and the existing token warning shows.
- [ ] **Given** Settings with a token **when** the user picks `afonsoft/skills` in the skills repo combobox and saves **then** `PUT /api/configuration/Taskboard:Skills:Repository` is called and a success toast appears.
- [ ] **Given** Settings without a token **when** the user types `other/repo` and saves **then** the override persists (free-text path).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| >50 matching repos | type `a` | list capped, "keep typing" footer shown |
| Repo list fetch fails | GitHub error | component shows free-text-only mode; caller keeps existing warning |
| Rapid typing | debounce ~150ms | filter applies to latest query only |
| Click-outside | blur | dropdown closes, committed value kept |
| Empty repo name in list | malformed DTO | filtered out of options |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read context files (section 3); confirm `RepositoryDto` fields and `TaskboardClient` signatures.
- [x] **T2 — Component:** implement `RepositoryCombobox` (input, dropdown, filter, keyboard, ARIA, states).
- [x] **T3 — Board:** swap datalist for the component; verify identical warnings/behavior.
- [x] **T4 — Settings:** add the skills-repo field wired to the configuration endpoints (depends on the `Taskboard:Skills:Repository` catalog key from SPEC-20260915-skills-repo-sync — if not yet merged, implement the catalog key in this branch or guard the UI).
- [x] **T5 — Tests:** filter/validation helper unit tests; bUnit tests for the component if the project already uses bUnit — otherwise keep logic in a testable static/pure helper.
- [ ] **T6 — Validation:** `dotnet build -c Release`, `dotnet test`, manual smoke of both pages (keyboard + free text).
- [ ] **T7 — Done + PR:** set `Status = Done`, open PR on `feature/devin-20260915-repo-search-combobox`.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260915-repo-search-combobox`; never `main`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Security:** no tokens rendered; repo names only.
- **Scope:** no sync logic, no `/github-board` changes, no persistence of last selection.
- **Architecture:** UI-only component; callers own data fetching and persistence.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) verified; edge cases handled.
- [ ] `dotnet build` clean (TreatWarningsAsErrors) and `dotnet test` green.
- [ ] ARIA attributes present; keyboard flow verified manually.
- [ ] Both integrations (Settings + Board) exercised end-to-end.

## Open Questions / Pending Ambiguity

- Resolved: the combobox is shared by Settings (skills source repo) and the Board (repo selector); list comes from `IGitHubService.GetRepositoriesAsync()`; free text allowed in both.
- Ordering note: this SPEC depends on the `Taskboard:Skills:Repository` catalog key defined in `SPEC-20260915-skills-repo-sync`; whichever lands first carries the key, the other rebases.
