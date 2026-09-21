# SPEC-20260921-board-cockpit-agent-observability

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `board-cockpit-agent-observability` |
| Type | `Feature` (UI + controle web) |
| Stack | `Blazor WASM / SignalR / SSE / .NET 10` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-board-cockpit-agent-observability` |
| Ticket | [#278](https://github.com/afonsoft/agent-harness/issues/278) |
| Status | `Approved` |
| Depends on | `SPEC-20260921-agent-execution-event-pipeline` |

## 1. User Story

**As a** usuário do Harness
**I want** ver e controlar execuções de agentes CLI na web — ações/tool calls, logs, decisões (plan/thought summaries), permissões, diffs, verificações e telemetria — tanto no Board (por issue) quanto no Cockpit (por run)
**So that** eu entenda o que o agente fez, aprove ações sensíveis, intervenha (steer/cancel) e faça replay de execuções antigas sem depender de terminal local.

**Problem context:**

- O cockpit renderiza `tool_call` (`RunTimeline.razor` + `ToolCallCard`) mas **nenhum produtor emite esse kind** — hoje tudo vira linhas `agent_output` de texto.
- Board (`TaskLogTab`) mostra só texto flat em memória — sem tool calls, sem persistência, sem replay após restart.
- Controles existem fragmentados: board tem `Cancel` (orphan, sem confirmação de estado), cockpit tem steer + approval gates, chat tem cancel/permissions — nenhuma superfície unificada mostra "o que posso fazer agora" num run.
- Sem visibilidade de: duração por stage, modelo usado, tokens/custo, diff por tool call, estado de sessão ACP.

## 2. Scope

**In scope:**
- Componente compartilhado `AgentRunTimeline` (Blazor) renderizando a taxonomia de `SPEC-20260921-agent-execution-event-pipeline`: cards de tool call (nome/args/status/output/diff), plan, thought summaries, permission com approve/deny inline, verification, metric, error.
- Board: `TaskLogTab` evolui para timeline estruturada + controles (cancel com estado real, approve/deny de permissões pendentes).
- Cockpit: `RunTimeline` consumindo eventos normalizados; `RunControlBar` ganha ações contextuais por estado.
- Controle web unificado: `POST /api/agents/runs/{id}/control` (actions: `cancel`, `steer`, `retry`) + reply de permissão por escopo.
- Painel de telemetria por run/stage (duração, modelo, tokens, exit code).
- Filtros na timeline (por kind, por stage, só erros, só tool calls), toggle raw/structured, auto-scroll com pausa.

**Out of scope:**
- Editor de pipeline / criação de stages na UI.
- Multi-agente paralelo com merge visual (cada stage é uma trilha).
- Replay visual tipo "vídeo" (scrub temporal) — replay é paginação.

## 3. Requirements

### RF-001 — `AgentRunTimeline` compartilhado

Componente em `Components/Agents/` consumido por Board, Cockpit e (fase 2) AI Chat:

- Props: `ScopeKind`, `ScopeId`, `HubUrl` (ou SSE endpoint), `ReadOnly`.
- Render por kind:

| Kind | Componente | Conteúdo |
|---|---|---|
| `lifecycle` | badge de fase | fase + timestamp |
| `message` | `ThoughtBlock` | conteúdo markdown |
| `thought` | `ThoughtBlock` (collapsed) | summary de decisão — label "Decisão do agente" |
| `plan` | `PlanCard` (novo) | checklist entries com status (pending/in_progress/done) |
| `tool_call` | `ToolCallCard` (existente) | nome, kind (read/edit/execute/fetch), args, status badge |
| `tool_output` | expandido dentro do `ToolCallCard` | output/diff correlacionado por `ToolCallId` |
| `permission` | `PermissionCard` (novo) | tool, detail, botões das options reais do agente, estado (pending/approved/denied/timeout) |
| `output` | console line | stream-colored (stdout/stderr/system) |
| `diff` | `GitDiffViewer` (existente) | patch |
| `verification` | `VerificationCard` (existente) | check/status |
| `metric` | `MetricChip` (novo) | `⏱ 2m14s · claude-sonnet · 12.4k tok` |
| `error`/`approval`/`steer` | alerts existentes | — |

- Correlação: `tool_call` + `tool_output` com mesmo `ToolCallId` renderizam como um card único (estado vivo atualizado por SignalR).
- Filtros toolbar: kinds multi-select, stage select, toggle "Raw output" (só `output` lines) vs "Structured".
- Auto-scroll sticky com botão "voltar ao fim" quando usuário sobe.

### RF-002 — Board integration

`TaskLogTab` → nova aba "Execução do Agente":

- Carrega histórico via `GET /api/agents/events?scopeKind=issue&scopeId={issueId}`.
- Live via SignalR (grupo por issue).
- Header de controle: estado real do run (`running`/`waiting_permission`/`idle`), botão Cancel (disabled quando não running), permissões pendentes com approve/deny inline.
- Compat: durante rollout, eventos `output` cobrem o que antes eram `AgentLogMessage` — nenhuma informação perdida.

### RF-003 — Cockpit integration

- `RunTimeline` troca parse ad-hoc por `AgentRunTimeline` com `ScopeKind=run`.
- `RunControlBar`: ações derivadas do estado — `Cancel` (running), `Steer` (running ou waiting), `Retry stage` (failed), badge `waiting_permission` pulsante quando há `permission` pending.
- Sidebar/painel de telemetria: por stage (duração, modelo, tokens) + total do run.

### RF-004 — Controle unificado

```
POST /api/agents/runs/{scopeId}/control
{ "action": "cancel" | "steer" | "retry", "content"?: string, "stageId"?: string }
→ 202 { accepted: true } | 409 (estado inválido) | 404

