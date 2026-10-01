# SPEC-20261001-terminal-memory-mobile: scrollback enxuto e terminal mobile-first

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Terminal Memory Footprint Reduction + Mobile UX |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation + Infrastructure (Terminal PTY) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-terminal-memory-mobile` |
| Technical owner | afonsoft |
| Status | Draft (pending human approval) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20260917-terminal-tabs, SPEC-20260920-terminal-pty-resize, SPEC-20260923-terminal-virtual-keybar, SPEC-20260923-terminal-focus-mode, SPEC-20260928-ai-code-generic-cli (RF-003 reattach/replay), SPEC-20260930-mobile-responsive-ui |

---

## 1. Executive Summary

### Problem

O histórico do terminal consome memória excessiva nas duas pontas:

**Cliente (xterm.js, por aba):**

- `terminal.js#init` → `scrollback: 5000` linhas por aba interativa;
  até 8 abas simultâneas (`MaxSessionsPerUser = 8`) → até ~40k linhas
  retidas em JS/DOM por usuário.
- `terminal.js#initReadOnly` → `scrollback: 10000` linhas no cockpit
  (`RunTerminal`), **além** da lista `_events`/`Events` que o
  `CockpitRun` já mantém — a saída fica duplicada em memória.
- Abas inativas permanecem montadas (`d-none`), cada uma com o buffer
  completo.

**Servidor (`TerminalSessionManager`):**

- `SessionEntry.Scrollback` (`StringBuilder`) com cap de
  `ScrollbackLimit = 200_000` chars (~400 KB UTF-16) **por sessão** —
  retido inclusive em sessões órfãs aguardando reattach (até 10 min) e
  em abas que o usuário esqueceu abertas (idle 30 min).
- Pior caso: 8 sessões × 400 KB ≈ 3,2 MB por usuário só de scrollback.

**Mobile:**

- `fontSize` fixo em 13 — pequeno para leitura em telas densas e sem
  ajuste para viewports estreitas.
- A virtual keybar só existe dentro do focus mode (`pointer:coarse` +
  `html[data-terminal-focus]`): fora dele não há como enviar
  Esc/Tab/setas/Ctrl em touch — e o focus mode esconde o topbar, então
  o usuário é forçado a um modo tudo-ou-nada.
- A tabstrip tem padding desktop e pode estourar a viewport em 360px.
- Com o soft keyboard aberto, o ResizeObserver refaz o fit mas nada
  garante que a linha do cursor fique visível.

### Objective

1. **Scrollback client-side adaptativo e menor**: defaults reduzidos e
   menores ainda em coarse pointer/telas pequenas.
2. **Scrollback server-side reduzido e configurável**: cap menor,
   overridable via `Terminal:ScrollbackChars`.
3. **Keybar acessível fora do focus mode** em dispositivos touch.
4. **Fit/visual polish mobile**: font-size adaptativo, tabstrip
   compacta, cursor visível após refit com teclado aberto.

### Expected outcome

Ver ACs (seção 6). Redução alvo: ~60–75% do pico de scrollback por
usuário (server) e ~60% por aba (client) sem perda de funcionalidade —
replay de reattach e copiar/baixar continuam operando sobre o buffer
agora menor.

### Out of scope

- Persistência de scrollback em disco/banco (replay continua em
  memória, apenas menor).
- Serialização/hibernação de abas inativas (dispose + recriação de
  xterm ao trocar de aba) — avaliada e adiada: o risco de quebrar o
  PTY vivo + a complexidade de estado não cabem neste slice; os caps
  menores já resolvem o problema reportado.
- Mudanças no protocolo PTY/SignalR (`TerminalHub` intacto).
- O `_events`/`Events` do `CockpitRun` (necessário à timeline) — só o
  buffer do xterm read-only encolhe.
- Virtual keyboard/shortcuts das **outras** telas — já coberto por
  SPEC-20260930-mobile-responsive-ui.

---

## 2. Agent Role

> Blazor/JS engineer — xterm.js interop, CSS mobile-first,
> concurrency-safe session manager.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não alterar `TerminalHub` (contrato SignalR) nem `IPtySession`.
- Não remover o replay de scrollback no rebind/reattach
  (SPEC-20260928 RF-003) — apenas o tamanho muda.
