# SPEC-20260923-terminal-focus-mode

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-focus-mode` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WASM / xterm.js / Bootstrap` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260923-terminal-focus-mode` |
| Ticket | [#339](https://github.com/afonsoft/agent-harness/issues/339) (Epic [#338](https://github.com/afonsoft/agent-harness/issues/338)) |
| Status | `Implemented` — entregue via [PR #341](https://github.com/afonsoft/agent-harness/pull/341) (aguardando merge) |
| Gap | `GAP-frontend-terminal-focus-mode` (gap-analysis-20260923) |
| Related | `SPEC-20260917-terminal-tabs`, `SPEC-20260920-terminal-pty-resize`, `SPEC-20260918-touch-targets`, `SPEC-20260918-sidebar-icon-rail` |

## 1. User Story

**As a** usuário do Harness acessando `/terminal` — principalmente em celular/tablet,
**I want** um botão para expandir o terminal para o viewport inteiro (e outro para restaurar), escondendo o título, a descrição e o chrome do app,
**So that** eu maximize as linhas/colunas úteis do shell em telas pequenas sem precisar abandonar a página ou a sessão PTY.

**Problem context:**
Hoje `/terminal` sempre renderiza `<h1>` + parágrafo de ajuda + tab strip dentro de `.app-main` (padding 1.5rem), abaixo de `.app-topbar` (3.5rem) e ao lado da sidebar. Em mobile há um hack em `site.css` (`@media ≤575.98px { .terminal-host { height: 100dvh; margin-inline: -0.75rem } }`) que força o host a 100dvh, mas topbar + título + tabs continuam ocupando espaço — a página estoura o viewport e o terminal rola junto com o chrome. Não existe nenhum modo "foco/zen" em nenhuma página do app.

## 2. Scope

**In scope:**
- Botão de alternância expandir/restaurar na página `/terminal` (todos os dispositivos — desktop e mobile).
- Estado "focus" CSS-driven via atributo no `<html>` (`html[data-terminal-focus]`), mesmo padrão do `data-sidebar-collapsed` já existente.
- No modo focus: esconde `.app-topbar`, o `<h1>` e o parágrafo de descrição da página; a área do terminal passa a ocupar o viewport inteiro (`100dvh`); a tab strip permanece visível em forma compacta (necessária para trocar de aba no mobile).
- Botão flutuante "restaurar" sempre acessível no modo focus (não depende da topbar, que estará oculta — no mobile o hamburger sai junto com ela).
- Remover o hack `@media ≤575.98px { .terminal-host { height: 100dvh } }` e substituí-lo pelo layout correto do focus mode.
- Refit do xterm (`taskboardTerminal.fitNow`) ao entrar/sair do modo focus.

**Out of scope:**
- Fullscreen API do browser (`requestFullscreen`) — o modo é CSS-only para não quebrar com navegação Blazor e PWAs.
- Barra de teclas virtuais para mobile — coberta por `SPEC-20260923-terminal-virtual-keybar`.
- Terminal read-only do cockpit (`RunTerminal.razor`) — sem modo focus nesta entrega.
- Persistir o estado focus (localStorage) — sessão apenas.
- Interceptar `Esc` para sair do modo focus — `Esc` é tecla válida do PTY; a saída é exclusivamente pelo botão.
- Split panes, temas, zoom de fonte.

## 3. Technical Context

**Where the change happens** (architecture, layers, integrations):
Frontend-only. `Terminal.razor` ganha um flag `_focusMode` e um botão toggle; ao alternar, chama um helper JS (`taskboard.setTerminalFocus`) que seta/remove `data-terminal-focus` em `document.documentElement` — o mesmo mecanismo de `taskboard.setSidebarCollapsed` (`taskboard.js`), então o CSS global em `site.css` pode esconder `.app-topbar` (que mora no `MainLayout`, fora da página) sem JS extra no layout. A `.terminal-page` em focus vira overlay de viewport via CSS (`position: fixed; inset: 0; z-index` acima do chrome, fundo `#0d1117`). O xterm refita via `fitNow` já existente; o `ResizeObserver` em `terminal.js` já reporta o novo cols/rows ao PTY — nenhum servidor muda.

