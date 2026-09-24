# SPEC-20260923-terminal-virtual-keybar

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-virtual-keybar` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WASM / xterm.js / SignalR` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260923-terminal-virtual-keybar` |
| Ticket | [#340](https://github.com/afonsoft/agent-harness/issues/340) (Epic [#338](https://github.com/afonsoft/agent-harness/issues/338)) |
| Status | `In implementation` |
| Gap | `GAP-frontend-terminal-virtual-keybar` (gap-analysis-20260923) |
| Depends on | `SPEC-20260923-terminal-focus-mode` (a keybar só existe dentro do modo focus) |
| Related | `SPEC-20260917-terminal-tabs`, `SPEC-20260921-terminal-paste-dedup`, `SPEC-20260918-touch-targets` |

## 1. User Story

**As a** usuário do Harness operando o `/terminal` em tela cheia num celular/tablet,
**I want** uma barra inferior de teclas virtuais com as teclas que o teclado do celular não tem — setas, Esc, Tab, Shift+Tab, Ctrl, Home/End, Page Up/Down e colar,
**So that** eu consiga navegar histórico, autocompletar, interromper processos e colar comandos sem teclado físico.

**Problem context:**
Teclados virtuais mobile não oferecem `Esc`, `Tab`, setas, `Ctrl`/`Shift+Tab` nem atalhos de terminal. Hoje toda a entrada do `/terminal` vem do teclado via `xterm.onData` → hub `Input`; não existe nenhuma UI que envie sequências de escape ou modificadores — impossível usar `Ctrl+C`, `↑` (histórico) ou `Tab` (completion) no celular. O paste mobile também depende do menu de contexto do SO porque não há botão.

## 2. Scope

**In scope:**
- Barra de teclas virtuais na página `/terminal`, renderizada **somente** quando as duas condições se combinam: modo focus ativo (`html[data-terminal-focus]`) **e** dispositivo touch (`@media (pointer: coarse), (hover: none)`).
- Teclas dedicadas: `Esc`, `Tab`, `Shift+Tab`, `←`, `↑`, `↓`, `→`, `Home`, `End`, `PgUp`, `PgDn`.
- Modificador `Ctrl` **sticky**: um toque arma o modificador (estado visual ativo); a próxima tecla enviada é combinada com Ctrl e o modificador desarma.
- Botão dedicado **Paste** (`Ctrl+V` equivalente): `navigator.clipboard.readText()` → `term.paste()`, com fallback quando a clipboard API não existe (contexto não-seguro) — segue o padrão de fallback já documentado em SPEC-20260917-terminal-tabs RF-004.
- Envio via hub `Input(sessionId, data)` da aba ativa — nenhum contrato novo.
- Refocus do xterm após cada toque (o botão não pode roubar o foco do terminal permanentemente).
- Alvos de toque ≥44px e barra rolável horizontalmente se exceder a largura.

**Out of scope:**
- Barra fora do modo focus ou em dispositivos sem touch (`pointer: fine`) — requisito explícito do usuário.
- Teclas configuráveis pelo usuário, long-press/repeat, haptics, múltiplos modificadores (Alt, Shift sticky genérico além do Shift+Tab dedicado).
- Suporte a gestos (swipe no terminal para scroll/histórico).
- Terminal read-only do cockpit (`RunTerminal.razor` — sem stdin).
- Paste via `execCommand('paste')` (deprecado) — só `navigator.clipboard.readText` + fallback informativo.

## 3. Technical Context

**Where the change happens** (architecture, layers, integrations):
Frontend-only, mesma fatia do SPEC-20260923-terminal-focus-mode. `Terminal.razor` renderiza `<div class="terminal-keybar">` dentro do overlay focus; a visibilidade é 100% CSS (`html[data-terminal-focus]` + media query coarse) — sem detecção por UA nem JS de device. Cada `<button>` chama um método Blazor `SendVirtualKeyAsync(sequence)` que invoca `_hub.InvokeAsync("Input", _activeTab.SessionId, seq)` — exatamente o caminho de `OnTerminalData`. O `Ctrl` sticky é estado local (`_ctrlArmed`): ao enviar uma tecla de letra/dígito com Ctrl armado, envia o byte de controle (`ch & 0x1f`, ex.: `c`→`\x03`); combinado com `V` dispara o mesmo caminho do botão Paste (equivalente ao `Ctrl+V` documentado na página). Para o paste, um helper JS novo `taskboardTerminal.pasteClipboard(elementId)` usa `navigator.clipboard.readText()` e chama `term.paste(text)` — mesmo destino do handler `attachPaste` existente.

**Files to read before implementing:**
- `AGENTS.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor` (após focus-mode: `_focusMode`, tab strip)
- `src/Taskboard.Client/wwwroot/js/terminal.js` (`attachPaste`, `term.paste`, map de instâncias)
- `src/Taskboard.Client/wwwroot/js/taskboard.js`
- `src/Taskboard.Client/wwwroot/css/site.css` (`.terminal-*`, media query touch em ~1151)
- `.specs/SPEC-20260923-terminal-focus-mode.md` (overlay e ciclo de vida do focus)
- `.specs/SPEC-20260917-terminal-tabs.md` (RF-004 — regras de clipboard)

**Files to create or modify:**
```text
src/Taskboard.Blazor/Components/Pages/Terminal.razor   (keybar + SendVirtualKeyAsync + Ctrl sticky + Paste)
src/Taskboard.Client/wwwroot/js/terminal.js          (pasteClipboard(elementId) — readText → term.paste)
src/Taskboard.Client/wwwroot/css/site.css            (.terminal-keybar rules, media query, sticky-Ctrl state)
docs/features.md · docs/features.pt-br.md            (teclas virtuais, tabela de sequências, fallback de paste)
.specs/SPEC-20260923-terminal-virtual-keybar.md
```

## 4. Requirements

### RF-001: Visibilidade da keybar
- **Description:** A barra `.terminal-keybar` só é visível com `html[data-terminal-focus]` presente **e** `@media (pointer: coarse), (hover: none)` — display `none` em qualquer outro caso.
- **Rules:** a regra é CSS pura (sem branch de device no C#/JS); desktop com touchscreen (coarse) também a vê em focus — comportamento aceito; fora do focus ela nunca renderiza espaço.
- **Input → Output:** focus + touch → barra visível no rodapé do overlay; caso contrário → ausente.

### RF-002: Teclas de sequência
- **Description:** Botões enviam sequências ANSI para a aba ativa via hub `Input`:

  | Tecla | Sequência | Tecla | Sequência |
  | --- | --- | --- | --- |
  | `Esc` | `\x1b` | `→` | `\x1b[C` |
  | `Tab` | `\t` | `←` | `\x1b[D` |
  | `Shift+Tab` | `\x1b[Z` | `Home` | `\x1b[H` |
  | `↑` | `\x1b[A` | `End` | `\x1b[F` |
  | `↓` | `\x1b[B` | `PgUp` | `\x1b[5~` |
  | | | `PgDn` | `\x1b[6~` |
- **Rules:** envio só quando `_activeTab.SessionId` não-nulo e hub `Connected` — caso contrário no-op silencioso (mesma guarda de `OnTerminalData`); teclas desabilitadas visualmente quando a aba está `Closed`/`Error`.
- **Input → Output:** toque → `Input(sessionId, seq)` → PTY recebe a sequência como se fosse teclado físico.

### RF-003: Ctrl sticky
- **Description:** Botão `Ctrl` arma o modificador por **uma** tecla: visual `active` enquanto armado; ao tocar uma letra/dígito envia o byte de controle (`ch & 0x1f` — `c`→`\x03`, `z`→`\x1a`, `l`→`\x0c`, `d`→`\x04`, `a`→`\x01`, `e`→`\x05`); ao tocar `V` com Ctrl armado executa o mesmo fluxo do botão Paste (Ctrl+V = paste, conforme o hint da página); qualquer outra tecla (setas, Esc, Tab) desarma sem combinar.
- **Rules:** segundo toque no próprio `Ctrl` desarma sem enviar nada; o estado nunca persiste entre abas ou além de uma tecla; `aria-pressed` reflete o estado armado.
- **Input → Output:** `Ctrl`→`C` → `\x03` (SIGINT) no PTY; `Ctrl`→`V` → paste via clipboard; `Ctrl`→`Ctrl` → nada enviado.

### RF-004: Botão Paste
- **Description:** Botão dedicado chama `taskboardTerminal.pasteClipboard(elementId)`: `navigator.clipboard.readText()` → `term.paste(text)` na instância da aba ativa — o mesmo caminho único de paste do `attachPaste` (SPEC-20260921-terminal-paste-dedup).
- **Rules:** o toque no botão é o user-gesture exigido pela clipboard API; se `navigator.clipboard`/`readText` indisponível (HTTP não-seguro) ou a promise rejeita (permissão negada) → fallback: escreve o hint no terminal (`Use o menu do browser para colar`) via `term.paste`/`writeln`, sem exception para o usuário; nunca loga o conteúdo colado.
- **Input → Output:** toque → texto do clipboard no PTY, ou hint visível no terminal.

### RF-005: Foco e ergonomia
- **Description:** Tocar uma tecla não move o foco de forma permanente: após o envio, `taskboardTerminal.focus(elementId)` recoloca o foco no xterm (teclado virtual pode reabrir); botões usam `onpointerdown:preventDefault` ou refocus explícito.
- **Rules:** cada tecla ≥44×44px; barra `overflow-x:auto` com `scrollbar-width:thin`; posição fixa no rodapé do overlay focus, sem sobrepor a última linha do xterm (a keybar reduz o espaço do host — ela está no fluxo do overlay, não sobreposta).
- **Input → Output:** sequência de toques rápidos → cada byte vai ao PTY e o terminal permanece focalizado.

### RF-006: Documentação
- **Description:** `docs/features(.pt-br).md` documentam a keybar: condição de visibilidade (focus + touch), tabela de sequências, comportamento do Ctrl sticky e do Paste.

**Business rules / invariants:**
- Nenhum byte é gerado no cliente além das sequências mapeadas — não há texto livre na keybar.
- `Ctrl` sticky nunca produz estado "preso": sempre desarma após uma tecla.
- Paste nunca registra conteúdo em log/telemetria.
- Nenhuma mudança em `TerminalHub`/API — somente o `Input` existente.

## 5. API Contract

Não se aplica — reutiliza `Input(sessionId, data)` do `terminal-hub` (SPEC-20260917-terminal-tabs §5). Helper JS novo (não-contrato externo): `taskboardTerminal.pasteClipboard(elementId) → Promise<bool>`.

## 6. Acceptance Criteria

- [ ] **Dado** celular (pointer coarse) em modo focus na `/terminal` **quando** a página renderiza **então** a keybar aparece no rodapé do overlay.
- [ ] **Dado** desktop com mouse em modo focus **quando** a página renderiza **então** a keybar NÃO aparece.
- [ ] **Dado** keybar visível e sessão aberta **quando** toca `↑` **então** o PTY recebe `\x1b[A` (histórico sobe um comando).
- [ ] **Dado** keybar visível **quando** toca `Tab` dentro de uma linha **então** `\t` chega ao PTY (completion do bash dispara).
- [ ] **Dado** `Ctrl` armado **quando** toca `C` durante `sleep 60` **então** `\x03` interrompe o processo e o Ctrl desarma.
- [ ] **Dado** `Ctrl` armado **quando** toca `V` com texto no clipboard **então** o texto é colado no PTY (não `\x16`).
- [ ] **Dado** botão Paste em contexto seguro **quando** toca **então** `readText` → `term.paste` insere o texto.
- [ ] **Dado** clipboard API indisponível/negada **quando** toca Paste **então** um hint aparece no terminal e nenhuma exception chega à UI.
- [ ] **Dado** keybar visível **quando** sai do modo focus **então** a barra some junto com o overlay.
- [ ] **Dado** toques rápidos em sequência **quando** 3 teclas seguidas **então** as 3 sequências chegam ao PTY em ordem e o foco permanece no xterm.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Aba `Closed`/`Error` | qualquer tecla | no-op (botões desabilitados), sem exception |
| `Ctrl` → tecla não-letra (seta) | `Ctrl` depois `↑` | desarma; só `\x1b[A` é enviado |
| Troca de aba com `Ctrl` armado | tap na tab | modificador desarma |
| OSK mobile aberta | resize do visualViewport | `ResizeObserver` refita; keybar continua visível acima do OSK |
| `readText` retorna vazio | Paste | nada é enviado, sem hint de erro |
| Sessão cai no meio do toque | hub `Closed` | próximos toques são no-op até Reopen |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** reler `Terminal.razor` pós-focus-mode e `terminal.js`; confirmar mapa de sequências contra xterm/bash.
- [x] **T2 — JS:** `pasteClipboard(elementId)` em `terminal.js` (readText → term.paste; fallback hint; retorna bool).
- [x] **T3 — Razor:** markup `.terminal-keybar` + `SendVirtualKeyAsync` + guarda de sessão + `_ctrlArmed` + refocus.
- [x] **T4 — CSS:** barra no overlay focus (fluxo, não sobreposta), media query `(pointer: coarse),(hover: none)`, alvos ≥44px, estado `Ctrl` armado.
- [x] **T5 — Docs:** `docs/features(.pt-br).md`.
- [x] **T6 — Validation:** `dotnet build`; suíte sem regressão; evidência manual em device/emulação touch: visibilidade, sequências, Ctrl+C, Ctrl+V, fallback de paste.

**7.1 Validation:** .NET — suíte existente sem regressão (mudança frontend); lógica de byte de controle (`ch & 0x1f`) e mapeamento de teclas são unit-testáveis se extraídos para um helper estático — preferível para cobrir RF-002/RF-003 sem device.

## 8. Organization Guardrails

- Branch `feature/devin-20260923-terminal-virtual-keybar` (pode ramificar de `terminal-focus-mode` até o merge deste); nunca commit em `main`/`develop`.
- Sem alteração em `.github/workflows/**`.
- Não logar conteúdo de clipboard/terminal (pode conter tokens).
- Visibilidade restrita: focus + touch apenas — não exibir em outros contextos.
- Nenhum contrato de servidor novo; somente `Input` existente.

## 9. Definition of Done

- [ ] RF-001…RF-006 implementados.
- [ ] Critérios da seção 6 verificados (build + evidência touch; unit tests do mapeamento se helper extraído).
- [ ] Edge cases tratados.
- [ ] `dotnet build` + `dotnet test` verdes.
- [ ] Guardrails respeitados; logs sem conteúdo de clipboard.

**Next action after DoD:** `Status = Done` + PR em `feature/devin-20260923-terminal-virtual-keybar`.

## Open Questions / Pending Ambiguity

- Nenhuma — decidido com o usuário: keybar só em focus + touch (media query CSS), Ctrl sticky + botão Paste dedicado, cockpit fora de escopo, dependência explícita do SPEC-20260923-terminal-focus-mode.
