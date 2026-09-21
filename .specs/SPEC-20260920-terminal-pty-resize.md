# SPEC-20260920-terminal-pty-resize

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-pty-resize` |
| Type | `Bugfix` (PTY resize via ioctl + client fit guards + session reattach) |
| Stack | `.NET 10 / P/Invoke libc ioctl / xterm.js / Blazor WASM / SignalR` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260920-recurring-jobs-terminal-resize` |
| Ticket | — |
| Status | `Done` |

---

## 1. User Story

**As a** usuário do `/terminal`
**I want** que redimensionar a janela redimensione o PTY silenciosamente e com dimensões corretas
**So that** não aparece texto `stty cols N rows M` digitado no shell e o layout não quebra durante ajustes de janela.

### Problem Context

1. **`stty` vazando como texto:** `PtySession.ResizeAsync` injeta `stty cols {c} rows {r}\n` no stdin do PTY. O comando ecoa na tela (visível ao usuário), só executa quando o bash está lendo stdin — com vim/top/agente em foreground o resize **nunca acontece** (texto fica enfileirado e pode corromper o input do app).
2. **Dimensões degeneradas:** `FitAddon` roda em elemento oculto/`d-none` ou em transição de layout → `proposeDimensions` retorna lixo (`rows 3`), que é enviado ao servidor e vira `stty` visível. ResizeObserver dispara múltiplas vezes com os mesmos valores → tempestade de `stty`.
3. **`Session ended: connection lost` recorrente:** sessões PTY são amarradas ao `connectionId` — qualquer queda do SignalR (abas em background congelam os timers de keep-alive) mata todos os PTYs. O reconnect automático reabria shells novos perdendo processo e buffer.
4. **Última linha cortada:** `.terminal-host { padding: .5rem; box-sizing: border-box }` — o FitAddon lê `getComputedStyle(host).height` (**border-box**, inclui padding+borda) e divide pela altura da célula → computa até 1 linha a mais do que cabe; a linha extra pinta na área de padding e é cortada pelo `overflow: hidden`. Correção: padding mora no `.xterm` (o FitAddon subtrai o padding do elemento explicitamente), não no host.

---

## 2. Scope

### In scope

- **Servidor — resize real via `TIOCSWINSZ`:** resolver o slave pts do processo filho (`/proc/{pid}/task/{pid}/children` → `/proc/{shellPid}/fd/0` → `/dev/pts/N`), abrir o device e `ioctl(TIOCSWINSZ)`. Quando a resolução falha → **no-op** (nunca injetar `stty` no stdin — o PTY ecoa input verbatim).
- **Cliente — guards de fit:** não reportar resize quando oculto/zero-size; dedup (só enviar se cols/rows mudaram); saneidade mínima (cols ≥ 2, rows ≥ 2, e não-enviar quando `proposeDimensions` degenerar).
- **Servidor — session reattach:** disconnect marca sessões como órfãs (PTY vivo) em vez de matar; `Reattach(sessionId)` rebinda callbacks ao novo `connectionId`; sweeper colhe órfãs após grace period.
- **Cliente — reconnect:** em `Reconnected`, cada aba tenta `Reattach` (preserva shell + buffer); falha → reopen (comportamento atual).

### Out of scope

- Persistência de `sessionId` entre reloads de página (reattach cobre apenas reconnect de conexão viva).
- Backend de PTY alternativo (`dotnet-pty`/native) — `script` continua o transporte.
- Buffer de output durante a órfandade — chunks emitidos enquanto desconectado se perdem (shell segue rodando).

---

## 3. Technical Context

### Where the change happens

