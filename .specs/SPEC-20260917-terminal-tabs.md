# SPEC-20260917-terminal-tabs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `terminal-tabs` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor WASM / SignalR / xterm.js` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260917-terminal-tabs` |
| Ticket | — |
| Status | `Approved` |

## 1. User Story

**As a** administrador do Taskboard
**I want** abrir várias sessões de terminal em abas, fechar cada aba individualmente e usar Ctrl+C / Ctrl+V e demais teclas como num terminal nativo
**So that** eu possa autenticar/operar múltiplos agent CLIs em paralelo e trabalhar com atalhos de teclado familiares, sem que uma aba destrua a sessão de outra.

**Problem context:**
Hoje `TerminalHub` mantém **uma única `PtySession` por usuário** (`Sessions` keyed por `userKey`): ao conectar, a sessão anterior é removida e descartada (`OnConnectedAsync` → `TryRemove` + dispose). `Input`/`Resize` não recebem identificador de sessão, então não há como rotear I/O para mais de um PTY. No cliente, `terminal.js` instancia um único `Terminal` xterm e repassa `onData` bruto — `Ctrl+C` chega como `\x03` (funciona), mas `Ctrl+V` é interceptado pelo browser e não cola; não há copy-on-select nem handlers de clipboard. A página `/terminal` também não oferece nenhuma UI de abas.

## 2. Scope

**In scope:**
- Multi-sessão no `TerminalHub`: sessões identificadas por `sessionId` multiplexadas na mesma conexão SignalR.
- Extração de um gerenciador de sessões testável (`TerminalSessionManager`) no server.
- UI de abas na página `/terminal` (nova aba, troca de aba, fechar aba, indicador de estado por aba).
- Teclado estilo terminal nativo: `Ctrl+C` (copia se seleção, senão `\x03`), `Ctrl+V`/`Ctrl+Shift+V`/`Shift+Insert` (cola), `Ctrl+Shift+C` copia; demais teclas passam direto.
- Limite de 8 abas por usuário, idle-timeout 30min **por sessão**, `?cmd=` preservado.
- Testes unitários do roteamento/lifecycle + build.

**Out of scope:**
- Persistência/reattach de scrollback server-side (sessões morrem com o processo, como hoje).
- Split panes, temas, font-size configurável, drag-reorder de abas.
- Reconexão automática recriando shells mortas (aba morta mostra ação explícita de "Reopen").
- Compartilhamento de sessão entre usuários/dispositivos.
- Interceptar `Ctrl+T`/`Ctrl+W`/`Ctrl+N` — o browser reserva essas teclas e não há workaround confiável (documentado como limitação).

## 3. Technical Context

**Where the change happens:**
`TerminalHub` (Server) passa a delegar para um singleton `TerminalSessionManager` que mantém `ConcurrentDictionary<userKey, ConcurrentDictionary<sessionId, SessionEntry>>`. O Blazor mantém uma lista de abas; cada aba possui `sessionId`, `elementId` (`terminal-host-{n}`) e estado (`connecting/open/closed/error`). `terminal.js` mantém `Map<elementId,{term,fit,observer}>` em vez de singleton.

**Files to read before implementing:**
- `AGENTS.md`
- `src/Taskboard.Server/Hubs/TerminalHub.cs`
- `src/Taskboard.Integrations/Terminal/PtySession.cs` e `PtySessionFactory.cs`
- `src/Taskboard.Blazor/Components/Pages/Terminal.razor`
- `src/Taskboard.Client/wwwroot/js/terminal.js`
- `src/Taskboard.Client/wwwroot/css/app.css` (`.terminal-host` e estilos de console)
- `tests/Taskboard.Tests.Unit/Integrations/Terminal/PtySessionTests.cs`
- `.specs/SPEC-20260917-cli-agents-terminal.md`

**Files to create or modify:**
```text
src/Taskboard.Server/Terminal/TerminalSessionManager.cs      (novo — registry/lifecycle por usuário)
src/Taskboard.Server/Hubs/TerminalHub.cs                     (delega ao manager; métodos com sessionId)
src/Taskboard.Blazor/Components/Pages/Terminal.razor         (UI de abas + roteamento por sessionId)
src/Taskboard.Client/wwwroot/js/terminal.js                  (Map de terms + clipboard/keyboard handlers)
src/Taskboard.Client/wwwroot/css/app.css                     (estilos da tab strip)
src/Taskboard.Server/Program.cs                              (DI do TerminalSessionManager)
tests/Taskboard.Tests.Unit/Server/TerminalSessionManagerTests.cs (novo)
tests/Taskboard.Tests.Integration/TerminalHubTests.cs        (novo ou extensão)
docs/features.md · docs/features.pt-br.md · docs/api.md · docs/api.pt-br.md
.specs/SPEC-20260917-terminal-tabs.md
```

