# SPEC-20260921-acp-v1-conformance

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `acp-v1-conformance` |
| Type | `Architecture` (protocol conformance + reliability) |
| Stack | `.NET 10 / ABP N-Layer / JSON-RPC 2.0 over stdio (NDJSON) / SignalR` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-acp-v1-conformance` |
| Ticket | [#281](https://github.com/afonsoft/agent-harness/issues/281) |
| Status | `Approved` |
| Depends on | `SPEC-20260921-agent-execution-event-pipeline` (Done, #277) |
| Consumed by | `SPEC-20260921-acp-v2-readiness` |
| References | <https://agentclientprotocol.com/protocol/v1/overview> · <https://docs.github.com/pt/copilot/reference/copilot-cli-reference/acp-server> · <https://github.com/agentclientprotocol/claude-agent-acp> |

## 1. User Story

**As a** usuário do Harness
**I want** que a comunicação com agentes CLI siga o ACP v1 por completo — handshake com capabilities reais, lifecycle de sessão (new/load/resume/close), permissões, modos/modelos via config options, comandos `/`, fs/terminal do client e telemetria `usage_update`
**So that** qualquer agente ACP-conforme (Claude via `claude-agent-acp`, Codex via `codex-acp`, Copilot `--acp`, Gemini, OpenCode, Kiro…) funcione com visibilidade e controle completos no board, cockpit e AI Chat — sem depender de flags de bypass (`--dangerously-skip-permissions`, `--allow-all-tools`) que hoje escondem as ações do agente.

**Problem context — evidências do código atual:**

`AcpSessionClient` (sessão persistente) já faz `initialize` → `session/new` → `session/prompt` → responde `session/request_permission`. É o único caminho bidirecional. Os gaps medidos contra a spec v1:

| Área | Estado atual | Gap vs ACP v1 |
|---|---|---|
| `initialize` | envia `protocolVersion:1` + `clientCapabilities{fs:false,terminal:false}` | Não envia `clientInfo`; **descarta** `agentCapabilities`, `authMethods`, `agentInfo` — não sabe se o agente suporta `loadSession`/`resume`/`close`, nem se exige auth |
| `session/new` | `{cwd, mcpServers:[]}` correto | **Descarta `modes` e `configOptions` da resposta** — o catálogo real de modelos/modos do agente nunca chega à UI |
| `session/prompt` | content blocks `{type:text}` + resposta com `stopReason` via evento | Sem timeout no pending response (vaza TCS se o agente travar); fallback legado `{text,delivery}` quando `SessionId` nulo |
| `session/cancel` | enviado **com `id`** | A spec define notification (sem `id`, sem resposta); e ao cancelar, o client **MUST** responder `cancelled` a todos os `session/request_permission` pendentes — hoje ficam pendurados |
| `session/request_permission` | resposta JSON-RPC correta (`outcome.selected/cancelled`) | Options mapeadas por substring (`Contains("allow")`); `allow_always`/`reject_always` não persistem a escolha; sem timeout/expiry |
| `session/load`/`resume`/`close`/`list`/`delete` | ausentes | Processo morre → sessão perdida; sem graceful close; sem resume quando o agente anuncia a capability |
| `session/set_mode` / `session/set_config_option` | ausentes | Modelo/modo/thought_level só via flag de processo no spawn — **o usuário não pode trocar o modelo de uma thread viva**; `configOptions` é a forma preferida da spec (modes deprecated) |
| `fs/read_text_file`/`fs/write_text_file` | `-32601` | Capabilities declaradas `false` — OK; mas implementá-las daria ao agente acesso a arquivos **com auditoria e permissão nossas** |
| `terminal/*` (5 métodos) | `-32601` | Idem — execução server-side é sensível, fica opt-in |
| `authenticate`/`logout` | ausentes | Agente que retorna `authMethods` não-empty → `session/new` falha com `auth_required` e hoje vira erro genérico |
| `session/update` variants | parser cobre `agent_message_chunk`, `agent_thought_chunk`, `tool_call`, `tool_call_update`, `plan`, `usage_update` | `user_message_chunk`, `available_commands_update`, `current_mode_update`, `config_option_update`, `session_info_update` caem em `activity` genérico |
| Transporte | stdio apenas | Copilot também oferece `--acp --port` (TCP); útil para containers/agentes remotos |
| One-shot (`JsonRpcAcpClient`) | stdout oportunista, stdin fechado | Board/cockpit rodam **sem ACP real** — sem permissões, sem tool_call estruturado, sem steer mid-run |

**Achado estratégico:** o pain "qual modelo o AI Chat usa?" se resolve pelo protocolo — o agente anuncia `configOptions` com `category:"model"` no `session/new`. Hoje descartamos isso e mantemos uma tabela curada que pode divergir do CLI real.

## 2. Scope

**In scope:**
- Handshake completo com captura de capabilities/auth/info e validação de versão.
- Lifecycle de sessão: `session/new` (com `modes`/`configOptions`), `session/load`, `session/resume`, `session/close`, `session/list`, `session/delete` — todos capability-gated.
- `session/set_mode` + `session/set_config_option` com propagação ao estado unificado.
- Semântica correta de cancelamento (notification + `cancelled` nos pending permissions + `$/cancel_request` para requests nossos).
- `authenticate`/`logout` para `authMethods` tipo `agent`; tipo `terminal` vira instrução de login.
- `fs/*` e `terminal/*` client-side opt-in, sandboxed ao workspace + PermissionGate.
- Parser completo dos `sessionUpdate` variants v1 + tolerância a extensões (`_`-prefixed, `_meta`).
- Sessão-per-run ACP para board/cockpit (opt-in) substituindo stdout scraping.
- Timeouts, watchdog de processo, drain de pending requests, stderr→`output` (não `error`).
- `mcpServers` em `session/new`: injetar o MCP do Harness (RAG/Knowledge) quando configurado.

**Out of scope:**
- ACP v2 wire format — ver `SPEC-20260921-acp-v2-readiness` (esta SPEC já prepara os pontos de extensão).
- Streamable HTTP/WebSocket transport (RFD em draft no protocolo).
- `elicitation/create` — só declaramos a capability se/quando houver UI de input estruturado; fica como trabalho futuro documentado.
- UI nova de slash-command palette/model picker — a SPEC entrega os eventos/estado; a UI consome via timeline/estado existentes.

## 3. Functional Requirements

### RF-001 — Initialize completo e negociação

`initialize` envia `protocolVersion` (máximo suportado — default 1), `clientInfo{name:"taskboard",title:"Harness",version}` e `clientCapabilities` refletindo o que de fato implementamos:

```json
{
  "fs": { "readTextFile": true, "writeTextFile": true },
  "terminal": true,
  "auth": { "terminal": true },
  "session": { "configOptions": { "boolean": {} } }
}
```

(fs/terminal só `true` quando os flags RF-008/RF-009 estiverem ativos; caso contrário `false`/omitidos.)

Da resposta, capturar e persistir por sessão: `protocolVersion` (validar: se maior que o máximo suportado → falhar com evento `error` claro), `agentCapabilities` (`loadSession`, `promptCapabilities{image,audio,embeddedContext}`, `mcpCapabilities{http,sse}`, `sessionCapabilities{resume,close,delete,additionalDirectories}`, `auth.logout`), `authMethods[]`, `agentInfo`. Resultado vira `AcpPeerInfo` no session holder e um evento `session_info` normalizado.

### RF-002 — Session lifecycle capability-gated

- `session/new`: enviar `cwd` (absoluto), `mcpServers` (RF-013), `additionalDirectories` **somente** se o agente anunciou a capability. Capturar `sessionId`, `modes`, `configOptions` da resposta → eventos `session_info` + estado do holder.
- `session/close`: chamar antes de matar o processo quando `sessionCapabilities.close` anunciado; timeout curto (2s) → kill como fallback.
- `session/resume` (cap `sessionCapabilities.resume`) e `session/load` (cap `loadSession`): usados pelo reconnect do RF-011. `session/load` drena os `session/update` de replay até a resposta — os updates entram no pipeline normal com flag `_meta.replay=true`.
- `session/list`/`session/delete`: expostos via service interno para diagnóstico futuro; `delete` só com capability.

### RF-003 — Config options e modes (modelo/modo reais do agente)

- Novo contrato `IAgentSessionClient.SetConfigOptionAsync(threadId, configId, value, type?)` → `session/set_config_option` (`{sessionId, configId, value}`, `type:"boolean"` quando bool). Resposta = array completo de `configOptions` → atualizar estado + evento `config_option_update`.
- `SetModeAsync(threadId, modeId)` → `session/set_mode` quando o agente anunciou `modes` sem `configOptions` equivalente (back-compat; modes está deprecated na spec).
- `POST /api/agents/control` ganha action `set_config` `{scopeKind,scopeId,configId,value}` → roteado por `AgentControlService`.
- O estado unificado (`GET /api/agents/state`) passa a expor `configOptions`/`modes`/`currentModeId` por thread — **o AI Chat passa a mostrar o catálogo real do agente** (category `model`, `mode`, `thought_level`, `model_config`).

### RF-004 — Autenticação

- `authMethods` não-vazio → antes de `session/new`, tentar `authenticate {methodId}` para métodos `type:"agent"` (ou sem `type`). Sucesso → prosseguir.
- `type:"terminal"` (exige nosso cap `auth.terminal:true`) → emitir evento `auth_required` com o comando (`name`/`args`/`env` do descriptor) para a UI instruir "rode `<cli> <args>` no terminal"; não chamar `authenticate`.
- Erro JSON-RPC `auth_required` em qualquer request → evento `error` tipado `auth_required` + marcar sessão `waiting_auth`.
- `logout` disponível via control plane quando `agentCapabilities.auth.logout` anunciado.

### RF-005 — Cancelamento conforme

- `session/cancel` enviado **como notification** (sem `id`).
- Ao cancelar: responder `{outcome:{outcome:"cancelled"}}` a **todos** os `PendingPermissions` da sessão e marcar tool calls não-finalizados como `cancelled` (evento `tool_call_update` sintético local).
- `$/cancel_request` aceito (notification recebida): cancela o request com o `id` indicado respondendo `-32800`.
- Timeout de turno: `session/prompt` pendente além de `Taskboard:Acp:TurnTimeout` (default 30min, configurável) → emitir `session/cancel` + falhar o TCS com `AcpTurnTimeoutException`.

### RF-006 — Permissões completas

- `session/request_permission`: payload normalizado passa a carregar `toolCallId`, `title`, `kind`, `status`, `rawInput`, `locations`, `content` do `toolCall` embedded + `options[]` completos (`optionId`,`name`,`kind`).
- Resposta usa o `optionId` exato — `MapOutcomeToOption` passa a casar por `kind` (`allow_once`,`allow_always`) e `reject_*`, não substring do `optionId`.
- `allow_always`/`reject_always` → registrar em cache por `(threadId, toolName|kind)` da sessão; próximos pedidos iguais são auto-respondidos (e auditados com evento `permission` + `auto:true`). UI pode revogar (limpa o cache da thread).
- Expiração: `Taskboard:Acp:PermissionTimeout` (default 10min) → responde `cancelled` automaticamente.

### RF-007 — Parser v1 completo

Novos mapeamentos em `AcpProtocolParser.ParseSessionUpdate`:

| `sessionUpdate` | Kind normalizado | Notas |
|---|---|---|
| `user_message_chunk` | `message` (role=user) | replay/`session/load` |
| `available_commands_update` | `commands` (novo `AgentEventKinds.Commands`) | payload = `availableCommands[]` → futura palette `/` |
| `current_mode_update` | `session_info` | atualiza `currentModeId` do holder |
| `config_option_update` | `session_info` | atualiza `configOptions` do holder |
| `session_info_update` | `session_info` | title/metadata da sessão |
| `_`-prefixed / desconhecido | `activity` | preserva raw — forward-compat v2 |

Manter `_meta` intacto no `RawJson`/`PayloadJson` (propagação de `traceparent` para OTel).

### RF-008 — `fs/*` client-side (opt-in)

Flag `Taskboard:Acp:ClientFs` (default `true` — read/write são baratos e dão visibilidade):
- `fs/read_text_file{sessionId,path,line?,limit?}` → lê dentro do workspace da sessão; responde `{content}`.
- `fs/write_text_file{sessionId,path,content}` → **sempre** passa pelo `PermissionGate` (o agente declara intenção de escrita; UI mostra path + diff/tamanho); aprovado → escreve, cria arquivo se necessário.
- Path traversal fora do root da sessão → erro JSON-RPC `-32602` + evento `error`.
- Cada chamada gera evento `tool_call`/`tool_output` correlacionado (`toolCallId` se presente no request, senão id do request).

### RF-009 — `terminal/*` client-side (opt-in, default OFF)

Flag `Taskboard:Acp:ClientTerminal` (default `false` — execução arbitrária server-side):
- `terminal/create` → spawn bounded com `outputByteLimit` (truncation em char boundary), responde `terminalId` imediato.
- `terminal/output` → `{output,truncated,exitStatus?}`; `terminal/wait_for_exit` → bloqueia até exit; `terminal/kill` → SIGTERM sem invalidar; `terminal/release` → kill+free e invalida o id.
- Todo `terminal/create` passa pelo PermissionGate mostrando `command`, `args`, `cwd`. Terminais são filhos do processo-sessão e morrem com ele.

### RF-010 — Sessão-per-run para board/cockpit (opt-in)

Flag `Taskboard:Acp:SessionRuns` (default `false` nesta entrega — migração gradual):
- `JsonRpcAcpClient.ExecuteAsync` ganha modo `AcpRun`: spawn → `initialize` → `session/new(cwd,mcpServers)` → `session/prompt(instructions)` → consome updates até a resposta `stopReason` → `session/close` → exit.
- Permissões durante run: auto-policy do sandbox da issue (`allow_once` auto-aprovado se sandbox `Full`, senão escala para o card do run no board — já temos o transporte).
- Resultado: board/cockpit ganham tool calls, plans e permissões reais em vez de stdout achatado. CLIs sem ACP continuam no modo args atual (fallback automático quando `initialize` falha).

### RF-011 — Watchdog, timeouts e drain

- `Process.Exited` → emit `lifecycle` `process_exit` (exit code), falha todos os `PendingResponses` (`AcpProcessDiedException`) e `PendingPermissions` (responde nada — processo já morreu; marca cancelled localmente).
- Reconnect: se `loadSession`/`resume` anunciado e a thread estava ativa → `AgentSessionManager` tenta respawn + `initialize` + `session/resume|load` (uma tentativa, backoff 2s) antes de declarar morta.
- stderr → `output` events com `Stream=stderr` (hoje vira `error` — stderr é logging informativo por spec).
- Todos os outbound requests têm timeout (`Taskboard:Acp:RequestTimeout`, default 60s; handshake 15s já existe; prompt turn usa RF-005).

### RF-012 — MCP servers no session/new

`session/new.mcpServers` passa a incluir o(s) servidor(es) MCP do Harness configurados (RAG/Knowledge MCP do Settings) como `type:"stdio"`/`"http"` conforme `mcpCapabilities` do agente (`http`/`sse` em v1; sse deprecated — só http/stdio). O agente ganha acesso direto ao conhecimento da org sem round-trip pela nossa UI.

### RF-013 — Taxonomia de erro ACP

`AcpError` tipado: `AuthRequired`, `MethodNotFound`(-32601), `InvalidParams`(-32602), `Internal`(-32603), `Cancelled`(-32800), `Timeout`, `ProcessDied`, `VersionUnsupported`. Mapeados para `AgentEventKinds.Error` com `code` no payload e para HTTP (409/502) no control plane.

### RF-014 — Transporte TCP (opcional)

`AgentCliInvocation`/`IAgentAdapter` aceitam `AcpTransport` (`stdio`|`tcp`) + `Port` por agente (settings). TCP conecta em `127.0.0.1:port` (Copilot `--acp --port`), mesmo NDJSON. Habilita agente ACP rodando em container/Vizinho sem spawn local.

## 4. Non-Functional Requirements

- **NFR-1** NDJSON estrito: uma mensagem por linha, sem `\n` embutido; escritas sempre via `WriteLock` (já existe).
- **NFR-2** Forward-compat: enums/variants desconhecidos nunca quebram o parse; `_meta` e `_`-methods preservados (base da SPEC v2).
- **NFR-3** Todo evento novo passa pelo `ISecretRedactor` antes de persistir/broadcast (já existente).
- **NFR-4** Telemetria: `usage_update` alimenta `AgentExecutionResult.Usage`/FinOps; `traceparent` de `_meta` propaga para spans OTel.
- **NFR-5** Sem regressão: agentes legados (shape `params.kind`) e não-ACP continuam funcionando — fallback paths mantidos.
- **NFR-6** Backpressure: stdout reader nunca bloqueia em persistência; sink normalizado já é assíncrono.

## 5. Arquitetura

```
Agent CLI (stdio/TCP)
   │ NDJSON JSON-RPC 2.0
   ▼
AcpSessionClient ──► AcpProtocolParser (v1 dialect)
   │                     │ session/update variants
   │                     ▼
   │            AgentSessionEvent → AgentSessionManager → IAgentExecutionEventSink
   │                                                        (persist + SignalR)
   ├── session/request_permission ──► PermissionGate ──► timeline reply
   ├── fs/* ──► WorkspaceFsHandler (sandbox+gate)        [RF-008]
   ├── terminal/* ──► TerminalRegistry (bounded, gated)  [RF-009]
   └── capabilities/configOptions ──► AgentControlService ──► /api/agents/state
```

Novos componentes: `AcpPeerInfo` (capabilities+info+authMethods), `WorkspaceFsAcpHandler`, `AcpTerminalRegistry`, `SessionConfigState` (modes/configOptions), cache de `always`-permissions. Todos em `Taskboard.Integrations/Agents` (handlers) e `Taskboard.Server/Services` (orquestração).

## 6. Test Plan

- **Fixture agent**: `tests/fixtures/fake-acp-agent` — script Node/Python que responde `initialize` com capabilities configuráveis, emite `session/update` variants, pede permissão, chama `fs/*`. Roda como processo real nos testes de integração.
- **Unit**: parser — cada variant + batch + `_`-ext + malformed; `MapOutcomeToOption` por `kind`; config options merge; path-sandbox do fs handler; permission cache `always`.
- **Integração**: handshake com capabilities → `session/new` com mcpServers → prompt → cancel (verifica notification sem id + `cancelled` nos pending) → fs write gated → reconnect com `resume`.
- **Contrato**: tabela de fixtures JSON de cada mensagem v1 (copiadas da spec) — parser não pode falhar em nenhuma.
- **BDD** `Dado_Quando_Entao` em pt-br conforme convenção.

## 7. Acceptance Criteria

1. `initialize` valida versão e captura `agentCapabilities`/`authMethods`/`agentInfo` — visível em `/api/agents/state`.
2. `configOptions` do `session/new` (ex.: `category:"model"`) viram seletor funcional na thread — trocar modelo chama `session/set_config_option` e reflete `config_option_update`.
3. Cancelar um turno envia notification sem `id` e responde `cancelled` a permissões pendentes (teste de integração prova).
4. Com `ClientFs` on, `fs/write_text_file` exige aprovação no card de permissão e escreve só dentro do workspace.
5. `available_commands_update` gera evento `commands` persistido/replayável.
6. Agente fake com `authMethods` → fluxo `authenticate` completo antes do `session/new`.
7. Processo morto → `process_exit` + pending drained; com `resume` cap → reconecta.
8. Build 0 warnings, cobertura não regride, docs en/pt-br atualizadas.

## 8. Rollout / Flags

| Flag | Default | Efeito |
|---|---|---|
| `Taskboard:Acp:ClientFs` | `true` | anuncia+serve `fs/*` |
| `Taskboard:Acp:ClientTerminal` | `false` | anuncia+serve `terminal/*` |
| `Taskboard:Acp:SessionRuns` | `false` | board/cockpit via sessão ACP |
| `Taskboard:Acp:RequestTimeout` | `60s` | timeout por request |
| `Taskboard:Acp:TurnTimeout` | `30min` | timeout de `session/prompt` |
| `Taskboard:Acp:PermissionTimeout` | `10min` | auto-`cancelled` |

Rollback: desligar flags devolve o comportamento atual (capabilities false → `-32601`, args-mode nos runs).
