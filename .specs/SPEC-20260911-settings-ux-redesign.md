# SPEC-20260911: Settings UX Redesign

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Settings UX Redesign |
| Product / System | agent-harness |
| Module / Bounded Context | Frontend |
| Change type | Design / UI |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/devin-20260911-settings-ux-redesign` |
| Technical owner | afonsoft |
| Status | Completed |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

The current `/settings` page is functional but visually uneven. Cards have inconsistent spacing, the save button floats outside the last card, the agents table is dense and hard to scan, and the page does not establish a clear visual hierarchy on small screens.

### Objective

Redesign the `/settings` page using the installed `design` skill so it is uniform, readable, and accessible. The page must feel like one coherent form with grouped sections, consistent typography, and a clear primary action. The board layout / Tailwind build spec (SPEC-20260911-kanban-smartsheet-layout.md) is the source of truth for tokens; this spec consumes the same token system.

### Design analysis (from `design` skill)

- **Mode:** `Operate` — the user is configuring the product, not being sold to.
- **Mobile-first:** at 375px the page is a single vertical stack. All settings are readable without horizontal scrolling.
- **One memorable idea:** a calm, grouped form with a sticky `Save changes` action bar.
- **Quality floor:** touch targets ≥ 44×44px, body text ≥ 16px, WCAG AA contrast, visible focus states, and reduced-motion support.

### Expected outcome

- Page uses a single page `card` with grouped sections instead of multiple floating cards.
- Consistent `label + input` rhythm and spacing.
- The agents table is converted into a legible list with clear row boundaries and toggle switch.
- Sticky bottom action bar for `Save changes` and `Cancel` (or `Reset`).
- Improved dark-mode readability and accessible color usage.

### Out of scope

- Adding new settings fields.
- Restructuring the settings API or persistence model.
- Changes to the `/skills` page (can be a follow-up).

---

## 2. Agent Role

> Senior Blazor/Tailwind designer and engineer, applying the `design` skill's craft floor.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Do not remove or rename fields in `AgentPreferenceDto`; only add `Description`.
- Do not add new dependencies without approval.
- Do not break the existing Tailwind token system or dark mode.

---

## 4. Product Context

### Functional context

The admin accesses `/settings` after login to manage the theme, GitHub token, and enabled agents. Saving reloads the page so the theme is applied immediately.

### Technical context

- Blazor Server, `Settings.razor`, `site.css`.
- Existing `TaskboardClient` methods: `GetSettingsAsync`, `SaveSettingsAsync`.
- Existing `AgentPreferenceDto` has `Type`, `Name`, `Version`, `Enabled`; this spec adds `Description`.
- MudBlazor is available for `Snackbar` only.

### Relevant stack

- .NET 10 Blazor Server
- Tailwind CSS (build-time, per SPEC-20260911-kanban-smartsheet-layout.md)
- CSS custom properties (`site.css`)
- MudBlazor for `Snackbar` only

---

## 5. Task Definition

### Main task

Redesign `Settings.razor` to be more uniform, readable, and accessible.

### Subtasks

1. Audit `Settings.razor` with the `design` skill craft floor (spacing, type, color, mobile).
2. Restructure the page into grouped sections with clear headings.
3. Refine form labels, inputs, and the dark-mode toggle.
4. Convert the agents table into a scannable list with toggle switches.
5. Add a sticky bottom action bar with `Save changes`.
6. Add loading, empty, and error states.
7. Build and smoke test on `https://task.afonsoft.dev/settings`.

### Do not do

- Do not add new settings fields.
- Do not modify `/skills` in this spec.
- Do not persist the layout of the settings page (collapsible sections, etc.).

---

## 6. Functional Requirements

### FR-001: Page Layout & Hierarchy

**Description:**
The page uses a single containing card with grouped sections instead of multiple disjoint cards.

**Structure:**

```text
Page
├── Page title + subtitle
├── Settings card
│   ├── Section: Appearance
│   ├── Section: Integrations
│   └── Section: Agents
└── Sticky action bar
    ├── Primary: Save changes
    └── Secondary: Reset
```

**Behavior:**
- The containing card has a `card` class with consistent padding (`p-6` on large, `p-4` on small).
- Section titles are `text-base font-semibold text-text` with a top border separator between sections (`border-t border-border pt-6`).
- Spacing between sections is `24px` (3× base grid).

### FR-002: Appearance Section

**Description:**
Theme and visual preferences grouped together.

**Fields:**
- **Dark mode** — a labeled toggle switch (not a native checkbox).
- **GitHub token** — a password input with a visible label, helper text, and a reveal/hide button.

