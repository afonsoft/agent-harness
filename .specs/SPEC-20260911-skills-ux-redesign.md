# SPEC-20260911: Skills UX Redesign

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Skills UX Redesign |
| Product / System | agent-harness |
| Module / Bounded Context | Frontend |
| Change type | Design / UI |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/devin-20260911-skills-ux-redesign` |
| Technical owner | afonsoft |
| Status | Completed |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

The current `/skills` page displays skills in a plain table with a single search input. The table feels dense, the source and path columns are hard to scan, and the page does not take advantage of the available metadata (`Name`, `Description`, `Source`, `Path`).

### Objective

Redesign `/skills` using the installed `design` skill so skills are presented as a clean, scannable card grid with source badges, clear typography, and responsive filtering. The page must feel uniform with the rest of the app and be easy to read on both mobile and desktop.

### Design analysis (from `design` skill)

- **Mode:** `Operate` — the user is browsing and searching the installed skills catalog.
- **Mobile-first:** at 375px the page shows one skill card per row, a search bar at the top, and a single filter bar.
- **One memorable idea:** a card grid where the `Source` is a color-coded badge and the file `Path` is visually separated from the description.
- **Quality floor:** touch targets ≥ 44×44px, body text ≥ 16px, WCAG AA contrast, visible focus states, and reduced-motion support.

### Expected outcome

- Skills are displayed as a responsive card grid (1 / 2 / 3 columns by breakpoint).
- Each card shows `Name`, `Description`, `Source` badge, and `Path`.
- A search input and optional source filter chips sit in a compact filter bar.
- Loading, empty, and error states are styled with existing components.
- The page is consistent with the settings and board design refresh.

### Out of scope

- Adding new skill metadata fields.
- Bulk actions on skills.
- Reordering or drag-and-drop.

---

## 2. Agent Role

> Senior Blazor/Tailwind designer and engineer, applying the `design` skill's craft floor.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Do not change API contracts or DTOs.
- Do not add new dependencies without approval.
- Do not break the existing Tailwind token system or dark mode.

---

## 4. Product Context

### Functional context

The admin accesses `/skills` to see which agent skills are installed. They can search by name, description, or source. Skills are discovered from directories like `~/.claude/skills`, `~/.devin/skills`, etc.

### Technical context

- Blazor Server, `Skills.razor`, `site.css`.
- Existing `TaskboardClient.GetSkillsAsync()` returns `IReadOnlyList<SkillDto>`.
- `SkillDto` has `Name`, `Description`, `Source`, `Path`.
- MudBlazor is available for `Snackbar` only.

### Relevant stack

- .NET 10 Blazor Server
- Tailwind CSS (build-time, per SPEC-20260911-kanban-smartsheet-layout.md)
- CSS custom properties (`site.css`)
- MudBlazor for `Snackbar` only

---

## 5. Task Definition

### Main task

Redesign `Skills.razor` to present skills as a readable, responsive card grid.

### Subtasks

1. Audit `Skills.razor` with the `design` skill craft floor.
2. Add CSS token classes for skill cards, source badges, and filter bar.
3. Refactor `Skills.razor` with a filter bar and card grid.
4. Add a loading skeleton or `Loading` component and `EmptyState`.
5. Build and smoke test on `https://task.afonsoft.dev/skills`.

### Do not do

- Do not add new skill fields.
- Do not modify the skill discovery algorithm; only add a detail lookup method.
- Do not add bulk selection or actions.
- Do not add a full markdown renderer; display plain text blocks in the dialog.

---

## 6. Functional Requirements

### FR-001: Page Header

**Description:**
A clear page title, subtitle, and total skill count.

**Elements:**
- `Skills` title (`text-2xl font-bold text-text`).
- `Installed agent skills` subtitle (`text-sm text-text-muted`).
- Total count badge (e.g. `12 skills`).

### FR-002: Filter Bar

**Description:**
A compact, always-visible filter bar for search and source filtering.

**Elements:**
- Search input (`type="search"`) with placeholder `Search by name, description or source`.
- Source filter chips (e.g. `All`, `claude`, `devin`, `cursor`, `opencode`, `taskboard`) when more than one source exists.
- Active chip is highlighted.

