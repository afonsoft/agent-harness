# SPEC-20261010-agents-page-tabs: CLI Agents page — tabbed layout

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agents page tabs — Dashboard / Sessions / CLI Agents / Prompt |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Blazor WASM (`Agents.razor`) + `agents/sessions` endpoint |
| Change type | Feature / UX |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261010-agents-page-tabs` |
| Depends on | SPEC-20260930-settings-tabs (tab pattern), SPEC-20261006-agent-dashboard-sessions-checkpoints |

## 1. Executive Summary

### Problem

`/agents` is one long scroll: the CLI table comes first, then **Agent
Dashboard** (the info-dense part the user actually watches), then **CLI
Sessions**, **Custom CLIs** and the **Default agent prompt** textarea — five
sections stacked vertically. The dashboard should be the landing view; the
rest is secondary.

Sessions today dump the union of all detected CLIs (`takePerCli: 50`) into one
table — noisy when you only care about one CLI's transcripts.

### Solution

Convert the page to the same tab model Settings already uses (ARIA tabs,
`?tab=` deep-link, keyboard ←/→/Home/End, all panes in DOM via `hidden=`):

| Tab | Contents |
|---|---|
| **Dashboard** (default) | Agent Dashboard card: activity feed + attention columns (delegation tasks, live runs, mailbox) + Sync/Refresh actions |
| **Sessions** | CLI picker first (chips or select of installed+detected CLIs) → sessions table for that CLI only, capped at the **last 30** |
| **CLI Agents** | Installed-CLIs table (detected binaries, versions, probes, Install/Models/Compare modals) **+ the Custom CLIs card and its modal** — one home for "what CLIs exist" |
| **Prompt** | Default agent prompt card (template editor, placeholders, save/reset) |

Sessions behavior change: instead of scanning every store on load, the user
picks a CLI first (remembers last pick via query param `?cli=`), then the
table loads `GET /api/agents/sessions?cli=<id>` with `takePerCli: 30`
(endpoint signature already supports both parameters — server change is only
the default cap and a `take` query param passthrough).

### Scope

In scope: `Agents.razor` restructure into tabs, Sessions CLI picker +
30-cap, `agents/sessions` take param, tests (razor-source guard style).
Out of scope: dashboard column logic, session scanning internals, custom-CLI
persistence, new session actions (resume stays as-is).

## 2. Requisitos

### RF-001 — Tab shell

`AgentsTabs = [("dashboard","Dashboard"),("sessions","Sessions"),("cli-agents","CLI Agents"),("prompt","Prompt")]`.
Same mechanics as `Settings.razor`: `role="tablist"`, `aria-selected`,
roving `tabindex`, `hidden=` panes, `OnAfterRender` reads `?tab=` once,
`SelectTab` does `Navigation.NavigateTo($"/agents?tab={id}", replace: true)`,
`OnTabsKeyDown` handles ←/→/Home/End.

### RF-002 — Dashboard tab (default)

Moves the existing Agent Dashboard card (activity feed `ev` list + four
attention columns + Refresh) into the first pane. No logic change — pure
relocation. Empty-state copy stays.

### RF-003 — Sessions tab with CLI-first picker

- Pane shows a CLI selector before any table: one button-chip per source the
  scanner supports (`claude`, `codex`, `opencode`, `gemini`, `agy`, `devin`)
  + custom CLIs from `GET /api/agents/custom` — disabled/annotated when the
  store has no sessions.
- On select → `Client.GetAgentSessionsAsync(cli)` → table identical to today's
  columns (Session, Directory, Modified, Resume).
- Selection persists in the URL: `/agents?tab=sessions&cli=opencode` —
  refresh restores both tab and CLI.
- No sessions for the chosen CLI → the existing empty-state copy scoped to
  that CLI.

### RF-004 — Sessions cap 30 + `take` param

`agents.MapGet("sessions")` gains `int? take` (default 30, clamp 1..200) →
`scanner.ScanAsync(cli, takePerCli: take ?? 30, ct)`. Client passes
`take: 30`. Response shape unchanged.

### RF-005 — CLI Agents tab (installed + custom)

Installed-CLIs card (current first table: CLI/Binary/Installed/Version +
Install/Models/Compare actions) and the Custom CLIs card + `_customCliModal`
live in this pane. The Sync/Refresh toolbar stays attached to the installed
table (Sync is metrics-wide but its natural home is next to the CLI table).

### RF-006 — Prompt tab

The Default agent prompt card (textarea `{repoUrl}/{issueTitle}/{issueBody}/
{issueComments}`, Salvar/Restaurar, `customizado` badge) moves here verbatim.

## 3. Arquitetura

```
Agents.razor
 ├─ tablist (same ARIA model as Settings)
 ├─ pane dashboard   → existing _dashboard/_events markup
 ├─ pane sessions    → _selectedCli chips → Client.GetAgentSessionsAsync(cli, take:30)
 │                        └─▶ GET /api/agents/sessions?cli={cli}&take=30
 │                              └─▶ AgentSessionScanner.ScanAsync(cli, 30)
 ├─ pane cli-agents  → installed table + Custom CLIs card + modals
 └─ pane prompt      → template card