**Behavior:**
- The toggle uses a custom switch component or a styled checkbox.
- The token input has a label `GitHub token`, helper `Paste a personal access token with repo scope.` and placeholder `ghp_…`.
- The token input uses `type="password"` by default; the reveal button toggles to `type="text"`.

### FR-003: Integrations Section

**Description:**
Placeholder for future integration toggles; currently not required. If not used, this section is omitted and GitHub token lives in `Appearance` or its own `Integrations` section.

**Decision:** GitHub token remains under `Integrations` section.

**Fields:**
- **GitHub token** (moved here from `Appearance`).

### FR-004: Agents Section

**Description:**
Agents are listed in a clean, scannable way with an enable/disable switch per agent.

**Layout:**
- Each agent is a row with:
  - Name on the first line, in `text-text font-medium`.
  - Short `Description` in `text-text-muted text-sm` on the second line.
  - `Version` and `ExecutablePath` (if available) as extra metadata, also muted.
  - A toggle switch on the right.
- Rows are separated by `border-b border-border`.
- Hover/focus background is `--color-surface-elevated`.

**Data:**
- Add `string? Description` to `AgentInfo` and `AgentPreferenceDto`.
- `AgentDiscoveryService` maps `AgentType` to a short description (e.g. `devin` → "Devin CLI for agentic coding", `claude` → "Claude Code integration").

**Behavior:**
- Loading shows `Loading` component.
- Empty list shows `EmptyState` with action `Refresh` (optional).
- Each toggle immediately updates the local `_enabled` set; save only on `Save changes`.

### FR-005: Sticky Action Bar

**Description:**
The primary action is always visible without scrolling.

**Layout:**
- Fixed/sticky at the bottom of the viewport on mobile, or at the bottom of the card on large screens.
- Contains:
  - `Save changes` — primary button.
  - `Reset` — secondary/ghost button (optional; reverts unsaved local changes).

**Behavior:**
- Save uses the existing `SaveAsync` flow (snackbar + full reload).
- Reset restores the form to the values loaded in `OnInitializedAsync`.
- On small screens, the bar has a top shadow for separation.

### FR-006: Responsive & Accessible

**Description:**
The settings page is usable on mobile and keyboard.

**Behavior:**
- All touch targets ≥ 44×44px.
- Inputs have `id`/`for` associations.
- Focus rings are visible.
- Sections stack vertically on all breakpoints.
- Reduced motion: disable transitions if `prefers-reduced-motion` is set.

---

## 7. Business Rules

- Saving always persists the full `SaveSettingsRequest`.
- The form does not auto-save; the user must press `Save changes`.
- The `Reset` button reverts to the last loaded server state, not the saved-on-disk state.
- Dark mode preview does not require a save; the toggle updates the UI immediately but only persists on save.

---

## 8. Domain Modeling

None. This is a pure UI refresh.

---

## 9. Expected Architecture

```text
src/Taskboard.Blazor/
  Components/Pages/
    Settings.razor           # redesigned page
  Components/Shared/
    ToggleSwitch.razor       # optional reusable toggle
    FormSection.razor        # optional section wrapper
    StickyActionBar.razor    # optional bottom action bar

src/Taskboard.Server/wwwroot/css/site.css
  # new token classes if needed: .switch, .form-section, .setting-row
```

---

## 10. API Contracts

No API contract changes.

---

## 11. Application Contracts

```csharp
public sealed record AgentInfo(
    string Name,
    string ExecutablePath,
    AgentType Type,
    AgentStatus Status,
    string? Version,
    string? Description);

public sealed record AgentPreferenceDto(
    AgentType Type,
    string Name,
    string? ExecutablePath,
    string? Version,
    bool Enabled,
    string? Description);
```

---

## 12. Persistence and Data

No persistence changes. The page consumes `SettingsDto` and `AgentPreferenceDto` as before.

---

## 13. Integrations

None.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Load error | `GetSettingsAsync` throws | Show `EmptyState` or inline error with retry. |
| No agents | empty `_agents` | Show `EmptyState` with message. |
| Save error | `SaveSettingsAsync` throws | `Snackbar` error message. |
| Reset | user clicks `Reset` | Reverts to original loaded values. |
| Dark mode toggle | user toggles | UI theme updates immediately, but persist only on save. |

---

## 15. Few-Shot Examples

