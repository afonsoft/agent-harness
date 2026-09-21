# SPEC-20260921-terminal-paste-dedup

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-paste-dedup` |
| Type | `Bugfix` |
| Stack | `.NET 10 / Blazor WASM / xterm.js (JS interop)` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-terminal-paste-dedup` |
| Ticket | [#271](https://github.com/afonsoft/agent-harness/issues/271) |
| Status | `Done` |

## 1. User Story

**As a** usuário do Harness
**I want** que Ctrl+V (e Shift+Insert / menu de contexto) no Terminal cole o conteúdo uma única vez
**So that** comandos e textos colados não chegam duplicados ao shell.

**Problem context:**
Colar com Ctrl+V na página `/terminal` envia o conteúdo **duas vezes** ao PTY. Causa raiz em `src/Taskboard.Client/wwwroot/js/terminal.js`:

1. `attachCustomKeyEventHandler` intercepta o `keydown` de Ctrl+V → `pasteClipboard()` chama `navigator.clipboard.readText().then(t => term.paste(t))` → `term.paste` dispara `onData` → `OnTerminalData` → `hub.Input` → PTY (**paste nº 1**).
2. Retornar `false` no handler apenas suprime o *processamento de tecla interno* do xterm — **não** chama `ev.preventDefault()` no DOM → o browser ainda dispara o evento `paste` na textarea interna do xterm → o handler nativo de paste do xterm escreve o clipboard via `onData` novamente → PTY (**paste nº 2**).

O mesmo acontece com Shift+Insert (o browser também emite `paste` nativamente). Paste por menu de contexto/Edit→Paste usa só o caminho nativo (não duplica, mas diverge do caminho custom).

Referência: `terminal.js:17-27` (`pasteClipboard`), `terminal.js:47-49` (handler Ctrl+V/Shift+Insert). xterm.js registra listener `paste` na sua textarea (`Clipboard`/`Browser` no bundle vendored `lib/xterm/xterm.js`).

## 2. Scope

**In scope:**
- Caminho único de paste: interceptar o evento DOM `paste` (capture) no host do terminal, `preventDefault()` + `stopPropagation()`, ler `event.clipboardData.getData('text/plain')` e chamar `term.paste()` **uma** vez.
- Cobertura uniforme: Ctrl+V, Ctrl+Shift+V, Shift+Insert, menu de contexto e Edit→Paste — todos convergem no evento `paste`.
- Remover o uso de `navigator.clipboard.readText()` para paste (mantém funcionamento em contexto não-seguro — `clipboardData` do evento não exige permissão).
- Manter Ctrl+C: copiar quando há seleção, `\x03` (SIGINT) quando não há.

**Out of scope:**
- `initReadOnly` (cockpit run terminal — sem stdin, sem paste).
- Mudanças no `TerminalHub`/`PtySession`/C# — o transporte está correto.
- Bracketed paste toggle configurável (o `term.paste` já aplica o wrapping padrão do xterm).
- Paste com confirmação para conteúdo multilinha (feature futura; hoje multilinha já vai direto).

## 3. Technical Context

**Where the change happens:** `src/Taskboard.Client/wwwroot/js/terminal.js` apenas — funções `pasteClipboard`/`attachKeys`/`init`. Nenhuma mudança em `.razor`, `.cs` ou contratos.

**Files to read before implementing:**
- `src/Taskboard.Client/wwwroot/js/terminal.js` — `attachKeys` (29-53), `init` (55-89), `dispose` (180-190 — listener novo precisa sair no dispose)
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor` — `OnTerminalData` (329-337) consome `onData`
- `src/Taskboard.Client/wwwroot/index.html` — ordem de scripts (xterm vendored antes de `terminal.js`)
- `src/Taskboard.Client/wwwroot/lib/xterm/xterm.js` — bundle vendored (referência de comportamento do paste interno)

**Files to create or modify:**
```text
src/Taskboard.Client/wwwroot/js/terminal.js   [mod]
```

**Fatos confirmados:**
- `attachCustomKeyEventHandler(ev => …)` retornando `false` não impede o `paste` event do browser — é apenas filtro do key handling do xterm.
- `term.paste(text)` → `onData` → `OnTerminalData` → `hub.InvokeAsync("Input", …)` → `PtySession.Write` — caminho único desejado.
- `event.clipboardData.getData('text/plain')` está disponível no handler de `paste` sem `navigator.clipboard` e sem permissão — cobre `http://` (non-secure context) onde `readText` falha.

## 4. Requirements

### RF-001: Paste único via evento `paste`
- **Description:** `init()` registra `el.addEventListener('paste', handler, { capture: true })`; o handler faz `ev.preventDefault()`, `ev.stopPropagation()`, lê `ev.clipboardData.getData('text/plain')` (fallback `'text'`) e, se não vazio, `term.paste(text)` — exatamente uma vez.
- **Input → Output:** Ctrl+V com clipboard `"kubectl get pods"` → PTY recebe `kubectl get pods` 1×.

### RF-002: Keydown de paste deixa de colar
- **Description:** o handler de keydown não chama mais `pasteClipboard`/`readText`; Ctrl+V/Shift+Insert retornam `true` (a tecla segue para o browser, que emite o `paste` event — capturado por RF-001). Ctrl+C mantém a lógica de cópia atual.
- **Rules:** nenhum caminho de paste pode existir fora do handler de `paste` event — nada de `term.paste`/`onData` manual no keydown.

### RF-003: Dispose limpa o listener
- **Description:** `dispose(elementId)` remove o listener de `paste` registrado em `init` (referência guardada no entry do `terms` map) — sem leaks ao fechar tabs.

### RF-004: Clipboard vazio/não-texto
- **Description:** `clipboardData` vazio ou ausente → handler não escreve nada (sem warn no terminal — o comportamento atual de escrever `[paste indisponível…]` é removido junto com o caminho `readText`; em non-secure context o paste event funciona, então o warning deixa de ser necessário).

**Business rules / invariants:**
- Um gesto de paste = uma escrita no PTY — medido por `onData` disparado exatamente 1× por evento de paste.
- Comportamento fora do paste (teclas, resize, copy) inalterado.

## 5. API Contract

N/A — sem mudança de API, contrato JS↔C# (`taskboardTerminal.*`, `OnTerminalData`/`OnTerminalResize`) preservado.

## 6. Acceptance Criteria

- [ ] **Given** terminal aberto **when** pressiono Ctrl+V com texto no clipboard **then** o texto aparece 1× na sessão e o PTY recebe 1 input.
- [ ] **Given** terminal aberto **when** pressiono Shift+Insert **then** paste único.
- [ ] **Given** terminal aberto **when** uso menu de contexto → Paste **then** paste único (mesmo caminho).
- [ ] **Given** app em `http://` (non-secure) **when** Ctrl+V **then** paste funciona via `clipboardData` (sem `navigator.clipboard`).
- [ ] **Given** seleção ativa **when** Ctrl+C **then** copia a seleção e não envia `\x03`; sem seleção, `\x03` chega ao PTY (inalterado).
- [ ] **Given** tab fechada **when** reabro e colo **then** paste único (listener re-registrado, sem duplicar handlers).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Clipboard com imagem/binário | paste event sem texto | nada é escrito |
| Clipboard multilinha | `cmd1\ncmd2` | `term.paste` aplica bracketed paste (1 evento onData) |
| Paste durante reconexão do hub | paste com hub down | `OnTerminalData` descarta silenciosamente (guarda já existe em `Terminal.razor:333`) |
| Reattach após reconnect | sessão re-anexada | listener continua no host element (sobrevive — é DOM, não SignalR) |

## 7. Task Plan (agent execution)

- [ ] **T1 — Reprodução:** log/`console.count` em `onData` ou sniff no `Input` do hub confirmando 2 writes por Ctrl+V (evidência do bug).
- [ ] **T2 — Fix:** handler de `paste` event (capture) + remoção do caminho `readText` no keydown + cleanup em `dispose`.
- [ ] **T3 — Verification:** `dotnet build` limpo; verificação manual dos ACs (Ctrl+V, Shift+Insert, context menu, non-secure); sem regressão em Ctrl+C/resize/tabs.
- [ ] **T4 — Done + PR:** DoD completo → `Status = Done` → PR em `feature/devin-20260921-terminal-paste-dedup`.

## 8. Organization Guardrails

- **Branches:** nunca commit em `main`/`master`/`develop`.
- **Workflows:** não editar `.github/workflows/**`.
- **Escopo:** mudança cirúrgica em `terminal.js`; sem alterar o xterm vendored nem o transporte SignalR.
- **Specs:** comportamento de clipboard documentado na linha de ajuda da página (texto já diz "Ctrl+V / Shift+Insert paste" — ajustar se necessário).

## 9. Definition of Done

- [ ] RF-001…RF-004 implementados; paste = 1 write por gesto em todos os caminhos.
- [ ] `dotnet build` limpo; suite de testes verde; cobertura ≥ gate vigente.
- [ ] Verificação manual documentada no PR (browsers: Chrome + Firefox).
- [ ] Guardrails respeitados.

## Open Questions / Pending Ambiguity

- Nenhuma bloqueante. Nota: sem runner de testes JS no repo — verificação é manual + revisão; se desejado, extrair `handlePasteEvent(ev, term)` como função pura testável é opcional.
