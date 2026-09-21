# SPEC-20260911: Kanban Smartsheet-Style Layout & Colors

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Kanban Smartsheet-Style Layout & Colors |
| Product / System | agent-harness |
| Module / Bounded Context | Frontend |
| Change type | Design / UI |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/devin-20260911-kanban-smartsheet-layout` |
| Technical owner | afonsoft |
| Status | Completed |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

The current primary board is functional but visually plain. It does not use color to convey status and priority at a glance, and it lacks the information-dense card layout and header of a typical project task board (e.g. Smartsheet-style).

### Objective

Redesign the primary task board (`BoardView` and `TaskCard`) to follow a Smartsheet-style kanban: colored column headers by status, color-coded cards by priority, and an informative board header with project metadata and a priority legend.

### Visual references

- Smartsheet simple project board: pastel column headers, colored cards, project header with `Project Name`, `Start Date`, `End Date`, `Duration in Days`, and a `Priority Level` legend.
- Generic kanban board: columns with distinct header colors and cards using soft priority backgrounds.

### Expected outcome

- `BoardView` displays a board header with project name, date range, duration and priority legend.
- Kanban columns have distinct pastel header colors mapped to `TaskStatus`.
- Task cards use soft background colors mapped to `TaskPriority`.
- `TaskCard` shows richer metadata: title, description snippet, assignee, due date and priority.
- Both light and dark themes remain consistent and accessible.

### Out of scope

- GitHub `KanbanBoard` layout changes (can be addressed in a follow-up).
- Adding or removing task fields (the UI shows existing fields; hidden when null).
- Drag-and-drop behavior changes.

---

## 2. Agent Role

> Senior Blazor/Tailwind frontend designer and engineer.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Do not change API contracts.
- Do not add new dependencies without approval.
- Do not break the existing Tailwind token system or dark mode.

---

## 4. Product Context

### Functional context

The admin logs in and sees the primary task board. The board groups tasks by status. Each card must communicate priority through color and include the most useful metadata without opening the detail view.

### Technical context

- Blazor Server, `BoardView.razor`, `TaskCard.razor`, `site.css`.
- Tailwind CSS must be built as a PostCSS CLI step and emitted to `wwwroot/css/tailwind.css`; the `cdn.tailwindcss.com` script must be removed from `App.razor`.
- CSS custom properties for light/dark tokens live in `site.css`.

### Relevant stack

- .NET 10 Blazor Server
- Tailwind CSS v3/v4 via PostCSS/Tailwind CLI
- Node.js 20+ / npm
- CSS custom properties (`site.css`)
- MudBlazor for dialogs/snackbar only

---

## 5. Task Definition

### Main task

Redesign the primary board UI with a Smartsheet-style kanban layout and color scheme.

### Subtasks

1. Remove Tailwind CSS CDN from `App.razor` and add local build pipeline (`package.json`, `tailwind.config.js`, `postcss.config.js`, `input.css`, MSBuild pre-build target).
2. Add CSS design tokens for status header and priority card colors.
3. Create/update `BoardHeader.razor` component.
4. Refactor `BoardView.razor` with colored column headers.
5. Refactor `TaskCard.razor` with priority-colored backgrounds and richer metadata.
6. Ensure responsive behavior and dark-mode contrast.
7. Build and smoke test on `https://task.afonsoft.dev`.

### Do not do

- Do not modify the GitHub board in this spec.
- Do not introduce new fonts or icon libraries.
- Do not persist layout preferences to settings in this spec.
- Do not keep `cdn.tailwindcss.com` in `App.razor`.

---

## 6. Functional Requirements

### FR-001: Board Header

**Description:**
Display a compact header above the kanban board with project metadata and a priority legend.

**Elements:**
- `Project Name` label and value (placeholder if no project is selected; can show the default `Local` project or `All Projects`).
- `Start Date` (earliest `StartDate` of visible tasks, or `—` if none).
- `End Date` (latest `DueDate` of visible tasks, or `—` if none).
- `Duration in Days` (computed from start and end, or `—`).
- `Priority Level` legend: colored swatches for each priority.

