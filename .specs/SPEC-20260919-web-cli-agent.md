# SPEC-20260919-web-cli-agent

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `web-cli-agent` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor WASM / SSE / JSON-RPC (ACP)` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-web-cli-agent` |
| Ticket | [#153 — E5](https://github.com/afonsoft/taskboard-ai/issues/153) |
| Status | `Approved` |

## 1. User Story

**As a** usuário do Harness
**I want** que a página `/ai-chat` funcione como um Web CLI Agent — uma sessão interativa e persistente com um agente CLI real (OpenCode, Claude, Codex…) que lê/escreve arquivos e executa comandos num workspace, com tool calls e pedidos de permissão renderizados inline no browser
**So that** eu possa dirigir trabalho real de código pela web, com o mesmo modelo de funcionamento do OpenCode (`opencode web`), sem abrir um terminal.

**Problem context:**
Hoje `/ai-chat` conversa apenas com `MockLLMProvider` (texto plano, sem tools, sem efeitos colaterais) e o botão "Run agent" enfileira um CLI **one-shot fire-and-forget** via `AgentOrchestrationService.EnqueueAsync` — sem multi-turn, sem streaming estruturado, sem permissões, sem binding de workspace. O modelo de referência (OpenCode, `anomalyco/opencode`) é server-centric: o servidor possui sessões duráveis bound a um diretório, um agent loop com tools, um stream SSE global de eventos tipados (`tool_call`, `permission.request`, deltas), endpoint de reply de permissão e PTY por WebSocket. O taskboard-ai já possui 80% do substrate necessário — falta a camada de **sessão interativa por thread**.

**Reference architecture (OpenCode, reverse-engineered):**
- `opencode web` → servidor HTTP local + SPA cliente; todo estado no servidor.
- `POST /api/session`, `POST /api/session/:id/prompt` (delivery `steer`|`queue`), `GET /api/session/:id/message`.
- `GET /api/event` — SSE global (`server.connected` + eventos, heartbeat 15s).
- `POST /api/session/:id/permission/:requestID/reply` — approve/deny de tool calls.
- `/api/pty` + WebSocket; `/api/fs/read|list|find`; auth via `OPENCODE_SERVER_PASSWORD`.
- Mensagens compostas de **parts estruturadas** (text, reasoning, tool, patch) — não texto plano.

## 2. Scope

**In scope:**
- Thread em modo `agent`: sessão interativa persistente (um processo CLI por thread) via JSON-RPC/ACP sobre stdio, multi-turn.
- Envio de prompt durante run (`delivery`: `steer` quando o agente suporta, senão `queue`) e cancelamento (botão Stop → `session/cancel` + kill da árvore de processos).
- Eventos tipados no stream SSE existente: `message` (deltas), `reasoning`, `tool_call`, `permission`, `session` (lifecycle), `error`.
- Permissões inline: request → SSE `ai_chat.permission` → UI approve/deny/always → `POST .../permissions/{id}/reply`.
- Binding de workspace por thread: `RepositoryFullName` (resolve via `WorkspaceService` → `~/repos/<repo>`) **ou** diretório customizado clampado ao workspace root.
- Mapeamento do VO `Sandbox` (`read-only`/`workspace-write`/`danger-full-access`) para o permission mode do agente.
- Capability detection por adapter: CLIs sem suporte a sessão caem no modo one-shot atual (resultado postado como eventos).
- Migration EF: `ai_chat_threads` + `Mode`, `AgentType`, `WorkspacePath`, `RepositoryFullName`; `ai_chat_events` + `Kind`, `PayloadJson`.
- Feature flag `Taskboard:WebCliAgent:Enabled` (default off).
- Testes unit + integration; docs en/pt-br.

**Out of scope:**
- Agent loop in-process .NET (LLM provider + tools próprias) — direção futura aprovada (Q1-c), vira spec separada.
- PTY/terminal embutido na página (já existe a página Terminal via `TerminalHub`; tool calls `bash` renderizam inline).
- FS browser dedicado (`/api/fs/*` do OpenCode) — diffs/arquivos aparecem dentro dos tool_call events.
- Multi-usuário, compartilhamento de threads, mobile.
- Novos AgentTypes além dos já suportados por `AgentCliInvocation`.
- Migração de threads `assistant` existentes (modo permanece `assistant`).

## 3. Technical Context

### 3.1 Architecture Overview

```text
Blazor WASM  /ai-chat  (modo agent)
   │  REST  TaskboardClient ──────────────┐
   │  SSE   /api/local/ai/threads/{id}/events (tipos estendidos)
   ▼                                      ▼
Taskboard.Server (Minimal APIs, auth existente)
   ├── AiChatService ............ threads/events/runs (modo assistant — inalterado)
   ├── AgentSessionManager ...... NOVO: registry threadId→IAgentSessionHandle,
   │                              idle reaper (30min), publish SSE + persistência
   ├── PermissionGate ........... NOVO: request→SSE→TCS→reply endpoint→agente
   └── IAgentSessionClient ...... NOVO contrato (Contracts)
           └── AcpSessionClient . NOVO (Integrations): JSON-RPC stdio,
                                   session/new|prompt|cancel, notifications
                                   session/update, session/request_permission
                 └── IAgentAdapter.BuildSessionCommand(agentType, workdir, sandbox)
```

Decisão de engine (Q1-c): **fase 1 = sessão interativa com CLI externo**; o processo agente possui o agent loop e as tools (exatamente como no OpenCode, onde o servidor delega ao runtime do agente). Fase 2 (spec futura): loop in-process .NET reutilizando os mesmos contratos de eventos/UI.

**Where the change happens:** `Taskboard.Application.Contracts` (contratos/DTOs), `Taskboard.Domain` (+colunas/VOs), `Taskboard.EntityFrameworkCore` (migration), `Taskboard.Integrations` (`AcpSessionClient`, adapters), `Taskboard.Server` (endpoints, `AgentSessionManager`, `PermissionGate`), `Taskboard.Blazor` (UI), `Taskboard.Client` (JS SSE já existe).

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — UI atual (sidebar, composer, SSE via `taskboardSse.connect`)
- `src/Taskboard.Application/AiChat/AiChatService.cs` — threads/runs/events
- `src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs` — transporte JSON-RPC stdio a estender para sessões
- `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs` + `AgentCliInvocation` — argv por AgentType
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — eligibility, logs, runs (one-shot, reusado como fallback)
- `src/Taskboard.Integrations/Terminal/TerminalSessionManager.cs` — padrão de registry+idle-timeout a espelhar
- `src/Taskboard.Integrations/Workspace/WorkspaceService.cs` — `ResolveCardWorkdir`, `ClampToHome`, `EnsureRoot`
- `src/Taskboard.Server/Program.cs` — endpoints `local/ai/*`, SSE handler, `RequireAuthorization`
- `src/Taskboard.Server/Services/InMemoryThreadEventStreamService.cs`, `IThreadEventStreamService.cs`
- `src/Taskboard.Domain/Entities/AiChat{Thread,Event,Run}.cs` + VOs (`Sandbox`, `ModelRef`)
- `src/Taskboard.Client/wwwroot/js/taskboard.js` — `taskboardSse.connect`
- `.specs/SPEC-005-ai-chat.md`, `SPEC-20260918-ai-chat-threads.md`, `SPEC-20260911-acp-json-rpc.md`, `SPEC-015-agent-orchestration.md`

**Files to create or modify:**
```text
src/Taskboard.Domain.Shared/Agents/AgentSessionCapability.cs        [new]
src/Taskboard.Domain/Entities/AiChatThread.cs                       [mod: Mode, AgentType, WorkspacePath, RepositoryFullName]
src/Taskboard.Domain/Entities/AiChatEvent.cs                        [mod: Kind, PayloadJson]
src/Taskboard.Domain/ValueObjects/AiChatEventKind.cs                [new]
src/Taskboard.EntityFrameworkCore/Configurations/AiChat*Configuration.cs [mod]
src/Taskboard.EntityFrameworkCore/Migrations/*_AddWebCliAgent.cs    [new]
src/Taskboard.Application.Contracts/AiChat/IAgentSessionClient.cs   [new]
src/Taskboard.Application.Contracts/AiChat/AgentSessionEvents.cs    [new: AgentSessionEvent, ToolCallInfo, PermissionRequestInfo...]
src/Taskboard.Application.Contracts/Agents/IAgentAdapter.cs         [mod: SupportsInteractiveSession, BuildSessionCommand]
src/Taskboard.Application.Contracts/Dtos/*                          [mod/new: thread dto + campos, event dto + Kind/Payload]
src/Taskboard.Integrations/Agents/AcpSessionClient.cs               [new]
src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs           [mod]
src/Taskboard.Server/Services/AgentSessionManager.cs                [new]
src/Taskboard.Server/Services/PermissionGate.cs                     [new]
src/Taskboard.Server/Program.cs                                     [mod: endpoints agent mode]
src/Taskboard.Blazor/Components/Pages/AiChat.razor                  [mod: mode, tool/permission renderers, steer/queue, stop]
src/Taskboard.Blazor/Components/{NewThreadDialog,ToolCallCard,PermissionPrompt}.razor [new/mod]
src/Taskboard.Blazor/Services/TaskboardClient.cs                    [mod]
src/Taskboard.Client/wwwroot/js/taskboard.js                        [mod: event types]
tests/Taskboard.*Tests/...                                          [new]
docs/**                                                             [mod: en + pt-br]
```

## 4. Requirements

### RF-001: Thread em modo agent
- **Description:** `POST /api/local/ai/threads` aceita `mode: "agent"` com `agentType` + (`repositoryFullName` | `workspacePath`). Threads antigas permanecem `assistant`.
- **Rules:** `agentType` deve ser elegível (`IAgentEligibilityService`) e ter `SupportsInteractiveSession`; workdir customizado clampado ao workspace root (`WorkspaceService`); repo resolve via `ResolveCardWorkdir` (root se ausente — clone sob demanda é responsabilidade do fluxo de board existente, não desta spec).
- **Input → Output:** `{ title, mode:"agent", agentType, repositoryFullName|workspacePath, sandbox, model? }` → `201 { thread }` com `mode`, `agentType`, `workspacePath`.

### RF-002: Sessão interativa persistente
- **Description:** `AgentSessionManager` garante no máximo 1 sessão viva por thread: `session/new` no primeiro prompt; processo persiste entre prompts; idle timeout 30min encerra (padrão `TerminalSessionManager`); restart sob demanda no próximo prompt (historial via replay dos eventos persistidos).
- **Input → Output:** prompt em thread sem sessão viva → spawn + `session/new(cwd, sandboxMode)` → evento `session` (`starting`→`ready`).

### RF-003: Prompt com steer/queue
- **Description:** `POST /api/local/ai/threads/{id}/prompt` `{ text, delivery? }`. Com run ativo: `steer` se o agente suportar (capability), senão `queue` (drain ao ficar idle); sem run: envio imediato. Composer nunca bloqueia.
- **Input → Output:** `{ text, delivery }` → `202 { admitted, position? }`.

### RF-004: Streaming de eventos tipados
- **Description:** Notifications `session/update` do agente viram eventos `AiChatEvent` persistidos (`Kind`+`PayloadJson`) e publicados no SSE existente: `ai_chat.event` (message/reasoning/tool_call/activity/error) e `ai_chat.session` (lifecycle). Historical replay preservado.
- **Rules:** deltas de uma mesma part agrupam no cliente; `tool_call` carrega `name`, `args`, `status`, `output`, `diff` quando disponível.

### RF-005: Permissões inline
- **Description:** `session/request_permission` → `PermissionGate` cria request persistida → SSE `ai_chat.permission` → UI approve/deny/always → `POST /api/local/ai/threads/{id}/permissions/{requestId}/reply` → resposta ao agente. Timeout 5min = deny. `always` vale só para a sessão corrente.
- **Input → Output:** `{ outcome: "allow"|"deny"|"always" }` → `204`; `404` se request expirada.

### RF-006: Cancel/Stop
- **Description:** `POST /api/local/ai/threads/{id}/cancel` envia `session/cancel` ao agente e, sem ack em 10s, mata a árvore de processos; sessão volta a `ready`. Deleção de thread encerra a sessão.

### RF-007: Sandbox → permission mode
- **Description:** `Sandbox` da thread mapeia para flags do adapter: `read-only` = sem escrita/execução sem permissão; `workspace-write` = escrita confinada ao workdir; `danger-full-access` = livre (prompts ainda emitidos para comandos classificados perigosos quando o agente os reporta).

### RF-008: Capability + fallback one-shot
- **Description:** `GET /api/agents` passa a expor `supportsInteractiveSession`. CLI sem suporte → thread `agent` usa o caminho one-shot existente por prompt (`IAgentAcpClient.ExecuteAsync` → resultado como eventos), sem steer/permissões.

### RF-009: Feature flag
- **Description:** `Taskboard:WebCliAgent:Enabled=false` (default) desliga endpoints `prompt/cancel/permissions` (404) e o modo `agent` na UI; modo `assistant` inalterado.

**Business rules / invariants:**
- 1 sessão viva por thread; concorrência de threads permitida (max 8 sessões por usuário, alinhado a `TerminalSessionManager`).
- `ai_chat_events` permanece imutável e append-only; todo output visível é persistido antes de publicar.
- Toda execução de processo reusa `WithoutTaskboardEnv.RemoveFrom` e roda com `UseShellExecute=false`, `CreateNoWindow=true`.
- Nenhum endpoint novo sem `RequireAuthorization`.

## 5. API Contract

**Auth:** existente (cookie login + `ApiKeyAuthenticationHandler`); todos os endpoints `RequireAuthorization`.

```http
POST /api/local/ai/threads
{ "title": "...", "mode": "agent", "agentType": "OpenCode",
  "repositoryFullName": "owner/repo" | "workspacePath": "/abs/path",
  "sandbox": "workspace-write", "model": "optional" }
→ 201 { thread } | 400 INVALID_AGENT | 422 AGENT_NOT_ELIGIBLE

POST /api/local/ai/threads/{id}/prompt
{ "text": "...", "delivery": "steer|queue" }      → 202 { admitted: true } | 404 | 409 THREAD_NOT_AGENT

POST /api/local/ai/threads/{id}/cancel            → 204 | 404 | 409 NO_ACTIVE_RUN

POST /api/local/ai/threads/{id}/permissions/{requestId}/reply
{ "outcome": "allow|deny|always" }                → 204 | 404 PERMISSION_EXPIRED

GET  /api/local/ai/threads/{id}/events            → SSE: backlog + live
     event: ai_chat.event     data: { id, kind: "message|reasoning|tool_call|activity|error", role, content, payload }
     event: ai_chat.session   data: { state: "starting|ready|running|idle|dead", agentType, workspacePath }
     event: ai_chat.permission data: { requestId, tool, detail, options }
     event: ai_chat.run       data: { run dto }   (mantido)

GET  /api/agents                                  → [..., supportsInteractiveSession]  (campo novo)
```

**Expected errors:** `400` validação · `404` thread/request inexistente ou feature off · `409` conflito de estado · `422` agente inelegível — formato `{ error: { code, message } }` existente, sem PII.

## 6. Acceptance Criteria

- [ ] **Given** flag on e agente elegível com sessão **when** crio thread `mode=agent` num repo e envio prompt **then** vejo `session(ready)` + resposta streaming com tool_calls inline.
- [ ] **Given** tool call que exige permissão **when** o agente emite `request_permission` **then** a UI mostra o prompt; `allow` executa, `deny` recusa, `always` não repete na sessão.
- [ ] **Given** run ativo **when** envio outra mensagem **then** ela é admitida como steer (suportado) ou queue e processada na sequência.
- [ ] **Given** run ativo **when** clico Stop **then** `session/cancel` é enviado e a sessão volta a `ready`; sem ack em 10s o processo morre.
- [ ] **Given** sessão idle >30min **when** envio prompt **then** nova sessão é criada e o contexto visível permanece (eventos persistidos).
- [ ] **Given** agente sem `supportsInteractiveSession` **when** thread `agent` envia prompt **then** execução one-shot posta resultado como eventos, sem crash.
- [ ] **Given** flag off **when** chamo `/prompt` **then** `404`.
- [ ] **Given** `workspacePath` fora do workspace root **when** crio thread **then** `400`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Processo morre mid-run | kill externo | `session(dead)` + evento `error`; próximo prompt respawna |
| Permission expira | sem reply em 5min | deny automático + evento `activity` |
| Servidor reinicia | sessões morrem | threads persistem; prompt recria sessão |
| JSON-RPC malformado | stdout inválido | log system na thread, sessão segue (padrão `JsonRpcAcpClient`) |
| Workdir inexistente | repo não clonado | `EnsureRoot` + `session/new` no root com aviso `activity` |

## 7. Task Plan

- [ ] **T1 — Domain/contratos:** colunas novas em `AiChatThread`/`AiChatEvent`, VO `AiChatEventKind`, DTOs, `IAgentSessionClient` + event records, `IAgentAdapter.SupportsInteractiveSession`/`BuildSessionCommand`, migration `AddWebCliAgent`. Testes de VO/mapping.
- [ ] **T2 — AcpSessionClient:** JSON-RPC stdio bidirecional (stdin+stdout), `session/new|prompt|cancel`, parse de `session/update` + `session/request_permission`, matriz de argv por agente (mínimo: OpenCode + Claude + Codex; demais declaram capability). Testes de framing/parse (`Dado_Quando_Entao`).
- [ ] **T3 — Server:** `AgentSessionManager` (registry+idle reaper+respawn), `PermissionGate`, endpoints `prompt/cancel/permissions`, extensão do SSE + flag. Integration tests dos endpoints e do stream.
- [ ] **T4 — UI:** `NewThreadDialog` (mode/agent/repo|dir/sandbox), renderers `ToolCallCard` + `PermissionPrompt` + `reasoning`, composer steer/queue + Stop, badge de estado da sessão; `taskboardSse` com os tipos novos.
- [ ] **T5 — Fallback + capability:** `supportsInteractiveSession` em `GET /api/agents`; caminho one-shot dentro de thread `agent`.
- [ ] **T6 — Verify:** `dotnet build` (warnings=errors), `dotnet test`, cobertura ≥ gate vigente (ratchet — não baixar `COVERAGE_THRESHOLD`), docs en/pt-br, SPEC → Done + PR.

## 8. Organization Guardrails

- **Branches:** nunca commit em `main`/`master`/`develop`; usar `feature/devin-20260919-web-cli-agent`.
- **Workflows:** não editar `.github/workflows/**`.
- **Specs:** contratos/rotas novas refletidos aqui; mudança de rota = documentar.
- **Segurança:** nunca logar API keys/env; prompts com segredos não são ecoados em logs de sistema; processos herdam env scrubbed (`WithoutTaskboardEnv`); workdir sempre clampado ao workspace root.
- **Arquitetura:** n-Layer ABP — contratos em `Application.Contracts`, spawn/JSON-RPC em `Integrations`, orquestração de sessão em `Server`, zero lógica de negócio em `.razor`/endpoints.
- **Escopo:** sem agent loop in-process, sem PTY na página, sem fs-browser dedicado.

## 9. Definition of Done

- [ ] RF-001…RF-009 implementados; ACs cobertos por testes.
- [ ] `dotnet build` limpo (`TreatWarningsAsErrors`); `dotnet test` verde; cobertura ≥ gate.
- [ ] Endpoints autenticados; erros no formato `{ error: { code, message } }`; logs sem PII/tokens.
- [ ] Flag off → comportamento 100% atual.
- [ ] Migration aplicada e reversível; docs atualizadas; `Status = Done` + PR aberto.

## Open Questions / Pending Ambiguity

- Nenhuma bloqueante. Nota de fase 2 (aprovada em Q1-c): agent loop in-process .NET (LLM provider + tools próprias estilo OpenCode) vira spec separada reutilizando os contratos de eventos desta spec.