POST /api/agents/runs/{scopeId}/permissions/{requestId}/reply
{ "optionId": "allow_once" | ... }   // optionId real do agente
→ 200 { outcome } | 410 (request expirada)
```

- `scopeId` é RunId (cockpit), IssueId (board one-shot) ou ThreadId (chat) — o serviço roteia para `PipelineEngine.SteerAsync`/`CancelAsync`, `AgentOrchestrationService.CancelAsync` ou `AgentSessionManager` conforme `ScopeKind`.
- Toda ação de controle emite `AgentExecutionEvent` (`steer`, `permission` com `respondedBy`, `lifecycle:stopping`) — auditável na própria timeline.
- Autorização: endpoints exigem role existente de admin/operator (mesmo policy dos demais `/api/agents/*`).

### RF-005 — Estados visíveis de sessão/run

`lifecycle` events alimentam um `RunStateBadge`: `queued → starting → running → waiting_permission → stopping → stopped|failed|completed`. Board e cockpit mostram o badge; chat já tem equivalente — alinhar cores/labels.

## 4. API Contracts

| Endpoint | Descrição |
|---|---|
| `GET /api/agents/events?...` | replay paginado (SPEC anterior) |
| `POST /api/agents/runs/{scopeId}/control` | ações de controle |
| `POST /api/agents/runs/{scopeId}/permissions/{requestId}/reply` | reply de permissão |
| `GET /api/agents/runs/{scopeId}/state` | `{ state, pendingPermissions[], activeStage, metrics }` — snapshot p/ mount |
| SignalR `ReceiveAgentEvent` | evento normalizado por grupo `agent-{scopeKind}-{scopeId}` |

`AgentLogHub` permanece (compat), mas novo hub method/grupo carrega o envelope completo.

## 5. Acceptance Criteria (BDD)

- [ ] **Dado** run de pipeline em execução **quando** o agente emite tool call **então** a timeline do cockpit mostra `ToolCallCard` com nome/args/status em ≤2s, e o output chega no mesmo card.
- [ ] **Dado** permissão pendente (`waiting_permission`) **quando** clico Allow **então** reply é enviado ao agente, card atualiza para `approved` com meu identity, e run retoma.
- [ ] **Dado** board issue run ativo **quando** abro a aba do agente **então** vejo timeline estruturada (não texto flat) + estado real + cancel funcional.
- [ ] **Dado** run finalizado ontem **quando** abro o cockpit **então** replay paginado reconstrói a timeline completa do `AgentRunEvent` persistido.
- [ ] **Dado** 2 usuários na mesma run **quando** um aprova permissão **então** ambos veem `approved` e o segundo cliente não pode re-responder (410).
- [ ] **Dado** filtro "só erros" **quando** ativo **então** apenas `error`/`verification` failed aparecem, mantendo ordem.
- [ ] **Dado** evento `thought` **quando** renderizado **então** mostra label "Decisão" e summary — nunca campo raw de CoT.

## 6. Edge Cases

| Caso | Tratamento |
|---|---|
| Permissão respondida em outra janela | segundo reply → 410 + card atualiza para estado final |
| Reconnect SignalR | snapshot `GET state` + `after=lastSeq` no replay endpoint |
| Run com milhares de eventos | paginação reversa (últimos N + "carregar anteriores") |
| Tool call sem output (agente morreu) | card fica `status=failed` após `lifecycle:failed` |
| Steer em run que já completou | 409 com mensagem clara |
| Issue/run sem eventos | empty-state explicativo |

## 7. Test Strategy

- **Unit (server):** `AgentControlEndpointsTests` — roteamento por scope, 409/410, autorização; `PermissionReplyTests` — idempotência.
- **Unit (UI, se bUnit for adicionado) ou integration via Playwright/DevTools MCP:** renderização por kind, correlação tool_call↔output, approve inline.
- **Browser (DevTools MCP):** cenário real — run de cockpit com opencode/claude `--acp`, validar cards, permissão inline, cancel, reconnect.
- Cobertura não reduz ratchet.

## 8. Rollout

- Aba do board mantém fallback: se `AgentRunEvent` vazio para issue antiga, mostra mensagem "logs legados não migrados" (sem crash).
- `Taskboard:AgentEvents:UiEnabled` (default true).
- Docs en/pt-br: `docs/features.md` (seção Cockpit/Board) + `docs/api.md` (endpoints novos).

## 9. Checklist

- [ ] `AgentRunTimeline` + `PlanCard`/`PermissionCard`/`MetricChip`
- [ ] Board `TaskLogTab` → timeline + controles
- [ ] Cockpit `RunTimeline`/`RunControlBar` + telemetria
- [ ] `POST control` + `permissions/reply` + `GET state`
- [ ] SignalR grupo `agent-*` + snapshot/reconnect
- [ ] Filtros + raw/structured toggle + auto-scroll
- [ ] Testes + browser validation + docs bilíngue