**Behavior:**
- Responsive: on small screens the header fields stack vertically.
- Use existing `.card` or `.kanban-board-header` token styles.

### FR-002: Status Column Headers

**Description:**
Each kanban column has a colored header that maps to the status it represents.

**Color mapping (light mode):**

| Status | Header background | Header text |
|---|---|---|
| Not Started | `--status-not-started-bg` (pastel pink) | dark text |
| In Progress | `--status-in-progress-bg` (pastel yellow) | dark text |
| Delayed | `--status-delayed-bg` (pastel cyan) | dark text |
| On Hold | `--status-on-hold-bg` (pastel gray) | dark text |
| Completed | `--status-completed-bg` (pastel green) | dark text |
| Archived | `--status-archived-bg` (pastel slate) | dark text |

**Dark mode:**
- Pastel colors are desaturated/tinted for the dark surface so text remains readable.
- Text uses a high-contrast color (dark text on pastel, or light text on desaturated dark pastel).

### FR-003: Priority Card Colors

**Description:**
Each task card has a soft background color based on `TaskPriority`.

**Color mapping (light mode):**

| Priority | Card background | Accent / left border |
|---|---|---|
| Urgent | deep pastel red | darker red border |
| High | pastel pink | pink border |
| Medium | pastel yellow | yellow border |
| Low | pastel cyan | cyan border |
| None | neutral surface | default border |

**Dark mode:**
- Use muted versions of the same hues so they do not glow against the dark board.
- Text remains accessible (WCAG AA 4.5:1 for body text).

### FR-004: Rich Task Card

**Description:**
`TaskCard` displays enough information for the user to scan a task without opening it.

**Fields shown (when present):**
- `Title` (bold, one line, truncate with ellipsis).
- `Description` snippet (max 2 lines, gray/muted).
- `Assigned To` — `Assignee.Name` or `—`.
- `Date Due` — `DueDate` formatted `MM/dd/yyyy` or `—`.
- `Priority` — label with color swatch.

**Interaction:**
- Click opens the existing task detail dialog.
- Card is focusable and keyboard accessible.

### FR-005: Responsive & Accessible

**Description:**
The board works from mobile to desktop without horizontal clipping.

**Behavior:**
- Mobile: columns stack vertically or the board becomes horizontally scrollable with `snap-x`.
- Touch targets ≥ 44×44 dp.
- Color is not the only indicator: status/priority text labels always visible.
- Focus ring visible for keyboard navigation.

### FR-006: Build-Time Tailwind CSS

**Description:**
Tailwind CSS is generated at build time instead of loaded from `cdn.tailwindcss.com`. This removes the production warning and makes the build hermetic and offline-capable.

**Build artifacts:**
- `src/Taskboard.Server/package.json` with `tailwindcss`, `postcss`, `autoprefixer` as dev dependencies.
- `src/Taskboard.Server/tailwind.config.js` with:
  - `content` pointing to `../Taskboard.Blazor/**/*.{razor,html,cshtml}` and the local `**/*.razor`.
  - `darkMode: 'class'`.
  - `theme.extend.colors` for `primary`, `surface`, `status-*` and `priority-*` tokens (mapped to CSS variables where possible).
- `src/Taskboard.Server/wwwroot/css/tailwind.input.css` with `@tailwind base; @tailwind components; @tailwind utilities;` and `@import` of `site.css` (or `site.css` remains a separate link in `App.razor`).
- `src/Taskboard.Server/wwwroot/css/tailwind.css` generated by `npx tailwindcss -i wwwroot/css/tailwind.input.css -o wwwroot/css/tailwind.css`.
- `src/Taskboard.Server/Taskboard.Server.csproj` runs `npm install` and `npm run build:css` in a `BeforeBuild` target.