```

All four modals (`_installModal`, `_modelsModal`, `_compareModal`,
`_customCliModal`) stay at page root — they must not live inside a `hidden=`
pane (Blazor.Bootstrap `Modal` renders in place).

Files touched: `Agents.razor` (+`.css` if pane spacing needs it),
`Program.cs` (`take` param), `TaskboardClient` (`GetAgentSessionsAsync(cli,
take)`), tests.

## 4. Config

Nenhuma chave nova. `?tab=`/`?cli=` são parâmetros de URL, não config.

## 5. Segurança

- `take` clamp `[1,200]` — evita scan gigante via query.
- `cli` é string opaque passada ao scanner; o scanner já trata valores
  desconhecidos como conjunto vazio — manter esse comportamento (400 só se o
  valor violar `[a-z0-9-]` — evita path tricks).

## 6. Testes

Unit razor-source guards (`tests/Taskboard.Tests.Unit/Blazor/`):

- `AgentsTabsTests`: `Agents.razor` contém `role="tablist"`, quatro
  `role="tab"` com `data-tab` dashboard/sessions/cli-agents/prompt, panes com
  `hidden=`, `aria-selected` ternário (não bool-bound — regra Blazor/aria),
  `?tab=`/`?cli=` handling.
- `AgentsSessionsTests` (client/service level, NSubstitute): picker → chama
  `sessions?cli=X&take=30`; `take` fora de `[1,200]` → clampado; `cli`
  inválido → 400.
- Scanner não muda contrato — testes existentes do `AgentSessionScanner`
  continuam.

## 7. Fases

- **P1**: RF-001 + RF-002 + RF-005 + RF-006 (tab shell + relocação pura) — a página fica navegável por abas sem mudança de comportamento.
- **P2**: RF-003 + RF-004 (CLI picker + cap 30 + `take` param).

## Acceptance criteria

- `/agents` abre no tab **Dashboard**; `?tab=sessions` abre direto em Sessions.
- Em Sessions, nenhuma tabela aparece antes de escolher um CLI; após escolher,
  só sessões daquele CLI, no máximo 30, mais recentes primeiro.
- `?tab=sessions&cli=codex` restaura tab + CLI após refresh.
- CLI Agents tab contém a tabela instalada **e** o card Custom CLIs; os quatro
  modals abrem normalmente a partir desse tab.
- Prompt tab contém o textarea + Salvar/Restaurar funcionando.
- Navegação por teclado (←/→/Home/End) e `aria-selected` corretos — mesmo
  modelo do Settings.

## Open questions

1. Badge de contagem por CLI nos chips do picker (scan leve `take=1` por CLI)
   — útil mas dobra o custo de scan; proposto: P3 se pedirem.
2. "Sync now" (metrics) é global — manter no tab CLI Agents (proposto) ou
   mover para o header da página? Header é mais visível; CLI Agents é o
   contexto certo — seguimos com CLI Agents.
3. Limite fixo 30 vs. paginação/"carregar mais" — pedido foi 30; paginação
   fica como followup se a base de sessões crescer.