- Não mexer em workflows/CI; `dotnet format` antes do push.
- Manter `TerminalRazorSourceGuardTests`/`TerminalFocusKeybarGuardTests`
  verdes e adicionar guards para os novos defaults.

---

## 4. Technical Context

### Relevant files

| File | Role |
|---|---|
| `src/Taskboard.Client/wwwroot/js/terminal.js` | `init`/`initReadOnly` criam o xterm com `scrollback` fixo; `dispose`, `watchScroll`, `getText` |
| `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` | `SessionEntry.Scrollback` + `ScrollbackLimit` (200_000), `AppendScrollback` trim, replay no `OpenAsync`/reattach |
| `src/Taskboard.Blazor/Components/Pages/Terminal.razor` | página `/terminal`: tabstrip, keybar, focus mode, `taskboardTerminal.init` |
| `src/Taskboard.Blazor/Components/Cockpit/RunTerminal.razor` | cockpit read-only → `initReadOnly` |
| `src/Taskboard.Blazor/Components/AiChat/PtyThreadPane.razor` | AI Code terminal → `init` |
| `src/Taskboard.Blazor/Components/Cockpit/RunShell.razor` | run shell → `init` |
| `src/Taskboard.Client/wwwroot/css/site.css` | `.terminal-keybar` (só em `data-terminal-focus` + coarse), tabstrip, focus overlay |
| `tests/Taskboard.Tests.Unit/Integrations/Terminal/TerminalSessionManagerTests.cs` | cobre trim/replay do scrollback |
| `tests/Taskboard.Tests.Unit/Blazor/Terminal*GuardTests.cs` | source guards JS/DOM |

### Current behavior

- `init`/`initReadOnly` instanciam `new Terminal({ scrollback: N })`
  com `N` fixo (5000/10000).
- `AppendScrollback` faz `Append` + `Remove(0, len - 200k)` sob `_gate`.
- Keybar visível somente em `html[data-terminal-focus]` +
  `(pointer: coarse)/(hover: none)`.
- `interactive-widget=resizes-content` já presente no viewport
  (SPEC-20260930-mobile-responsive-ui).

### New/changed elements

- `terminal.js`: `resolveScrollback(kind)` — lê
  `matchMedia('(pointer: coarse), (max-width: 767.98px)')` e devolve o
  cap por tipo de terminal; `init`/`initReadOnly` aceitam `options`
  opcional `{ scrollback }` (override pelo caller; default adaptativo).
- `TerminalSessionManager`: `ScrollbackLimit` 200_000 → **64_000**
  chars, overridable por ctor interno/config `Terminal:ScrollbackChars`.
- `Terminal.razor`: botão ⌨ na tabstrip (só coarse pointer) alterna a
  keybar fora do focus mode — `html[data-terminal-keybar]` espelha o
  padrão `data-terminal-focus`.
- `terminal.js`: após `reportResize`/fit bem-sucedido com
  `isAtBottom`, `term.scrollToBottom()` mantém o cursor visível quando
  o soft keyboard abre/fecha.
- CSS: font-size adaptativo (`init` recebe `fontSize` 12 mobile/13
  desktop), tabstrip compacta <768px, keybar visível também com
  `[data-terminal-keybar]`.

---

## 5. Functional Requirements

### RF-001 — Scrollback client-side adaptativo

`taskboardTerminal.init`/`initReadOnly` passam a resolver o scrollback
assim:

| Contexto | Interativo (`init`) | Read-only (`initReadOnly`) |
|---|---|---|
| Desktop (fine pointer, ≥768px) | 2000 linhas | 3000 linhas |
| Mobile (coarse pointer ou <768px) | 800 linhas | 1200 linhas |
| Override via `options.scrollback` | respeitado | respeitado |

- Detecção: `window.matchMedia('(pointer: coarse), (hover: none), (max-width: 767.98px)')`.
- Assinaturas ficam backward-compatible: `init(elementId, dotNetRef, tabKey, options?)`, `initReadOnly(elementId, options?)` — callers existentes não precisam passar nada.

### RF-002 — Cap server-side menor e configurável

- `ScrollbackLimit`: 200_000 → 64_000 chars (default interno).
- Override opcional por configuração `Terminal:ScrollbackChars`
  (int > 0; inválido/ausente → default). Injetado via `IConfiguration`
  no ctor público; ctor interno de teste aceita o valor direto.