**Behavior:**
- `App.razor` removes `<script src="https://cdn.tailwindcss.com"></script>` and replaces it with `<link rel="stylesheet" href="css/tailwind.css" />`.
- `dotnet build` re-runs the Tailwind CLI and emits the updated CSS.
- The generated `tailwind.css` is ignored in `.gitignore`; only the source files (`tailwind.input.css`, `tailwind.config.js`, `package.json`) are committed.

---

## 7. Business Rules

- Status and priority colors are derived from their value objects, not from user preference.
- The board header is read-only; editing metadata happens in the existing task detail dialog.
- Dark-mode colors must be chosen from the same token set, not a separate hardcoded palette.

---

## 8. Domain Modeling

None. This is a pure UI refresh.

---

## 9. Expected Architecture

```text
src/Taskboard.Server/
  package.json              # dev dependencies: tailwindcss, postcss, autoprefixer
  tailwind.config.js        # content paths, darkMode, custom colors
  postcss.config.js         # postcss setup
  Taskboard.Server.csproj   # BeforeBuild target for npm install + build:css
  wwwroot/css/
    tailwind.input.css      # @tailwind directives (+ optional @import site.css)
    tailwind.css            # generated, ignored in git
    site.css                # existing custom properties and component classes
  wwwroot/_content/...      # MudBlazor static assets

src/Taskboard.Blazor/
  App.razor                 # remove CDN tailwind, link local tailwind.css
  Components/
    BoardView.razor         # board header + colored columns
    TaskCard.razor          # colored cards with metadata
    Shared/
      PriorityLegend.razor  # optional legend component
```

---

## 10. API Contracts

No API contract changes.

---

## 11. Application Contracts

No application contract changes.

---

## 12. Persistence and Data

No persistence changes. The UI consumes existing task DTOs.

---

## 13. Integrations

None.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| No tasks in a column | empty status | Column body shows `EmptyState` or `+` drop target. |
| No `DueDate` | `DueDate` is null | Shows `—` for date due. |
| No `Assignee` | `Assignee` is null | Shows `—` for assigned to. |
| Unknown priority | value not in mapping | Falls back to `None` color. |
| Dark mode active | `data-theme="dark"` | Uses dark-mode pastel tokens. |
| Long title/description | > 1-2 lines | Truncated with `text-ellipsis` and `line-clamp`. |

---

## 15. Few-Shot Examples

```css
:root {
  --status-not-started-bg: #ffd1dc;
  --status-in-progress-bg: #ffeaa7;
  --status-delayed-bg: #a8e6cf;
  --status-on-hold-bg: #dfe6e9;
  --status-completed-bg: #b8e994;

  --priority-urgent-bg: #fab1a0;
  --priority-high-bg: #ffcccc;
  --priority-medium-bg: #ffeaa7;
  --priority-low-bg: #d3f3f3;
}

[data-theme="dark"] {
  --status-not-started-bg: #6b3b46;
  --status-in-progress-bg: #6b5b2e;
  --status-delayed-bg: #2e5c4d;
  --status-on-hold-bg: #4a5054;
  --status-completed-bg: #4a6b3b;

  --priority-urgent-bg: #5c3b36;
  --priority-high-bg: #5c3b3b;
  --priority-medium-bg: #5c4e2e;
  --priority-low-bg: #2e4f4f;
}
```

```razor
<!-- Board header concept -->
<div class="kanban-board-header">
    <div>
        <label class="text-text-muted text-xs">Project Name</label>
        <p class="text-text font-semibold">@ProjectName</p>
    </div>
    <div>
        <label class="text-text-muted text-xs">Start Date</label>
        <p class="text-text">@StartDate</p>
    </div>
    <div>
        <label class="text-text-muted text-xs">End Date</label>
        <p class="text-text">@EndDate</p>
    </div>
    <div>
        <label class="text-text-muted text-xs">Duration in Days</label>
        <p class="text-text">@DurationDays</p>
    </div>
    <PriorityLegend />
</div>
```

---

## 16. Non-Functional Requirements

- WCAG AA contrast for all text on colored backgrounds.
- No layout shift on theme toggle.
- Virtualized list keeps initial render fast.
- Builds with `TreatWarningsAsErrors`.