**Files to read before implementing:**
- `AGENTS.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor`
- `src/Taskboard.Blazor/Layout/MainLayout.razor` (estrutura `.app-shell`/`.app-topbar`)
- `src/Taskboard.Client/wwwroot/css/site.css` (`.terminal-*`, `.app-*`, linha ~1191 hack mobile)
- `src/Taskboard.Client/wwwroot/js/taskboard.js` (padrão `setSidebarCollapsed`)
- `src/Taskboard.Client/wwwroot/js/terminal.js` (`fitNow`, `focus`, `reportResize`)
- `.specs/SPEC-20260917-terminal-tabs.md`

**Files to create or modify:**
```text
src/Taskboard.Blazor/Components/Pages/Terminal.razor   (botão toggle + flag _focusMode + botão flutuante de sair)
src/Taskboard.Client/wwwroot/css/site.css            (html[data-terminal-focus] rules + remoção do hack ≤575.98px)
src/Taskboard.Client/wwwroot/js/taskboard.js         (setTerminalFocus helper — data attribute no <html>)
docs/features.md · docs/features.pt-br.md            (seção terminal: modo focus)
.specs/SPEC-20260923-terminal-focus-mode.md
```

## 4. Requirements

### RF-001: Toggle de modo focus
- **Description:** A página `/terminal` exibe um botão de alternância (ícone expand/contract) visível em todos os dispositivos, posicionado na área da tab strip (lado direito). Clicar entra/sai do modo focus.
- **Rules:** alvo de toque ≥44×44px (SPEC-20260918-touch-targets); `aria-pressed`/`aria-label` refletem o estado; título "Expand terminal" / "Restore terminal".
- **Input → Output:** clique → `_focusMode` alterna → `taskboard.setTerminalFocus(bool)` → `html[data-terminal-focus]` → CSS aplica o layout focus.

### RF-002: Layout focus
- **Description:** Com `html[data-terminal-focus]` presente: `.app-topbar` fica `display:none`; `.terminal-page` vira `position:fixed; inset:0` (overlay total, `z-index` acima do chrome do app, fundo `#0d1117`); `<h1>` e parágrafo de descrição ficam ocultos; `.terminal-host` ocupa todo o espaço restante sem `min-height:200px` nem borda/radius/margins extras.
- **Rules:** a tab strip continua visível mas compacta (padding reduzido); a sidebar permanece intocada (em mobile ela já é offcanvas oculta — o hamburger some com a topbar, e a saída do focus é pelo botão flutuante do RF-003); nada fora de `.app-shell` muda.
- **Input → Output:** `data-terminal-focus` presente → viewport 100% para o terminal; ausente → layout atual preservado.

### RF-003: Saída sempre acessível
- **Description:** No modo focus existe um botão flutuante (canto superior direito do overlay, acima do conteúdo) que restaura o layout normal — necessário porque a topbar (e o hamburger no mobile) está oculta.
- **Rules:** mesmo componente lógico do toggle (mesma ação, estado `aria-pressed=true`); visível apenas no modo focus; `z-index` acima do xterm mas abaixo de modais Bootstrap (`<1040`); não intercepta teclas — `Esc` continua indo para o PTY.
- **Input → Output:** clique → sai do modo focus → layout restaurado.

### RF-004: Refit e foco do xterm
- **Description:** Após cada transição de modo, chamar `taskboardTerminal.fitNow(elementId)` e `taskboardTerminal.focus(elementId)` da aba ativa — o `ResizeObserver`/`reportResize` existente propaga cols/rows ao PTY via hub `Resize`.
- **Rules:** aguardar `Task.Yield()` após `StateHasChanged` (mesmo padrão de `ActivateTabAsync`); falhas de JS interop no teardown são engolidas como hoje.
- **Input → Output:** toggle → cols/rows corretos no PTY após a transição.

### RF-005: Cleanup do hack mobile
- **Description:** Remover `@media (max-width:575.98px) { .terminal-host { height:100dvh; margin-inline:-0.75rem; border-radius:0 } }` de `site.css` — substituído pelo focus mode; fora do focus o terminal volta a respeitar o fluxo normal em qualquer viewport.
- **Rules:** `.terminal-page` e `.terminal-body` continuam `height:100%` dentro de `.app-main` no modo normal.
- **Input → Output:** viewport pequeno sem focus → terminal ocupa o espaço de `.app-main` sem estourar a página.

### RF-006: Documentação
- **Description:** `docs/features(.pt-br).md` documentam o modo focus do terminal (botão, o que é ocultado, limitação de não usar Fullscreen API).

**Business rules / invariants:**
- O estado focus é **sessão apenas** — nunca persistido; recarregar a página sai do focus.
- Nenhuma tecla é interceptada para sair do focus — a saída é só pelo botão.
- Nenhuma mudança em `TerminalHub`, `TerminalSessionManager` ou APIs — o hub `Resize` existente já cobre a nova geometria.