## 4. Requirements

### RF-001: `TerminalSessionManager` (registry multi-sessão)
- **Description:** Serviço singleton no Server que cria, localiza, enumera e fecha `PtySession`s por `(userKey, sessionId)`; gera `sessionId` (GUID string); emite eventos `Output(sessionId,chunk)` e `Closed(sessionId,reason)` consumidos pelo hub via `IHubContext`.
- **Rules:** máx. `MaxSessionsPerUser = 8` por usuário — `Open` além disso lança `HubException("Maximum of 8 terminal sessions per user")`; cada sessão tem `IdleWatch` próprio (30min); `CloseAll(connectionId)` no disconnect só fecha sessões daquela conexão; sessões de outras conexões do mesmo usuário **não** são derrubadas por uma nova aba (remove o comportamento "replaced" entre abas — mantido apenas quando a **mesma connectionId** é reusada).
- **Input → Output:** `Open(userKey, connectionId) → sessionId`; `Input(sessionId,data)`; `Resize(sessionId,cols,rows)`; `Close(sessionId)`; `CloseAllForConnection(connectionId)`.

### RF-002: Contrato do hub com `sessionId`
- **Description:** `TerminalHub` expõe `Task<string> Open()`, `Task Input(string sessionId, string data)`, `Task Resize(string sessionId,int cols,int rows)`, `Task Close(string sessionId)`. Callbacks para o cliente: `output(sessionId, chunk)` e `closed(sessionId, reason)` (`reason` ∈ `exited | idle-timeout | closed | error`).
- **Rules:** `Input`/`Resize`/`Close` ignoram `sessionId` desconhecido ou de outra conexão (no-op seguro); flag `Taskboard:Terminal:Enabled=false` continua rejeitando a conexão; métodos não validados nunca executam nada fora do registry.
- **Input → Output:** vide assinaturas; cliente recebe `output`/`closed` sempre com `sessionId`.

### RF-003: UI de abas na `/terminal`
- **Description:** Tab strip acima do host: cada aba mostra `Terminal N` + estado (dot verde=cinza por status) + botão **✕**; botão **+** cria nova aba (desabilitado no limite, com tooltip); clicar troca a aba visível — cada aba tem seu próprio container xterm (scrollback preservado no cliente ao alternar).
- **Rules:** ao fechar a última aba, mostra estado vazio com botão "New terminal"; `?cmd=` continua abrindo **uma** aba e enviando o comando (fluxo de Login dos agents inalterado); aba cuja sessão morreu (`closed`) mostra banner interno "Session ended: {reason}" + botão **Reopen** que cria `Open()` novo **naquela aba** (reusa elementId/xterm, limpa buffer); hub `Closed`/perda de conexão marca todas as abas `closed` sem destruir os elementos.
- **Input → Output:** interações de mouse/teclado → chamadas ao hub e troca de `display` dos containers.

### RF-004: Teclado estilo terminal
- **Description:** `attachCustomKeyEventHandler` por instância xterm implementando: `Ctrl+C` → se `term.hasSelection()` copia seleção via `navigator.clipboard.writeText` e retorna `false`; sem seleção retorna `true` (envia `\x03`); `Ctrl+V` e `Ctrl+Shift+V` e `Shift+Insert` → `navigator.clipboard.readText().then(t=>term.paste(t))` e retorna `false`; `Ctrl+Shift+C` → sempre copia seleção; demais eventos retornam `true` (pass-through nativo — F1–F12, Ctrl+Z, Ctrl+L, setas já funcionam via `onData`).
- **Rules:** se `navigator.clipboard` indisponível (contexto não-seguro HTTP), fallback: `Ctrl+V` sem clipboard API mostra hint no console (`Use o menu do browser ou Ctrl+Shift+V`) e não engole a tecla; copy nunca loga conteúdo.
- **Input → Output:** KeyboardEvent → ação de clipboard/paste ou `\x03` para o PTY.

### RF-005: Limites e timeouts
- **Description:** `MaxSessionsPerUser=8` e `IdleTimeout=30min` por sessão, constantes internas testáveis; `LastActivityUtc` atualizado em Input/output como hoje.
- **Input → Output:** excesso → `HubException`; inatividade → `closed(sessionId,"idle-timeout")`.

### RF-006: Documentação
- **Description:** `docs/features(.pt-br).md` descrevem abas/atalhos/limitação Ctrl+T|W|N; `docs/api(.pt-br).md` documentam as novas assinaturas do hub.