**Behavior:**
- Search filters by `Name`, `Description`, and `Source` (case-insensitive).
- Selecting a source chip filters cards to that source; `All` clears the source filter.
- Search and source filters are combined with AND.
- On mobile, chips wrap to a second line.

### FR-003: Skill Card Grid

**Description:**
Skills are shown as cards in a responsive grid.

**Layout:**
- `small` (0–639px): 1 card per row.
- `medium` (640–1023px): 2 cards per row.
- `large` (1024px+): 3 cards per row.

**Card structure:**
- `Name` — `text-base font-semibold text-text`, one line, truncated.
- `Source` badge — small, color-coded pill (e.g. `claude` = purple, `devin` = indigo, `cursor` = orange, `opencode` = green, `taskboard` = primary).
- `Description` — `text-sm text-text-muted`, 2–3 lines, truncated with `line-clamp`.
- `Path` — `text-xs text-text-muted font-mono`, truncated, with a `title` tooltip on hover.

**Interaction:**
- Card is focusable and keyboard accessible.
- Card has a subtle hover/focus background (`--color-surface-elevated`).
- Click or `Enter` on a focused card opens the skill detail dialog.

### FR-004: Source Badge Colors

**Description:**
Each source has a distinct badge color for quick scanning.

**Color mapping (light and dark modes):**

| Source | Badge background (light) | Badge text | Dark mode adjustment |
|---|---|---|---|
| `claude` | light purple | dark purple | desaturated purple |
| `devin` | light indigo | dark indigo | desaturated indigo |
| `cursor` | light orange | dark orange | desaturated orange |
| `opencode` | light green | dark green | desaturated green |
| `taskboard` | `--color-primary` muted | `--color-text` | muted primary |
| unknown | `--color-surface-elevated` | `--color-text-muted` | muted surface |

### FR-005: Loading, Empty and Error States

**Description:**
The page handles loading, empty, and error states clearly.

**Behavior:**
- Loading: show `Loading` or a 3-card skeleton grid.
- No skills at all: show `EmptyState` with `No skills installed`.
- Filtered to zero: show `EmptyState` with `No skills match your filters` and a `Clear filters` button.
- Load error: show `EmptyState` or inline alert with `Retry`.

### FR-006: Responsive & Accessible

**Description:**
The page is usable on mobile and keyboard.

**Behavior:**
- Touch targets ≥ 44×44px.
- Search input has an accessible `label` (visually hidden if needed).
- Focus rings visible on search, chips, and cards.
- Reduced motion: disable card hover transitions if `prefers-reduced-motion` is set.

### FR-007: Skill Detail Dialog

**Description:**
Clicking a skill card opens a MudBlazor dialog that displays the full skill details, including `References` and `Scripts` sections from the `SKILL.md` file when they exist.

**Data contract:**

```csharp
public sealed record SkillDetailDto(
    string Name,
    string Description,
    string Source,
    string Path,
    IReadOnlyList<string> Tools,
    string? References,
    string? Scripts,
    string Content);
```

**API endpoint:**

```http
GET /api/skills/{source}/{name}
```

**Behavior:**
- The endpoint looks up the skill by `source` and `name`, reads `Path/SKILL.md`, and parses the frontmatter and markdown.
- `Tools` comes from the YAML frontmatter `tools` list.
- `References` is the content under the `## References` heading (case-insensitive) if it exists.
- `Scripts` is the content under the `## Scripts` heading (case-insensitive) if it exists.
- `Content` is the full markdown body after the frontmatter.
- If the skill is not found or the file is missing, the endpoint returns `404 Not Found`.

**Dialog layout:**
- Title: `Skill.Name` with `SourceBadge`.
- Description and path at the top.
- `Tools` as a list of badges or inline text.
- `References` section, collapsed if empty.
- `Scripts` section, collapsed if empty.
- `Content` in a scrollable `<pre>` or formatted block.
- Close button at the bottom.

---

## 7. Business Rules

- Search is client-side and case-insensitive.
- Source badges are derived from `SkillDto.Source`; unknown sources fall back to a neutral badge.
- The path is not clickable; it is purely informational.

---

## 8. Domain Modeling

None. This is a pure UI refresh.

---

## 9. Expected Architecture