## 5. API Contract

Não se aplica — nenhum endpoint/contrato novo. Reutiliza `Input`/`Resize`/`output`/`closed` do `terminal-hub` (SPEC-20260917-terminal-tabs §5).

## 6. Acceptance Criteria

- [ ] **Dado** usuário na `/terminal` em qualquer viewport **quando** clica o botão expandir **então** topbar, título e descrição desaparecem e o terminal ocupa o viewport inteiro.
- [ ] **Dado** modo focus ativo **quando** clica o botão flutuante restaurar **então** o layout original retorna e o botão fica `aria-pressed=false`.
- [ ] **Dado** modo focus ativo no mobile **quando** a transição termina **então** o xterm refitou e o PTY recebeu cols/rows do novo tamanho.
- [ ] **Dado** 2+ abas abertas em modo focus **quando** troca de aba **então** a tab strip compacta permite a troca e a aba correta refita.
- [ ] **Dado** viewport ≤576px **sem** modo focus **quando** a página carrega **então** o terminal não força 100dvh nem estoura o scroll da página (hack removido).
- [ ] **Dado** modo focus ativo **quando** o usuário aperta `Esc` **então** `\x1b` vai para o PTY e o modo focus NÃO sai.
- [ ] **Dado** modo focus ativo **quando** a página recarrega ou navega para outra rota **então** o estado focus não persiste (atributo removido).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Navegar para `/board` com focus ativo | route change | `DisposeAsync`/helper remove `data-terminal-focus`; outra página nunca herda o overlay |
| Toggle durante `Connecting`/`Closed` de aba | clique | layout alterna normalmente; xterm refita quando a sessão abrir |
| Teclado virtual mobile aberto | OSK resize | `ResizeObserver` existente refita — cols/rows reais propagados |
| Desktop ≥992px | clique expandir | topbar some, sidebar pode permanecer (desktop não é touch-primary); terminal ocupa `.app-content` inteiro |
| `data-terminal-focus` setado manualmente | devtools | comportamento idêntico ao botão (CSS puro) |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** ler arquivos da seção 3; confirmar que `reportResize`/`fitNow` já cobrem a propagação de tamanho.
- [x] **T2 — JS helper:** `taskboard.setTerminalFocus(bool)` em `taskboard.js` setando `document.documentElement.dataset.terminalFocus`.
- [x] **T3 — Razor:** flag `_focusMode`, botão toggle na tab strip, botão flutuante de sair, chamadas `fitNow`+`focus` pós-transição, remoção do atributo no `DisposeAsync`.
- [x] **T4 — CSS:** regras `html[data-terminal-focus]` (topbar off, overlay `.terminal-page`, tab strip compacta, host full) + remover o hack `@media ≤575.98px`.
- [x] **T5 — Docs:** `docs/features(.pt-br).md`.
- [x] **T6 — Validation:** `dotnet build` (warnings as errors); testes existentes passam; evidência manual: mobile ≤576px (expandir → tela cheia real, sem scroll de página) e desktop.

**7.1 Validation:** .NET — suíte existente deve passar sem regressão (mudança frontend/JS; não há harness de teste de UI — evidência manual documentada nos critérios); `dotnet build` limpo.

## 8. Organization Guardrails

- Branch `feature/devin-20260923-terminal-focus-mode`; nunca commit em `main`/`develop`.
- Sem alteração em `.github/workflows/**`.
- Não logar conteúdo de terminal.
- Escopo travado: nada de Fullscreen API, keybar virtual, persistência ou interceptação de `Esc`.
- Mudança apenas em Blazor/JS/CSS — nenhum contrato de servidor.

## 9. Definition of Done

- [ ] RF-001…RF-006 implementados.
- [ ] Critérios da seção 6 verificados (build + evidência manual mobile/desktop).
- [ ] Edge cases tratados (navegação limpa o atributo, OSK refita).
- [ ] `dotnet build` + `dotnet test` verdes.
- [ ] Guardrails respeitados.

**Next action after DoD:** `Status = Done` + PR em `feature/devin-20260923-terminal-focus-mode`.

## Open Questions / Pending Ambiguity

- Nenhuma — decisões confirmadas com o usuário: esconde topbar+título (mantém tab strip), toggle em todos os dispositivos, sem persistência, `Esc` não interceptado, cockpit fora de escopo.