```razor
<!-- Section concept -->
<div class="form-section">
    <h2 class="form-section-title">Appearance</h2>
    <label class="form-label" for="theme-toggle">Dark mode</label>
    <div class="setting-row">
        <span class="text-text">Use dark theme</span>
        <ToggleSwitch id="theme-toggle" @bind-Value="_isDark" />
    </div>
</div>

<div class="form-section">
    <h2 class="form-section-title">Integrations</h2>
    <label class="form-label" for="github-token">GitHub token</label>
    <p class="form-helper">Paste a personal access token with repo scope.</p>
    <div class="setting-row-input">
        <input id="github-token" type="password" class="input" @bind="_gitHubToken" placeholder="ghp_…" />
        <button type="button" class="btn-secondary" @onclick="ToggleTokenVisibility">Show</button>
    </div>
</div>

<!-- Agent row concept -->
<div class="setting-row">
    <div>
        <div class="text-text font-medium">@agent.Name</div>
        <div class="text-text-muted text-sm">@agent.Description</div>
        <div class="text-text-muted text-xs">@agent.Type · v@(agent.Version)</div>
    </div>
    <ToggleSwitch @bind-Value="_enabledState[agent.Type]" />
</div>

<!-- Sticky action bar -->
<div class="action-bar">
    <button type="button" class="btn-secondary" @onclick="ResetAsync">Reset</button>
    <button type="button" class="btn-primary" @onclick="SaveAsync">Save changes</button>
</div>
```

---

## 16. Non-Functional Requirements

- WCAG AA contrast for all interactive and body text.
- Page usable at 375px without horizontal scroll.
- No layout shift when toggles switch.
- Builds with `TreatWarningsAsErrors`.

---

## 17. Mandatory Guardrails

- Do not change the `SaveSettingsRequest` shape or the settings API.
- Do not log the GitHub token.
- Do not expose the token in the UI beyond the masked input.
- Do not add new dependencies.
- Do not break the existing Tailwind token system.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Page renders | `/settings` loads without 500. |
| Theme toggle | Toggle updates the page immediately. |
| Agent toggles | Toggle rows update local state. |
| Save | `Save changes` persists and reloads. |
| Reset | `Reset` reverts to last loaded state. |
| Mobile | All controls stack, no horizontal scroll. |
| Smoke | `https://task.afonsoft.dev/settings` loads. |

---

## 19. Acceptance Criteria

- [ ] `/settings` uses a single grouped card layout.
- [ ] Appearance and Integrations sections are clearly separated.
- [ ] Agents are shown as a scannable list with toggle switches.
- [ ] A sticky action bar contains `Save changes` and `Reset`.
- [ ] The page is responsive and accessible (375px+).
- [ ] Build compiles with `TreatWarningsAsErrors`.
- [ ] Integration and smoke tests pass.

---

## 20. Implementation Plan

1. Review `Settings.razor` against the `design` skill craft floor.
2. Add `string? Description` to `AgentInfo` and `AgentPreferenceDto`; map descriptions in `AgentDiscoveryService`.
3. Add any missing CSS token classes to `site.css` (`form-section`, `form-label`, `form-helper`, `setting-row`, `action-bar`).
4. Create or update `Settings.razor` with the new grouped structure.
5. Optionally create `ToggleSwitch.razor` if not available from Tailwind/MudBlazor.
6. Add `Reset` logic to restore original loaded state.
7. Build and run unit/integration tests.
8. Smoke test on `https://task.afonsoft.dev/settings` in light and dark mode.

---

## 21. Rollback Strategy

- Revert `Settings.razor` and `site.css` to the previous Tailwind state.
- Remove any new components (`ToggleSwitch`, `FormSection`, `StickyActionBar`) if they were extracted.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Custom toggle not accessible | Médio | Média | Use native checkbox with a styled wrapper; keep `input` focusable. |
| Sticky bar covers content on mobile | Médio | Média | Add bottom padding to the page equal to bar height. |
| Reload after save causes state loss | Baixo | Baixa | Keep current reload behavior; not in scope to change. |

---

## 23. Definition of Done

- [ ] SPEC approved.
- [ ] `Settings.razor` refactored with grouped sections and sticky action bar.
- [ ] Agents list uses scannable rows with toggles.
- [ ] Responsive and dark-mode verified.
- [ ] Build and tests pass.
- [ ] Smoke test on `https://task.afonsoft.dev/settings` passes.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Should `GitHub token` live under `Integrations` or `Appearance`? (Proposed: `Integrations`.)
2. Is a `Reset` button required, or only `Save changes`? (Proposed: keep `Reset` as secondary.)
3. Should the agent list show a short description? (Resolved: yes — add `Description` to `AgentInfo` and `AgentPreferenceDto`, sourced from the agent type.)

## Human Approval Checklist

- [ ] New layout direction approved.
- [ ] Section grouping approved.
- [ ] Agent list vs table approved.
- [ ] Sticky action bar approved.
