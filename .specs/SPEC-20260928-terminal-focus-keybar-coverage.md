# SPEC-20260928-terminal-focus-keybar-coverage

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-focus-keybar-coverage` |
| Type | `Tests / Frontend` |
| Stack | `.NET 10 / Blazor WASM / xterm.js` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-terminal-focus-keybar-coverage` |
| Ticket | [#358](https://github.com/afonsoft/agent-harness/issues/358) (Epic [#356](https://github.com/afonsoft/agent-harness/issues/356)) |
| Status | `Approved` |
| Related | `SPEC-20260923-terminal-focus-mode` (PR #341), `SPEC-20260923-terminal-virtual-keybar` (PR #342), `SPEC-20260923-terminal-tabs-keyed-render` (padrão source-guard) |

## 1. User Story

**As a** mantenedor do Harness
**I want** cobertura automatizada mínima sobre a superfície JS/DOM do focus mode e da virtual keybar
**So that** refactors futuros em `Terminal.razor`/`terminal.js`/`site.css` não removam silenciosamente os hooks do E23 — hoje a única validação é manual e continua pendente.

**Problem context:**

PRs #341/#342 entregaram focus mode + virtual keybar sem nenhum teste: `grep -rn "setTerminalFocus\|keybar\|pasteClipboard\|terminal-focus" tests/` retorna 0 ocorrências. A validação manual em dispositivo touch segue pendente desde 2026-09-24 (`gap-analysis-20260923.md` §8). O repositório já tem convenção para isso: `TerminalRazorSourceGuardTests` (`tests/Taskboard.Tests.Unit/Blazor/`) age como tripwire lendo a fonte do componente e do JS — exatamente porque o bug real vive na reconciliação DOM↔JS que o bUnit não observa.

## 2. Scope

**In scope:**

- Novo `TerminalFocusKeybarGuardTests` em `tests/Taskboard.Tests.Unit/Blazor/` seguindo o padrão `TerminalRazorSourceGuardTests` (localizar raiz via `Taskboard.sln`, `Regex.Matches`/`Contains` sobre a fonte).
- Guards cobrindo: `taskboard.setTerminalFocus` invocado em `Terminal.razor`; `html[data-terminal-focus]` e `.terminal-keybar` presentes; `taskboardTerminal.pasteClipboard` exposto em `terminal.js`; keybar condicionada a `(pointer:coarse)`/`(hover:none)` em `site.css`.
- Se trivial, estender `TerminalTabsTests` (bUnit) com render do botão de focus — **opcional**, só se o componente já for testável sem JS real.

**Out of scope:**

- Harness E2E/browser (Playwright/Selenium) — decisão arquitetural maior, não cabe neste spec.
- Validar comportamento runtime do xterm (impossível sem browser).
- Mudar implementação do focus/keybar — cobertura apenas.

## 3. Technical Context

**Evidência AS-IS (superfície a proteger):**

- `src/Taskboard.Blazor/Components/Pages/Terminal.razor:87` — `<div class="terminal-keybar" role="toolbar">`
- `Terminal.razor:377,502` — `JS.InvokeVoidAsync("taskboard.setTerminalFocus", ...)`
- `Terminal.razor:437` — `JS.InvokeVoidAsync("taskboardTerminal.pasteClipboard", tab.ElementId)`
- `src/Taskboard.Client/wwwroot/js/terminal.js`, `taskboard.js` — implementações JS
- `src/Taskboard.Blazor/wwwroot/css/site.css` (ou equivalente) — media query `(pointer:coarse),(hover:none)`

**Files to read before implementing:**

- `tests/Taskboard.Tests.Unit/Blazor/TerminalRazorSourceGuardTests.cs` — padrão a replicar
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor`, `src/Taskboard.Client/wwwroot/js/terminal.js`, CSS do keybar

**Files to create or modify:**

```text
tests/Taskboard.Tests.Unit/Blazor/TerminalFocusKeybarGuardTests.cs   # new
```

## 4. Requirements

### RF-001: Guards de markup do focus/keybar

- **Description:** Fonte de `Terminal.razor` deve conter: (a) `.terminal-keybar` com `role="toolbar"`, (b) invocação `taskboard.setTerminalFocus`, (c) invocação `taskboardTerminal.pasteClipboard`, (d) atributo/classe de estado de focus (`data-terminal-focus` ou `IsFocusMode` equivalente na fonte).

### RF-002: Guards de implementação JS

- **Description:** `terminal.js`/`taskboard.js` devem exportar `setTerminalFocus` e `pasteClipboard` (match sobre o ponto de registro — `taskboard.setTerminalFocus =` / `taskboardTerminal.pasteClipboard =` ou equivalente `export`).

### RF-003: Guard de media query

- **Description:** CSS deve conter media query condicionando `.terminal-keybar` a `(pointer: coarse)`/`(hover: none)` — protege a decisão "keybar só em touch" do E23.

## 5. API Contract

Sem mudança de API — testes apenas.

## 6. Acceptance Criteria

- [ ] **Given** a fonte atual **when** os guards rodam **then** todos passam (green baseline).
- [ ] **Given** remoção intencional de qualquer hook (simulada em teste manual do autor) **when** o guard correspondente roda **then** ele falha com mensagem clara apontando o RF.
- [ ] **Given** `dotnet test` **when** executado **then** suíte verde com os novos guards.

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** mapear nomes exatos na fonte (JS registration names, classes CSS, atributos).
- [ ] **T2 — Implementation:** `TerminalFocusKeybarGuardTests` com um `Fact` por RF.
- [ ] **T3 — Verification:** `dotnet test` filtrado + suíte completa; mutação manual de um hook para confirmar falha do guard.
- [ ] **T4 — Done + PR:** `Status = Done` e PR na branch do spec.

## 8. Organization Guardrails

- Somente `tests/` — nenhum arquivo de `src/` alterado.
- Não adicionar dependências (bUnit já referenciado; guards usam `System.Text.RegularExpressions`).

## 9. Definition of Done

- [ ] Guards cobrindo RF-001..RF-003 verdes.
- [ ] Validação manual em touch continua documentada como pendência separada (não bloqueia este spec).
- [ ] `Status = Done` no mesmo PR.
