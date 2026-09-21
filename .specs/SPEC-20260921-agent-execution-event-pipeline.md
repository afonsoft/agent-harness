# SPEC-20260921-agent-execution-event-pipeline

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-execution-event-pipeline` |
| Type | `Architecture` (refactor + feature) |
| Stack | `.NET 10 / ABP N-Layer / EF Core / SignalR + SSE / JSON-RPC stdio` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-agent-execution-event-pipeline` |
| Ticket | [#277](https://github.com/afonsoft/agent-harness/issues/277) |
| Status | `Done` |
| Depends on | — |
| Consumed by | `SPEC-20260921-board-cockpit-agent-observability` |

## 1. User Story

**As a** usuário do Harness
**I want** que toda execução de agente CLI (board, cockpit, AI Chat) produza um fluxo único de eventos estruturados, persistentes e reordenáveis — independente de o transporte ser ACP real, JSON-RPC improvisado ou stdout cru
**So that** o board e o cockpit possam mostrar ações, tool calls, decisões, permissões e logs de forma consistente, com replay confiável.

**Problem context — estado atual da orquestração (evidências):**

Existem hoje **três transportes diferentes** com o mesmo nome "ACP", nenhum deles conforme ao protocolo ACP real por completo:

1. **`JsonRpcAcpClient`** (`src/Taskboard.Integrations/Agents/JsonRpcAcpClient.cs`) — one-shot: spawn de processo com stdout/stderr redirecionados; faz parse *oportunista* de cada linha de stdout como JSON-RPC; notificações viram texto `method: params`; tudo que não é JSON vira log `System`. Usado por `PipelineEngine` (cockpit) e `AgentOrchestrationService` (board).
2. **`LocalCliAgentAcpClient`** (`src/Taskboard.Integrations/Agents/LocalCliAgentAcpClient.cs`) — one-shot: stdout/stderr cru, sem nenhum parse; resultado = exit code.
3. **`AcpSessionClient`** (`src/Taskboard.Integrations/Agents/AcpSessionClient.cs`) — sessão persistente stdio JSON-RPC: `session/new` → `session/prompt` → `session/cancel` → `session/reply_permission`. **Não é conforme ao ACP real**:
   - Não envia `initialize` (handshake obrigatório no ACP, com `protocolVersion` e capabilities).
   - `session/new` envia `params = { cwd, sandboxMode }`; o ACP real exige `{ cwd, mcpServers: [] }` e não define `sandboxMode`.
   - `AcpSessionMessageParser` lê `params.kind`/`params.content`; o ACP real envia `session/update` com `params.update.sessionUpdate` como discriminador (`agent_message_chunk`, `agent_thought_chunk`, `tool_call`, `tool_call_update`, `plan`, `available_commands_update`, `current_mode_update`) — ou seja, **notificações reais de tool calls/plans são hoje descartadas como `kind="message"` vazio**.
   - `session/request_permission` existe no ACP real com shape diferente (options tipadas `allow_once`/`allow_always`/`reject_once`/`reject_always`).

Superfícies de visibilidade atuais (fragmentadas):

| Caminho | Transporte | Eventos | Persistência | UI |
|---|---|---|---|---|
| Board run (`AgentOrchestrationService`) | one-shot raw/JSON-RPC | `AgentLogMessage` (IssueId, Timestamp, Stream, Content) | SQLite `AgentLog` (persist fire-and-forget — **sem ordem garantida**) + cache in-memory | `TaskLogTab` — linhas de texto + SignalR `/agent-log-hub` |
| Cockpit run (`PipelineEngine`) | one-shot | `CockpitEventDto` (RunId, Timestamp, Kind, Title, PayloadJson) | buffer bounded em memória | `RunTimeline` — renderiza `tool_call`, **mas nada produz esse kind** |
| AI Chat agent mode (`AgentSessionManager`) | sessão ACP-like | `AgentSessionEvent` → `AiChatEvent` (message/reasoning/tool_call/activity/error/permission) | SQLite (durável) | `AiChat.razor` + SSE |

**Gaps concretos:**
- `RunTimeline.razor` já renderiza `ToolCallCard` para `kind="tool_call"`, mas `PipelineEngine` só emite `agent_output` (linhas de texto) — tool calls de CLIs que emitem JSON estruturado são **achatados em texto**.
- Sem `id`, `sequence`, `parentEventId`, `sessionId`, `stageId`, `toolCallId` correlacionando eventos — impossível ordenar, deduplicar ou ligar request↔result. `AppendLog` persiste via `Task.Run` sem sincronização → **ordem de persistência não garantida**.
- Telemetria parcial: `AgentExecutionResult.Usage` (TokenUsage via `TokenUsageParser` no stdout) e FinOps existem — falta duração por etapa, modelo efetivo no envelope e custo por evento.
- "Decisões" não existem como conceito — devem vir de `plan`/`thought` chunks e metadados de tool calls, **nunca** chain-of-thought privado.

## 2. Scope

**In scope:**
- Envelope normalizado `AgentExecutionEvent` (Domain.Shared) com taxonomia de kinds e correlação completa.
- Camada de normalização: parser ACP conforme (handshake `initialize`, `session/update` com `sessionUpdate` discriminant, `session/request_permission` real) + adaptador raw-stdout que emite `output` events.
- Entidade durável `AgentRunEvent` (EF Core) com sequência por escopo (run/thread/issue), replay endpoint e política de retenção/redação.
- `AgentExecutionResult` estendido com telemetria (duration, exitCode, model, tokens quando disponíveis).
- Unificação dos produtores: `AgentOrchestrationService`, `PipelineEngine`, `AgentSessionManager` emitem para o mesmo sink normalizado.

**Out of scope (SPEC seguinte):**
- UI Board/Cockpit/Chat (timeline, cards, filtros) — `SPEC-20260921-board-cockpit-agent-observability`.
- Controle web (steer/cancel/approve unificado) — idem.
- Agentes MCP/HTTP remotos — só CLIs locais via stdio.

## 3. Requirements

### RF-001 — Envelope `AgentExecutionEvent`

Value object em `Domain.Shared/Agents` (ou `Application.Contracts` se só trafegar em DTOs — decisão: Domain.Shared pois alimenta entidade persistida):

```csharp
public sealed record AgentExecutionEvent(
    string EventId,          // ULID — ordenável
    string ScopeKind,        // "run" | "thread" | "issue"
    string ScopeId,          // RunId | ThreadId | IssueId
    long Sequence,           // monotônica por (ScopeKind, ScopeId)
    DateTimeOffset TimestampUtc,
    string Kind,             // taxonomia abaixo
    string? StageId = null,
    string? SessionId = null,
    string? ParentEventId = null,   // ex.: tool_call ↔ tool_result
    string? ToolCallId = null,
    string? Title = null,
    string? PayloadJson = null,     // payload tipado por kind
    string? RawJson = null,         // mensagem ACP original (truncada, redacted)
    string Stream = "system");      // stdout | stderr | system
```

**Taxonomia de `Kind`:**

| Kind | Payload | Origem |
|---|---|---|
| `lifecycle` | `{ phase: starting\|ready\|stopping\|stopped\|failed }` | todos os transports |
| `message` | `{ role, content }` | `agent_message_chunk` (agregado) / stdout |
| `thought` | `{ summary }` | `agent_thought_chunk` — **summary only**, nunca CoT cru |
| `plan` | `{ entries: [{id, title, status}] }` | `session/update: plan` |
| `tool_call` | `{ toolCallId, name, kind, status, arguments, locations[] }` | `tool_call` / `tool_call_update` |
| `tool_output` | `{ toolCallId, content, diff? }` | `tool_call_update` com output |
| `permission` | `{ requestId, tool, detail, options[], outcome?, respondedBy? }` | `session/request_permission` + reply |
| `output` | `{ stream, line }` | stdout/stderr não-estruturado |
| `diff` | `{ files[], patch? }` | pipeline worktree diff |
| `verification` | `{ check, status, detail }` | `PipelineEngine` |
| `metric` | `{ durationMs, exitCode?, tokensIn?, tokensOut?, costUsd?, model }` | fim de execução / `session/update` usage |
| `error` | `{ message, code? }` | qualquer camada |
| `approval` | `{ gate, status, actor }` | pipeline HITL |
| `steer` | `{ content }` | input humano durante run |

### RF-002 — Parser ACP conforme (`AcpProtocolParser`)

Reescrever `AcpSessionMessageParser` + handshake no `AcpSessionClient`:

1. `initialize` antes de `session/new`: `{ protocolVersion: 1, clientCapabilities: { fs: { readTextFile: true, writeTextFile: true }, terminal: false } }`. Falha → evento `error` + abortar sessão.
2. `session/new` com `{ cwd, mcpServers: [] }` (manter `sandboxMode` apenas para CLIs que documentam — via capability flag no `IAgentAdapter`).
3. `session/update`: ler `params.update.sessionUpdate` como discriminador:
   - `agent_message_chunk` → acumular em buffer por turno → `message`
   - `agent_thought_chunk` → `thought` (summary)
   - `tool_call` → `tool_call` com `toolCallId`/`title`/`kind`/`status`
   - `tool_call_update` → correlacionar por `toolCallId` (`ParentEventId`), atualizar `status`/`output`/`diff`
   - `plan` → `plan` com entries
   - `usage_update` (quando presente) → `metric`
   - desconhecido → `output` com `RawJson` preservado (forward-compat)
4. `session/request_permission`: params reais `{ sessionId, toolCall: {...}, options: [{optionId, kind}] }` → `permission` event; reply com `{ outcome: { outcome: "selected", optionId } }`.
5. Mensagens não-JSON em stdout → `output`; stderr → `output` com `Stream=stderr`.

CLIs sem modo ACP continuam no caminho raw — o adaptador emite apenas `lifecycle` + `output` + `metric`. Nada quebra.

### RF-003 — Entidade `AgentRunEvent` + replay

```csharp
public sealed class AgentRunEvent : Entity<Guid>  // Domain
{
    public string ScopeKind { get; }
    public string ScopeId { get; }
    public long Sequence { get; }      // atribuída por IEventSequencer (SQLite)
    public string Kind { get; }
    public string? StageId, SessionId, ParentEventId, ToolCallId, Title;
    public string? PayloadJson, RawJson;
    public string Stream;
    public DateTimeOffset TimestampUtc;
    // índice único (ScopeKind, ScopeId, Sequence); índice (ScopeId, Kind)
}
```

- `IAgentExecutionEventSink` (Application.Contracts): `EmitAsync(AgentExecutionEvent)` — assigna `Sequence`, persiste, publica no stream correspondente (SignalR grupo `run-{id}` / `agent-log-hub` / SSE chat).
- Endpoint de replay: `GET /api/agents/events?scopeKind=run&scopeId={id}&after={seq}` → página de eventos (limite 500, paginação por `Sequence`). Reusado pelo cockpit/board/chat para histórico + reconnect.
- Board: `AgentOrchestrationService` passa a persistir via sink (substitui log in-memory; manter `GetLogsAsync` como adapter de compat lendo `kind=output|message`).

### RF-004 — Telemetria em `AgentExecutionResult`

```csharp
public sealed record AgentExecutionResult(
    int ExitCode,
    bool Success,
    TimeSpan? Duration = null,
    string? ModelUsed = null,
    int? TokensIn = null,
    int? TokensOut = null,
    decimal? CostUsd = null);
```

Emissão automática de `metric` event ao fim de cada stage/run/sessão-prompt. Tokens/custo só quando a CLI reportar (`usage_update` ACP ou stderr estruturado conhecido) — nunca estimar.

### RF-005 — Redação e retenção

- `ISecretRedactor` aplicado em `PayloadJson`/`RawJson` antes de persistir: patterns de token (`ghp_`, `sk-`, `Bearer `, `api_key=`, env `*_TOKEN` valores conhecidos do processo) → `***`.
- `RawJson` truncado em 8 KB; `PayloadJson` em 32 KB.
- Retenção: `Taskboard:AgentEvents:RetentionDays` (default 30) — cleanup job remove `AgentRunEvent` antigos; cockpit buffer in-memory permanece para replay quente.

## 4. API Contracts

```
GET /api/agents/events?scopeKind={run|thread|issue}&scopeId={id}&after={seq}&take={n}
→ 200 { events: AgentExecutionEvent[], nextAfter: long, hasMore: bool }

POST /api/harness/runs/{id}/events/replay   (opcional: re-publica buffer no SignalR p/ reconexão)
```

Sem mudança breaking nos endpoints existentes — `agent-output`/`ReceiveLog` continuam emitindo (derivados do sink).

## 5. Acceptance Criteria (BDD)

- [ ] **Dado** CLI em modo ACP real emitindo `session/update` com `sessionUpdate=tool_call` **quando** a sessão processa **então** um `AgentRunEvent` kind=`tool_call` é persistido com `ToolCallId`, `Title` e `RawJson` redacted.
- [ ] **Dado** `tool_call_update` posterior com mesmo `toolCallId` **quando** processado **então** evento `tool_output` tem `ParentEventId`/`ToolCallId` correlacionando.
- [ ] **Dado** CLI sem ACP (raw stdout) **quando** executa via `LocalCliAgentAcpClient` **então** linhas viram `output` events com `Stream` correto e `lifecycle`/`metric` encerram o run.
- [ ] **Dado** 200 eventos emitidos para um run **quando** `GET /api/agents/events?after=0` **então** retorna em ordem de `Sequence` sem gaps nem duplicatas.
- [ ] **Dado** stdout contendo `ghp_xxx` **quando** normalizado **então** `PayloadJson`/`RawJson` persistidos contêm `***`.
- [ ] **Dado** `initialize` retornando erro JSON-RPC **quando** `StartSessionAsync` **então** sessão falha com `error` event e processo é encerrado (sem prompt enviado).
- [ ] **Dado** restart do servidor **quando** cockpit reabre um run antigo **então** replay vem do `AgentRunEvent` persistido (não do buffer).

## 6. Edge Cases

| Caso | Tratamento |
|---|---|
| `sessionUpdate` desconhecido (ACP novo) | `output` event + `RawJson` — forward-compat |
| Linha stdout > 8 KB não-JSON | trunca com marcador, `output` |
| Interleaving stdout/stderr | cada um sua `Stream`; `Sequence` global preserva ordem de chegada |
| Dois `tool_call` com mesmo id | segundo vira `tool_output` update; sem duplicar card |
| Evento emitido após run completado | aceito, `lifecycle` final continua sendo o último lógico para UI |
| CLI morre sem exit event | watchdog emite `lifecycle:failed` + `metric` com exitCode -1 |

## 7. Test Strategy

- **Unit:** `AcpProtocolParserTests` — fixture de JSON-RPC real (capturado de opencode/claude `--acp`), cada `sessionUpdate` → kind correto; `EventSequencerTests` (ordem/concorrência); `SecretRedactorTests`; `RawStdoutAdapterTests`.
- **Integration:** `AgentEventsEndpointsTests` — replay pagination, filtros por kind, autorização.
- **E2E (opcional CI):** spawn `opencode acp` real em thread → assert `tool_call` persistido no SQLite.
- Coverage: não reduzir ratchet (≥73%).

## 8. Rollout / Migration

- `AiChatEvent` existente **não migra** — novos eventos usam `AgentRunEvent`; o chat passa a ler dos dois (adapter) ou é migrado na SPEC de UI.
- `AgentLogHub`/`ReceiveLog` mantidos; `SignalRAgentLogBroadcaster` vira projeção do sink.
- Feature flag `Taskboard:AgentEvents:Enabled` (default true) para rollback rápido ao caminho in-memory.

## 9. Checklist

- [ ] `AgentExecutionEvent` + `AgentRunEvent` + migration EF
- [ ] `AcpProtocolParser` conforme (initialize/sessionUpdate/permission)
- [ ] `IAgentExecutionEventSink` + sequencer + SignalR/SSE fan-out
- [ ] `AgentOrchestrationService`, `PipelineEngine`, `AgentSessionManager` emitindo via sink
- [ ] `GET /api/agents/events` com paginação
- [ ] `ISecretRedactor` + truncamento + retention job
- [ ] `AgentExecutionResult` com telemetria
- [ ] Testes unit/integration; docs en/pt-br (`docs/api.md`, `docs/features.md`)