- `src/Taskboard.Integrations/Terminal/PtySession.cs` — `ResizeAsync` via ioctl + resolução de pts.
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — `SessionEntry` mutável (ConnectionId nullable + OrphanedAtUtc), `OrphanAllForConnectionAsync`, `ReattachAsync`, sweep de órfãs (`OrphanTimeout` = 10min).
- `src/Taskboard.Server/Hubs/TerminalHub.cs` — `OnDisconnectedAsync` → orphan; novo método `Reattach(sessionId)`.
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor` — `Reconnected` → `Reattach` por aba, fallback reopen.
- `src/Taskboard.Client/wwwroot/js/terminal.js` — guards + dedup no observer/fitNow.
- `src/Taskboard.Client/wwwroot/css/site.css` — padding do host → `.xterm` (border-box), elimina a linha cortada no rodapé.
- `tests/Taskboard.Tests.Unit/` — teste de resize real (spawn `script`, `stty size`, sem eco do comando) + orphan/reattach no manager.

### Key conventions

- `winsize` = `{ ushort ws_row, ws_col, ws_xpixel, ws_ypixel }`; `TIOCSWINSZ = 0x5414` (Linux).
- P/Invoke `libc.ioctl` — environment alvo é Linux; fallback `stty` preserva outros OSes.
- Failures de resize são no-op + `LogDebug` (nunca derrubam a sessão).

---

## 4. Requirements

### RF-001: `PtySession.ResizeAsync` via `TIOCSWINSZ`

- **Description:** na primeira chamada, resolve o pts slave: `/proc/{_process.Id}/task/{_process.Id}/children` → primeiro PID filho (shell pós-`exec bash -l`) → `FileInfo`/readlink de `/proc/{childPid}/fd/0` → `/dev/pts/N` → `File.Open` (ReadWrite) → `ioctl(fd, 0x5414, ref winsize)`. Kernel aplica winsize e envia `SIGWINCH` ao process group — apps em foreground (vim/top/agentes) recebem o evento.
- **Rules:** path resolvido é cacheado **somente em sucesso** (falha por spawn-race retenta no próximo resize); falha de resolução/ioctl → no-op + `LogDebug` (injetar `stty` ecoa texto visível — proibido); validação `1..500` mantida; dedup server-side (ignora se igual à última).

### RF-002: Guards no `terminal.js`

- **Description:**
  - `ResizeObserver`: já pula `offsetParent === null`; adicionar skip quando `el.clientHeight === 0` e quando `term.cols/rows` não mudaram desde o último report.
  - `fitNow`: mesma guarda de visibilidade/zero-size.
  - Degenerado: não reportar quando `term.cols < 2 || term.rows < 2`.
- **Rules:** último `(cols,rows)` reportado guardado por entry; `init` já reporta após primeiro fit; padding visual mora em `.terminal-host .xterm` (`box-sizing: border-box`) — nunca em `.terminal-host`, cujo `getComputedStyle().height` é border-box e inflaria a contagem de linhas.

### RF-003: Session reattach pós-reconnect

- **Description:**
  - `TerminalHub.OnDisconnectedAsync` → `OrphanAllForConnectionAsync` (em vez de matar PTYs). Órfã = `ConnectionId = null` + `OrphanedAtUtc`.
  - `TerminalHub.Reattach(sessionId)` → `TerminalSessionManager.ReattachAsync` rebinda `ConnectionId`/`OnOutput`/`OnClosed` ao novo connectionId; exige mesmo `UserKey` e `Session.IsRunning`.
  - `SweepIdleAsync` colhe órfãs após `OrphanTimeout` (10min default) com reason `connection lost`; idle-timeout (30min) segue valendo.
  - Cliente: `Reconnected` → `Reattach` por aba Open; `true` → `fitNow` (buffer preservado); `false` → `ReopenTabAsync` (shell novo).
- **Rules:** órfãs rejeitam `Input`/`Resize`/`Close` até reattach; `Reattach` de outro usuário → `false`; sessão reaped/exited → `false` → reopen no cliente.

---

## 5. Data Model / Contracts

Aditivo de contrato no hub: novo método `Reattach(sessionId) → bool`. `Open/Input/Resize/Close` inalterados.

---

## 6. Acceptance Criteria

- [x] **AC1:** Redimensionar a janela não imprime `stty …` no terminal. *(teste de PTY real: output sem `stty cols`)*
- [x] **AC2:** Com processo em foreground (`top`/`vim`), resize aplica imediatamente (`stty size` dentro da sessão reflete novos cols/rows). *(`stty size` → `41 163` após `ResizeAsync(163,41)`)*
- [x] **AC3:** Aba oculta ou layout em transição não gera resize de dimensões degeneradas (`rows 3`/`rows 0` nunca chegam ao servidor). *(guards `offsetParent`/zero-size/min 2/dedup no `terminal.js`)*
- [x] **AC4:** Aba em background reconecta sem `Session ended: connection lost` — `Reattach` restaura a sessão com o mesmo `sessionId`, processo vivo e buffer preservado. *(testes de manager: orphan → reattach → input/output rebindados)*
- [x] **AC5:** Sessão órfã sem reconnect por >10min é colhida pelo sweeper (sem leak de PTY). *(sweep com `OrphanTimeout` zero → `connection lost` + dispose)*

---

## 7. Implementation Tasks

- [x] **T1:** ioctl path + resolução de pts + fallback no-op em `PtySession`.
- [x] **T2:** guards/dedup em `terminal.js`.
- [x] **T3:** teste de resize real (sem eco, `stty size` correto) + unit.
- [x] **T4:** orphan/reattach no `TerminalSessionManager` + `TerminalHub` + `Terminal.razor`; sweep de órfãs; testes de ciclo de vida.

## 8. Non-Goals / Guardrails

- Resize fora de Linux é no-op (nunca injeta `stty` — echo é o bug); resize inicial do spawn continua via `stty` dentro do `script -c`, que não ecoa.
- Nenhum resize pode matar a sessão ou o pump de output.
- `Reattach` exige mesmo `UserKey` — sessão nunca migra entre usuários.