```text
src/Taskboard.Blazor/
  Components/Pages/
    Skills.razor              # redesigned page
  Components/Shared/
    SkillCard.razor           # optional card component
    SourceBadge.razor         # optional badge component
    FilterChip.razor          # optional filter chip component
    SkillDetailDialog.razor   # MudBlazor dialog component

src/Taskboard.Server/
  Program.cs                 # /api/skills/{source}/{name} endpoint
  wwwroot/css/site.css       # new token classes: .skill-card, .skill-grid, .source-badge, .filter-bar

src/Taskboard.Integrations/Skills/
  SkillDiscoveryService.cs   # GetDetailAsync method

src/Taskboard.Application.Contracts/Skills/
  SkillDetailDto.cs          # detail DTO
```

---

## 10. API Contracts

### Skill detail

```http
GET /api/skills/{source}/{name}
```

**Response `200 OK`:**

```json
{
  "name": "manage-taskboard",
  "description": "Gerencie o Dashi Taskboard...",
  "source": "taskboard",
  "path": "/home/ubuntu/repos/agent-harness/skills/manage-taskboard",
  "tools": ["Bash", "Read", "Edit", "Write"],
  "references": "## Contexto\n\n...",
  "scripts": null,
  "content": "## Contexto\n\n..."
}
```

**Response `404 Not Found`:**

```json
{
  "type": "https://taskboard.ai/errors",
  "title": "Not Found",
  "status": 404,
  "detail": "Skill not found."
}
```

---

## 11. Application Contracts

```csharp
public sealed record SkillDto(string Name, string Description, string Source, string Path);

public sealed record SkillDetailDto(
    string Name,
    string Description,
    string Source,
    string Path,
    IReadOnlyList<string> Tools,
    string? References,
    string? Scripts,
    string Content);

public interface ISkillDiscoveryService
{
    Task<IReadOnlyList<SkillDto>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<SkillDetailDto?> GetDetailAsync(string source, string name, CancellationToken cancellationToken = default);
}
```

---

## 12. Persistence and Data

No persistence changes. The UI consumes existing `SkillDto`.

---

## 13. Integrations

None.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| No skills installed | empty list | Show `EmptyState` with message. |
| One source only | all skills from `taskboard` | Hide source filter chips or show a single disabled chip. |
| Unknown source | `Source` not in mapping | Neutral badge. |
| Long description | > 3 lines | Truncated with `line-clamp-3`. |
| Long path | > 1 line | Truncated with `text-ellipsis`; tooltip on hover. |
| No search results | filter returns empty | Show `EmptyState` with `Clear filters` button. |
| Load error | `GetSkillsAsync` throws | Show inline alert or `EmptyState` with retry. |
| Skill detail not found | `source` or `name` unknown | Endpoint returns `404 Not Found`. |
| Missing references or scripts | heading not in `SKILL.md` | Dialog hides the corresponding section. |

---

## 15. Few-Shot Examples

```razor
<!-- Filter bar concept -->
<div class="filter-bar mb-6">
    <label class="visually-hidden" for="skills-search">Search skills</label>
    <input id="skills-search" type="search" class="input" placeholder="Search by name, description or source" @bind="_search" @oninput="OnSearchInput" />
    <div class="flex flex-wrap gap-2">
        <button class="filter-chip @(string.IsNullOrEmpty(_selectedSource) ? "filter-chip-active" : "")" @onclick="() => SelectSource(null)">All</button>
        @foreach (var source in _sources)
        {
            <button class="filter-chip @(_selectedSource == source ? "filter-chip-active" : "")" @onclick="() => SelectSource(source)">@source</button>
        }
    </div>
</div>

<!-- Card grid concept -->
<div class="skill-grid">
    @foreach (var skill in _filteredSkills)
    {
        <SkillCard Skill="skill" />
    }
</div>

<!-- Skill card concept -->
<div class="skill-card">
    <div class="flex items-start justify-between gap-3">
        <h3 class="skill-card-title" title="@skill.Name">@skill.Name</h3>
        <SourceBadge Source="@skill.Source" />
    </div>
    <p class="skill-card-description">@skill.Description</p>
    <p class="skill-card-path" title="@skill.Path">@skill.Path</p>
</div>
```

---

## 16. Non-Functional Requirements

- WCAG AA contrast for badge text and card body.
- Page usable at 375px without horizontal scroll.
- Virtualization is not needed because the skill list is expected to be small (< 500 items); the grid renders the filtered list.
- Builds with `TreatWarningsAsErrors`.