- `AppendScrollback`/`GetScrollback`/replay permanecem idênticos —
  apenas o cap muda.

### RF-003 — Keybar fora do focus mode (touch)

- Novo toggle na tabstrip (ícone ⌨, `aria-pressed`, só renderizado em
  coarse pointer via CSS) liga `html[data-terminal-keybar]`.
- `.terminal-keybar` passa a aparecer quando `[data-terminal-keybar]`
  **ou** `[data-terminal-focus]` estiverem presentes em coarse pointer.
- Em fine pointer a keybar nunca aparece fora do focus mode (desktop
  tem teclado físico).

### RF-004 — Tipografia e densidade mobile

- `init`/`initReadOnly` escolhem `fontSize` 12 (mobile) / 13 (desktop)
  pelo mesmo matchMedia do RF-001; `options.fontSize` sobrepõe.
- <768px: `.terminal-tabstrip` com padding reduzido,
  `overflow-x: auto` + `scroll-snap-type: x proximity`, título da aba
  truncado (`max-width` + ellipsis), h1/descrição da página colapsados
  (a descrição esconde <576px).
- Altura mínima do host em mobile reduz p/ 160px (hoje 200px) para
  acomodar keybar + keyboard.

### RF-005 — Cursor visível com soft keyboard

- Após `reportResize` aplicar fit válido, se `isAtBottom(entry)` o
  term rola ao fim (`scrollToBottom`) — teclado abrindo/fechando não
  deixa a linha ativa escondida atrás da keybar/teclado.
- Sem regressão: usuário scrolled-up mantém posição (não força scroll).

### RF-006 — Guards e testes

- `TerminalSessionManagerTests`: novo caso cobrindo trim no novo cap
  (64k) e respeito a cap customizado via ctor interno.
- Source guard (padrão `TerminalRazorSourceGuardTests`): `terminal.js`
  contém `resolveScrollback`, `scrollToBottom` pós-resize e os defaults
  do RF-001; `Terminal.razor` contém o toggle `data-terminal-keybar`;
  `site.css` contém a regra `[data-terminal-keybar] .terminal-keybar`.
- Nenhum teste existente quebrado (o teste de replay/reattach continua
  válido com cap menor).

---

## 6. Acceptance Criteria

- AC-01: `/terminal` em viewport 360×740: sem scroll horizontal de
  página, fonte legível (12px), keybar acessível sem entrar em focus
  mode.
- AC-02: Pico de scrollback server-side ≤ 64 KB por sessão (default)
  e ≤ ~512 KB por usuário (8 sessões) — contra ~3,2 MB hoje.
- AC-03: Pico client-side ≤ 2000 linhas/aba desktop e 800 mobile —
  contra 5000 hoje; cockpit read-only ≤ 3000/1200 — contra 10000.
- AC-04: Browser refresh/reattach continua exibindo o backlog
  (limitado ao novo cap) — teste de unidade + manual.
- AC-05: `getText`/download refletem o buffer bounded (documentado na
  UI do botão copiar? não — manter silencioso, cap é razoável).
- AC-06: Build 0 warnings, `dotnet format --verify` limpo, suite
  unitária + integração verdes, guards novos passando.
- AC-07: `Terminal:ScrollbackChars` respeitado (ex.: 1000 → buffer
  corta em 1000 chars).

## 7. Risks & Mitigations

- **Replay menor pode esconder contexto ao reattach** — mitigado: cap
  de 64k chars ainda cobre ~800 linhas de saída típica; overrides via
  config para quem precisar de mais.
- **matchMedia em prerender** — `init` só roda em `OnAfterRender` no
  browser; sem chamada em prerender.
- **StringBuilder capacity não devolve memória ao trim** — aceito: o
  cap menor já limita o pico; `Remove` previne crescimento.
- **`data-terminal-keybar` fora do focus mode sobrepõe layout** — a
  keybar participa do flex flow (já faz no focus mode), então encolhe
  o host sem cobrir a última linha.

## 8. Open Questions

- Vale expor `Terminal:ScrollbackChars` na tela de Settings? — proposta:
  não neste slice (config-only); a tela de settings já está em tabs.
- Cap ideal do cockpit read-only (3000 desktop) — revisar após uso
  real de runs longos.