## 5. API Contract

**Hub:** `POST /terminal-hub/negotiate` + WebSocket (SignalR, auth cookie existente)

| Método | Args | Retorno |
| --- | --- | --- |
| `Open` | — | `string sessionId` |
| `Input` | `sessionId, data` | — |
| `Resize` | `sessionId, cols, rows` | — |
| `Close` | `sessionId` | — |

**Client callbacks:** `output(string sessionId, string chunk)`, `closed(string sessionId, string reason)`.

**Expected errors:** `HubException("Maximum of 8 terminal sessions per user")` no `Open` além do limite; `HubException("Terminal disabled")` quando a flag desativa.

## 6. Acceptance Criteria

- [ ] **Dado** usuário autenticado na `/terminal` **quando** clica **+** duas vezes **então** existem 3 PTYs ativas e digitar em cada aba vai para a sessão correta.
- [ ] **Dado** 2 abas abertas **quando** fecha a aba 1 **então** seu PTY é disposto no server e a aba 2 continua recebendo output.
- [ ] **Dado** texto selecionado **quando** `Ctrl+C` **então** o clipboard recebe o texto e nenhum `\x03` é enviado.
- [ ] **Dado** nenhuma seleção **quando** `Ctrl+C` **então** `\x03` chega ao PTY (interrompe `sleep 60`).
- [ ] **Dado** clipboard com texto e contexto seguro **quando** `Ctrl+V` **então** o texto é colado no PTY.
- [ ] **Dado** 8 abas abertas **quando** tenta a 9ª **então** `HubException` e mensagem amigável na UI.
- [ ] **Dado** aba inativa 30min **quando** expira **então** `closed(idle-timeout)` só naquela aba.
- [ ] **Dado** `/terminal?cmd=claude` **quando** abre **então** uma aba recebe o comando (comportamento atual preservado).
- [ ] **Dado** perda de conexão **quando** `closed`/hub `Closed` **então** todas as abas exibem estado dead com opção de Reopen.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `Input` com sessionId de outro usuário/conexão | id válido mas dono diferente | no-op, sem exceção |
| Fechar aba já morta | `Close(id)` idempotente | no-op |
| `navigator.clipboard` ausente (HTTP não-seguro) | `Ctrl+V` | hint no terminal, tecla não engolida |
| Resize de aba escondida | troca de aba → `fit()` | recalcula cols/rows ao exibir |
| `?cmd=` com aba já existente | nova navegação | nova aba com o comando |

## 7. Task Plan

- [ ] **T1 — Manager:** `TerminalSessionManager` (registry por user/session, idle watch, cap, eventos) + DI + testes unitários (open/route/close/cap/idle/close-all-per-connection).
- [ ] **T2 — Hub:** novas assinaturas com `sessionId`, callbacks roteados, remoção do kill-on-reconnect por usuário; testes de integração do hub.
- [ ] **T3 — JS:** `Map` de instâncias xterm por elementId + `attachCustomKeyEventHandler` (copy/paste) + `focus/write/dispose` por id.
- [ ] **T4 — UI:** tab strip + estado por aba + `?cmd=` + reopen; CSS.
- [ ] **T5 — Validação:** `dotnet build` + `dotnet test` completos; smoke manual (2 abas, Ctrl+C copy/SIGINT, Ctrl+V paste, idle, limite).
- [ ] **T6 — Docs/PR:** docs bilíngues, SPEC `Done`, PR, merge, deploy (`dotnet publish` + `systemctl --user restart taskboard-server`).

**7.1 Validation:** .NET — unit tests para RF-001/002/005; integration tests do hub; manual para RF-003/004 (JS).

## 8. Organization Guardrails

- Branch `feature/devin-20260917-terminal-tabs`; nunca commit em `main`.
- Sem alteração em `.github/workflows/**`.
- Não logar conteúdo de terminal (pode conter tokens digitados).
- Sessões PTY só para usuários autenticados; nenhum input arbitrário vira processo fora do PTY bash já existente.

## 9. Definition of Done

- [ ] RF-001…RF-006 implementados.
- [ ] Critérios da seção 6 cobertos por testes/evidência.
- [ ] Edge cases tratados.
- [ ] Build + testes verdes localmente.
- [ ] Guardrails respeitados; logs sem conteúdo de terminal/PII.

**Next action:** `Status = Done` + PR.

## Open Questions / Pending Ambiguity

- Nenhuma — escopo confirmado com o usuário (abas ≤8, idle 30min/aba, clipboard com fallback, `?cmd=` preservado).