---

## 17. Mandatory Guardrails

- Keep existing `Virtualize` and `onclick` behavior.
- Do not hardcode colors in components; use CSS tokens.
- Do not add external icon fonts.
- Do not regress the GitHub board.
- Do not load Tailwind from `cdn.tailwindcss.com` in production.
- Do not commit generated `wwwroot/css/tailwind.css` to the repository.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Tailwind build | `dotnet build` generates `wwwroot/css/tailwind.css`. |
| No CDN | `cdn.tailwindcss.com` is not requested by `App.razor`. |
| Board renders | `BoardView` loads without 500. |
| Status colors | Column header backgrounds map to status tokens. |
| Priority colors | Card backgrounds map to priority tokens. |
| Dark mode | Colors switch with `data-theme="dark"`. |
| Mobile layout | Header stacks, cards remain readable. |
| Smoke | `https://task.afonsoft.dev/` loads. |

---

## 19. Acceptance Criteria

- [ ] `cdn.tailwindcss.com` is removed and `wwwroot/css/tailwind.css` is generated at build time.
- [ ] `BoardView` shows a board header with project metadata and priority legend.
- [ ] Kanban columns have colored headers per status.
- [ ] Task cards have priority-colored backgrounds and show title, description, assignee, due date and priority.
- [ ] Both light and dark themes are visually consistent and accessible.
- [ ] Build compiles with `TreatWarningsAsErrors`.
- [ ] Integration and smoke tests pass.

---

## 20. Implementation Plan

1. Remove Tailwind CDN from `App.razor`.
2. Add `package.json`, `tailwind.config.js`, `postcss.config.js` and `tailwind.input.css` to `Taskboard.Server`.
3. Add MSBuild `BeforeBuild` target to run `npm install` and `npm run build:css`.
4. Verify `wwwroot/css/tailwind.css` is generated and served correctly; add it to `.gitignore`.
5. Add CSS custom properties for status and priority colors to `site.css`.
6. Refactor `BoardView.razor` to include a board header and colored column headers.
7. Refactor `TaskCard.razor` to new layout and metadata.
8. Optionally extract `PriorityLegend.razor` shared component.
9. Update `site.css` with component classes (`.kanban-column-header`, `.task-card`, `.priority-*`).
10. Build and run unit/integration tests.
11. Smoke test on `https://task.afonsoft.dev/` in light and dark mode.

---

## 21. Rollback Strategy

- Revert `BoardView.razor`, `TaskCard.razor` and `site.css` to the previous Tailwind state.
- Keep the original component backups if needed.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Pastel colors fail contrast in dark mode | Médio | Média | Provide dark tokens and test with a contrast checker. |
| Header adds clutter on small screens | Médio | Média | Stack header fields and hide legend behind a toggle on `small`. |
| GitHub board indirectly affected | Baixo | Baixa | Keep changes scoped to `BoardView`/`TaskCard`. |

---

## 23. Definition of Done

- [ ] SPEC approved.
- [ ] Tailwind CDN removed from `App.razor`.
- [ ] Local Tailwind build pipeline added and `wwwroot/css/tailwind.css` is generated on `dotnet build`.
- [ ] CSS tokens for status and priority colors added.
- [ ] `BoardView` and `TaskCard` refactored.
- [ ] Responsive and dark-mode verified.
- [ ] Build and tests pass.
- [ ] Smoke test on `https://task.afonsoft.dev` passes.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Should the GitHub `KanbanBoard` also adopt this layout, or only the primary board? (Proposed: only primary board; GitHub as follow-up.)
2. Should the header show a real selected project name or a placeholder? (Proposed: show the default/selected project name; placeholder if none.)
3. Should `Urgent` use the deep red from the example, or a different hue? (Proposed: deep red to contrast with `High` pink.)

## Human Approval Checklist

- [ ] Color mapping for statuses and priorities approved.
- [ ] Board header content approved.
- [ ] Scope (primary board only) approved.