---

## 17. Mandatory Guardrails

- Do not change the existing `SkillDto` shape.
- Do not expose arbitrary file system paths; read only `Path/SKILL.md` for known discovered skills.
- Do not add new dependencies beyond `MudBlazor` (already in use) and existing parsing utilities.
- Do not break the existing Tailwind token system or dark mode.
- Do not log or expose sensitive paths.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Page renders | `/skills` loads without 500. |
| Search filters | Search text filters by name/description/source. |
| Source filter | Clicking a source chip filters cards. |
| Clear filters | `Clear filters` restores full list. |
| Empty state | Empty list shows `EmptyState`. |
| No results | Search with no matches shows `EmptyState` with clear action. |
| Skill detail dialog | Clicking a card opens the dialog with `References` and `Scripts`. |
| Skill detail 404 | Unknown `source`/`name` returns `404 Not Found`. |
| Dark mode | Cards, badges and dialog switch with `data-theme="dark"`. |
| Mobile | Single-column grid at 375px, no horizontal scroll. |
| Smoke | `https://task.afonsoft.dev/skills` loads. |

---

## 19. Acceptance Criteria

- [ ] `/skills` uses a responsive card grid instead of the table.
- [ ] Filter bar with search and source chips is visible and functional.
- [ ] Each skill card shows `Name`, `Description`, `Source` badge, and `Path` clearly.
- [ ] Clicking a skill card opens a dialog with `References` and `Scripts` sections when present.
- [ ] The `/api/skills/{source}/{name}` endpoint returns `200` with `SkillDetailDto` or `404` when not found.
- [ ] Loading, empty, and no-results states are styled.
- [ ] The page is responsive and accessible.
- [ ] Build compiles with `TreatWarningsAsErrors`.
- [ ] Integration and smoke tests pass.

---

## 20. Implementation Plan

1. Review `Skills.razor` against the `design` skill craft floor.
2. Add CSS token classes to `site.css` for the card grid, source badges, and filter chips.
3. Add `SkillDetailDto` and `GetDetailAsync` to `ISkillDiscoveryService`/`SkillDiscoveryService`.
4. Add `GET /api/skills/{source}/{name}` endpoint in `Program.cs`.
5. Refactor `Skills.razor` with filter bar and card grid.
6. Optionally extract `SkillCard.razor`, `SourceBadge.razor`, `FilterChip.razor`, and `SkillDetailDialog.razor` shared components.
7. Add `EmptyState` for no-results with a `Clear filters` action.
8. Add a markdown section parser for `References` and `Scripts` headings.
9. Build and run unit/integration tests.
10. Smoke test on `https://task.afonsoft.dev/skills` in light and dark mode.

---

## 21. Rollback Strategy

- Revert `Skills.razor` and `site.css` to the previous table-based state.
- Remove any new shared components (`SkillCard`, `SourceBadge`, `FilterChip`) if extracted.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Source badge colors fail contrast | Médio | Média | Provide dark variants and test with a contrast checker. |
| Path text overflows on small screens | Médio | Média | Truncate with `text-ellipsis` and add `title` tooltip. |
| Unknown source breaks layout | Baixo | Baixa | Neutral fallback badge. |

---

## 23. Definition of Done

- [ ] SPEC approved.
- [ ] `Skills.razor` refactored with card grid and filter bar.
- [ ] Source badges use consistent colors.
- [ ] Skill detail dialog and `GET /api/skills/{source}/{name}` endpoint working.
- [ ] Responsive and dark-mode verified.
- [ ] Build and tests pass.
- [ ] Smoke test on `https://task.afonsoft.dev/skills` passes.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Should source filter chips always be visible or only when there are skills from multiple sources? (Proposed: always show `All` plus any source present in the current list.)
2. Should the card have a click action to open a detail dialog? (Resolved: yes — click opens a MudBlazor dialog with the skill details, including `References` and `Scripts` sections if present.)
3. Should the path be displayed as a full relative path or just the parent directory? (Proposed: full relative path with truncation and tooltip.)

## Human Approval Checklist

- [ ] Card grid layout approved.
- [ ] Source badge colors approved.
- [ ] Filter bar behavior approved.
