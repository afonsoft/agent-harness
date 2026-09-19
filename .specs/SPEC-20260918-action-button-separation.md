# SPEC-20260918 — Action Button Separation: ≥8px entre botões-irmãos (gap-1 → gap-2)

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `action-button-separation` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly / Bootstrap 5` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260918-action-button-separation` |
| Ticket | `GAP-spec-contradiction-action-button-gap` (gap-analysis-20260918) — decisão do usuário: **opção b) impor ≥8px** |
| Status | `Approved` — aprovada pelo usuário (2026-09-18) |

Origin: gap-analysis-20260918 — `SPEC-20260918-sidebar-icon-rail` continha
uma contradição interna: AC-5 exige "sibling buttons have ≥8px separation"
mas o RF autorizava `d-inline-flex gap-1` (4px) para "dense/inline cells".
O usuário decidiu pela **opção b): impor ≥8px** — normalizar os grupos de
botões-irmãos de `gap-1` para `gap-2` (0.5rem = 8px).

## 1. User Story

**As a** Harness user on any viewport,
**I want** sibling action buttons separated by at least 8px,
**so that** AC-5 of the sidebar-icon-rail SPEC is satisfied literally and
adjacent actions can't be mistapped.

## 2. Scope

### In scope

- `Settings.razor` — config-table action cells `d-inline-flex gap-1`
  (Save/Cancel and Edit/Reset groups, ~lines 370, 377) → `gap-2`.
- `Agents.razor` — installed-agent action cell `d-inline-flex gap-1`
  (Login/Models, ~line 83) → `gap-2`.
- `AiChat.razor` — composer action stack `d-flex flex-column gap-1`
  (Send/Run agent, ~line 145) → `gap-2`.
- `SPEC-20260918-sidebar-icon-rail.md` — note resolving the RF/AC
  contradiction (AC-5 wins; `gap-1` no longer sanctioned for action
  groups).

### Out of scope

- `NavMenu.razor` `flex-column gap-1` — navigation item spacing, not an
  action-button group.
- Icon+text gaps inside a single control (e.g. `.agent-badge` inline
  `gap: 0.25rem`, `.nav-link` `gap: .75rem`) — those are intra-element.
- Any other spacing refactor.
- `.github/workflows/**`.

## 3. Technical Context

- Bootstrap 5 gap utilities: `gap-1` = 0.25rem (4px), `gap-2` = 0.5rem
  (8px) — AC-5's "≥8px" maps exactly to `gap-2`.
- The three groups are the only `gap-1` usages containing sibling action
  buttons (verified by grep over `src/Taskboard.Blazor`/`src/Taskboard.Client`).
- Touch-target sizing (≥44px) is handled separately by
  `SPEC-20260918-touch-targets` (merged, PR #129).

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | Every group of sibling action buttons uses `gap-2` (≥8px): `d-inline-flex gap-1` and `flex-column gap-1` action groups become `gap-2`. |
| RF-002 | Navigation/badges/intra-element spacing is unchanged. |
| RF-003 | The sidebar-icon-rail SPEC gains a note recording the contradiction resolution (AC-5 authoritative over the dense-cell `gap-1` allowance). |

## 5. Acceptance Criteria

- **AC-1** *Given* `/settings` config table, *when* inspected, *then* the
  Save/Cancel and Edit/Reset groups have ≥8px between sibling buttons.
- **AC-2** *Given* `/agents`, *when* an installed agent row renders,
  *then* Login/Models siblings have ≥8px separation.
- **AC-3** *Given* `/ai-chat` composer, *when* inspected, *then* Send and
  Run agent have ≥8px vertical separation.
- **AC-4** *Given* the codebase, *when* grepped for `gap-1`, *then* no
  match remains inside a sibling action-button group.

## 6. DoD

- [ ] RF-001..003 implemented.
- [ ] `dotnet build` clean.
- [ ] SPEC → `Status: Done`; PR merged; `taskboard-server` redeployed.
