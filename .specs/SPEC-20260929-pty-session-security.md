# SPEC-20260929-pty-session-security

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `pty-session-security` |
| Type | `Security / Backend + Frontend` |
| Stack | `.NET 10 / Blazor WASM / PTY` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-pty-session-security` |
| Ticket | [#367](https://github.com/afonsoft/agent-harness/issues/367) (Epic [#366](https://github.com/afonsoft/agent-harness/issues/366)) |
| Status | `Done` |
| Related | `SPEC-20260928-ai-code-generic-cli` (PR #353 — origem dos achados) |

## 1. User Story

**As a** operador do Harness multiusuário
**I want** que sessões PTY só possam ser reassociadas ao seu dono, que o contexto Docker seja validado contra contêineres autorizados e que o ciclo de vida do pane de terminal não misture sessões
**So that** um usuário autenticado não possa seqüestrar o terminal de outro usuário, executar em contêineres arbitrários, nem ver scrollback/saída cruzada entre threads.

**Problem context:**

Achados de segurança e ciclo de vida reportados pelo Devin Review no PR #353 (todos confirmados no código atual):

1. 🟥 **Rebind ignora o proprietário** — `TerminalSessionManager.OpenAsync` (`src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs:~102-115`): quando `requestedSessionId` já existe, a sessão é reassociada à nova conexão **sem comparar `bound.UserKey == userKey`**. Como os IDs são determinísticos (`t-<threadId>`), outro usuário autenticado que conheça/adivinhe o threadId pode rebindar a sessão, receber a saída e enviar comandos ao PTY alheio.
2. 🟨 **ContainerContext não valida contêiner autorizado** — `ThreadPtyResolver` (`src/Taskboard.Server/Services/ThreadPtyResolver.cs:~90-97`): o nome persistido passa apenas por validação sintática (`DockerCliSpawner.IsValidContainerName`) antes de `docker exec`. Um cliente pode indicar qualquer contêiner acessível ao daemon Docker, mesmo fora do seletor da UI.
3. 🔴 **Troca de thread PTY mistura sessões** — `PtyThreadPane` é renderizado sem `@key` (`src/Taskboard.Blazor/Components/Pages/AiChat.razor:~87-90`). Blazor reutiliza a instância ao trocar de thread PTY; `ConnectHubAsync` é chamado de novo sem descartar conexão/handlers/xterm anteriores — callbacks duplicados e saída misturada.
4. 🟡 **Scrollback some após refresh** — no rebind, o servidor emite o replay via `OnOutput` antes de retornar o sessionId; `PtyThreadPane` descarta eventos cujo ID ≠ `_sessionId` (ainda nulo até `OpenForThread` responder).
5. 🔍 **`AppendScrollback` fora do lock** — o buffer de scrollback é modificado fora do lock usado nas leituras; saída contínua simultânea a um reattach pode perder/reordenar dados.
6. 🔍 **Diretório inicial inconsistente** — sem `WorkspacePath` o terminal usa `UserProfile`, enquanto `EnsureSessionAsync` usa o workspace configurado; instalações com `WorkspaceRoot` custom divergem.

## 2. Scope

**In scope:**

- Checagem de `UserKey` no caminho de rebind de `OpenAsync` (e em qualquer outro caminho que reassocie `ConnectionId`/callbacks).
- Validação de `ContainerContext` contra a lista de contêineres conhecidos/autorizados (descoberta via `DockerCliDiscovery.ListContainersAsync` ou configuração explícita), no servidor.
- `@key` por thread (ou dispose explícito de conexão/handlers/xterm) em `PtyThreadPane`.
- Ordem de replay: garantir que o cliente só processe scrollback após associar `_sessionId` (id determinístico no cliente antes de invocar, ou replay retornado na resposta do open).
- Lock coerente para `AppendScrollback` vs leituras.
- Alinhar workdir default do terminal ao `WorkspaceRoot` configurado.
- Testes cobrindo cada correção.

**Out of scope:**

- Mudança de protocolo do hub/endpoint além do necessário para ownership.
- Container-only CLI discovery e `docker exec` path resolution — SPEC-20260929-docker-cli-context.
- AuthN/AuthZ geral do servidor (API key já exigida).

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — rebind, `AppendScrollback`, `UserKey`.
- `src/Taskboard.Server/Services/ThreadPtyResolver.cs` — validação de `ContainerContext`.
- `src/Taskboard.Server/Services/AgentSessionManager.cs` — workdir coerente (leitura).
- `src/Taskboard.Blazor/Components/AiChat/PtyThreadPane.razor` + `AiChat.razor` — `@key`, `_sessionId`, dispose.
- `tests/` — unit (rebind UserKey, allowlist) + guards/bUnit se aplicável.

**Files to read before implementing:**

- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` (OpenAsync, rebind, Detach, scrollback)
- `src/Taskboard.Server/Services/ThreadPtyResolver.cs`
- `src/Taskboard.Integrations/Agents/DockerCliDiscovery.cs` (fonte da allowlist)
- `src/Taskboard.Integrations/Agents/DockerCliSpawner.cs`
- `src/Taskboard.Blazor/Components/AiChat/PtyThreadPane.razor`
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` (~L80-95 render do pane)

## 4. Requirements

### RF-001: Rebind exige ownership

- **Description:** No caminho de rebind de `OpenAsync`, exigir `bound.UserKey == userKey`; mismatch → falha explícita (nova sessão não é retornada; erro ou novo id). Mesmo critério em qualquer outro caminho que reassocie callbacks/ConnectionId.

### RF-002: ContainerContext contra allowlist

- **Description:** Antes de construir `docker exec`, validar `thread.ContainerContext` contra os contêineres retornados por `DockerCliDiscovery` (ou lista configurada). Nome desconhecido → rejeição com mensagem clara. Manter `IsValidContainerName` como primeira barreira.

### RF-003: Isolamento do pane PTY por thread

- **Description:** `@key` no `PtyThreadPane` pelo `Id` da thread (recriação ao trocar) **ou** teardown explícito de conexão/handlers/xterm antes de associar nova thread. Sem callbacks duplicados nem xterm órfão.

### RF-004: Replay de scrollback sem race

- **Description:** O histórico de uma sessão PTY viva deve ser exibido após refresh da página. Opções: cliente gera o sessionId determinístico antes de chamar `OpenForThread` e aceita eventos com esse id; ou servidor retorna scrollback na resposta e o cliente escreve após associar o id. Preservar ordem entre replay e saída ao vivo.

### RF-005: Scrollback thread-safe

- **Description:** `AppendScrollback` e as leituras de `Scrollback` devem usar o mesmo mecanismo de sincronização (lock ou coleção concorrente com snapshot consistente).

### RF-006: Workdir inicial coerente

- **Description:** O workdir default do PTY de thread segue a mesma regra de `EnsureSessionAsync` (workspace configurado), não `UserProfile`, quando `WorkspaceRoot` está configurado.

## 5. API Contract

Sem mudança de contrato público; o rebind passa a falhar para usuário não-dono (comportamento corretivo, não breaking de API bem-formada).

## 6. Acceptance Criteria

- [ ] **Given** usuário B autenticado **when** tenta rebind da sessão PTY do usuário A (mesmo `requestedSessionId`) **then** o rebind é recusado (teste unitário verde).
- [ ] **Given** `containerContext` fora da lista de contêineres conhecidos **when** a thread PTY é resolvida **then** a resolução falha com erro claro (teste verde).
- [ ] **Given** alternância PTY A→B→A **when** o pane re-renderiza **then** nenhum handler/terminal duplicado permanece (guard ou teste bUnit).
- [ ] **Given** scrollback existente + refresh **when** a página reabre a thread **then** o histórico é exibido (teste do caminho de replay/ordenação).
- [ ] **Given** build/test completos **then** verde, 0 warnings.

## 7. Task Plan (agent execution)

- [ ] **T1 — Ownership:** checagem de `UserKey` no rebind + teste de recusa.
- [ ] **T2 — Allowlist:** validação de `ContainerContext` no resolver + teste.
- [ ] **T3 — Pane:** `@key`/dispose + teste/guard.
- [ ] **T4 — Replay/lock:** correção do race e do `AppendScrollback` + testes.
- [ ] **T5 — Workdir:** alinhamento + teste.
- [ ] **T6 — Done + PR:** `Status = Done` com referência ao PR.

## 8. Organization Guardrails

- Camada correta: ownership em `TerminalSessionManager`/`ThreadPtyResolver` (Server/Integrations); nenhuma regra em endpoint.
- Não enfraquecer nenhum check existente (`IsValidContainerName`, session cap, `VERSION_CONFLICT`).
- `.github/workflows/**` intocado.

## 9. Definition of Done

- [ ] Rebind cross-user bloqueado e testado.
- [ ] ContainerContext validado e testado.
- [ ] Sem vazamento de pane/handlers e scrollback preservado.
- [ ] Build/test verde; `Status = Done`.
