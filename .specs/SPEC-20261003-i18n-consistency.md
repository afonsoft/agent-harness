# SPEC-20261003-i18n-consistency: document language + culture-consistent formatting

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | i18n Consistency Baseline |
| Product / System | agent-harness |
| Module / Bounded Context | Blazor (UI) |
| Change type | Bugfix / Frontend |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261003-i18n-consistency` |
| Technical owner | afonsoft |
| Status | Draft |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20261003-a11y-baseline |
| Gap keys | GAP-a11y-html-lang, GAP-i18n-date-formats, GAP-typography-ellipsis |

---

## 1. Executive Summary

### Problem

- `index.html` declares `<html lang="en">` while UI copy is
  predominantly pt-BR (AI Code titles, Settings labels, toasts) mixed
  with English ("Log out", "Loading settings..."). Assistive tech reads
  Portuguese text with English phonology.
- Date/number formatting is inconsistent and culture-dependent:
  `ToString("g")` (Settings.razor:102, FinOps.razor:245-250) renders
  "9/3/2026 11:23 AM" because the WASM runtime runs with en-US culture;
  other pages hardcode `dd/MM HH:mm` (Cockpit.razor:76), `MM-dd HH:mm`
  (FinOps.razor:287-311) and `yyyy-MM-dd HH:mm` (Agents.razor:640).
- Literal `...` used instead of `…` across Loading/copy strings
  (AiChat.razor:75, Settings.razor:18, Agents.razor:42, Prompts.razor:20,
  Cockpit.razor:43, Workflow.razor, FinOps placeholders).

### Objective

Settle on **pt-BR as the document language** (majority of copy already
is; the repo's docs are pt-BR first for users), pin the client culture
accordingly, and normalize date/number/typography conventions so every
page formats consistently.

## 2. Scope

**In scope:**
- `<html lang="pt-BR">` in `index.html`; English-only strings keep working (no translation effort required).
- Set Blazor WASM default culture (`CultureInfo.DefaultThreadCurrentCulture/UICulture = pt-BR` in `Taskboard.Client/Program.cs`) so `ToString("g")`, `N0` etc. render pt-BR formats.
- Normalize all hardcoded date formats to `ToString("g")`-family (or a shared `UiFormat` helper: `dd/MM/yyyy HH:mm` for datetimes, `dd/MM HH:mm` for compact), removing the mixed `MM-dd`/`yyyy-MM-dd` UI strings. Code/log contexts keep ISO.
- Replace `...` with `…` in user-visible strings (Loading messages, placeholders, titles).
- `tabular-nums` on FinOps numeric columns (`site.css` utility class or Bootstrap `font-monospace` where already used).

**Out of scope:**
- Full bilingual resource system (resx localization) — out of budget; tracked as future work.
- Server-side log messages and API payloads.

## 3. Technical Context

Client boot: `src/Taskboard.Client/Program.cs`. Shell: `index.html`.
Formatting lives inline in the pages listed below.

**Files to read before implementing:**
- `src/Taskboard.Client/wwwroot/index.html`
- `src/Taskboard.Client/Program.cs`
- `src/Taskboard.Blazor/Components/Pages/{FinOps,Cockpit,Agents,Settings,AiChat,Prompts,Workflow}.razor`
- `src/Taskboard.Client/wwwroot/css/site.css`

**Files to create or modify:**
```text
src/Taskboard.Client/wwwroot/index.html
src/Taskboard.Client/Program.cs
src/Taskboard.Blazor/Components/Pages/FinOps.razor
src/Taskboard.Blazor/Components/Pages/Cockpit.razor
src/Taskboard.Blazor/Components/Pages/Agents.razor
src/Taskboard.Blazor/Components/Pages/Settings.razor
src/Taskboard.Blazor/Components/Pages/AiChat.razor
src/Taskboard.Blazor/Components/Pages/Prompts.razor
src/Taskboard.Blazor/Components/Pages/Workflow.razor
src/Taskboard.Blazor/Components/Shared/UiFormat.cs   (new — optional)
src/Taskboard.Client/wwwroot/css/site.css
```

## 4. Requirements

### RF-001: document language
- **Description:** the rendered document language must match the predominant copy language.
- **Rules:** `lang="pt-BR"`; UI strings remain as authored (no translation pass).
- **Input → Output:** page load → `<html lang="pt-BR">`.

### RF-002: pt-BR client culture
- **Description:** WASM client culture is pt-BR so `"g"`, `"N0"`, `"C"` formats are pt-BR.
- **Rules:** set in `Program.cs` before host run; must not break existing tests.

### RF-003: consistent date/number formatting
- **Description:** all user-facing datetimes use pt-BR formats via culture or a shared helper; no page invents its own format string.
- **Rules:** datetimes `dd/MM/yyyy HH:mm`; compact `dd/MM HH:mm`; numbers grouped pt-BR (`1.234`); ISO stays in logs/API only.

### RF-004: typography polish
- **Description:** loading/copy strings use `…`; numeric comparison columns use tabular figures.
- **Rules:** replace ASCII `...` in user-facing strings; add `tabular-nums` utility and apply in FinOps tables.

**Business rules / invariants:**
- API/JSON payloads and file formats unchanged.
- No resx/localization infra introduced.

## 5. API Contract

N/A.

## 6. Acceptance Criteria

- [ ] **Given** any page **when** inspected **then** `<html lang="pt-BR">`.
- [ ] **Given** FinOps **when** a date renders **then** it shows `dd/MM HH:mm` (pt-BR), never `MM-dd` or `9/3/2026 11:23 AM`-style en-US.
- [ ] **Given** Settings "Last run" **when** rendered **then** pt-BR format.
- [ ] **Given** Loading states **when** rendered **then** they use `…` (single char).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Browser en-US user | any page | Still pt-BR formats — product language decision |
| Numbers in FinOps | `1234567` tokens | `1.234.567` grouping |

## 7. Task Plan

- [ ] **T1 — Discovery:** grep all `ToString("` in `src/Taskboard.Blazor` to complete the format inventory.
- [ ] **T2 — Implementation:** lang, culture boot, format normalization, ellipsis, tabular-nums.
- [ ] **T3 — Verification:** render each touched page; AC checks.
- [ ] **T4 — Validation:** build + format; grep-zero for `ToString("MM-dd`/`dd/MM` leftovers outside helper.
- [ ] **T5 — Done + PR.**

## 8. Organization Guardrails

- Feature branch only; no workflow edits; `TreatWarningsAsErrors`.

## 9. Definition of Done

- [ ] RF-001..RF-004 implemented; ACs verified; build green.

## Open Questions / Pending Ambiguity

- pt-BR chosen as document language per copy majority — if the product should instead be English-first, flip RF-001/RF-002 target and translate pt-BR strings (bigger effort; would need its own resx spec).
