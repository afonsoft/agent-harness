# SPEC-20261001-chat-agent-delegation: delegação chat → agent e sub-agents

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat → Agent Delegation + Sub-agents |
| Product / System | agent-harness |
| Module / Bounded Context | Application + Integrations |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-chat-ux` |
| Technical owner | afonsoft |
| Status | Done — entregue via [PR #411](https://github.com/afonsoft/agent-harness/pull/411) (merged 2026-09-30) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20261001-chat-capability-registry, SPEC-20261001-chat-ux-compact, SPEC-20261001-chat-mcp-client |

---

## 1. Executive Summary

### Problem

Hoje os modos Chat e Agent são ilhas: Chat fala com o provider com tools
confinadas; Agent executa CLIs completos (`IAgentOrchestrationService`)
com terminal/PTY. O usuário quer que **o Chat delegue**: "rode isso com o
agent" ou "gere um sub-agente para analisar X" — sem trocar de modo nem
perder a conversa.

aaPanel implementa isso com `tools/task.py`: um sub-agente isolado
read-only que recebe prompt + toolset restrito e retorna resultado;
`skill_agents/*.md` são profiles especializados. OpenCode tem o conceito
`Task`/`subagent` equivalente.

### Objective

Duas tools novas no catálogo (kind `AgentDelegation`):

| Tool | Alvo | Toolset | Caso de uso |
|---|---|---|---|
| `run_agent` | `IAgentOrchestrationService` (CLI real) | full do CLI | "implemente X no repo" — trabalho pesado, async, com terminal |
| `task` | sub-agente provider in-process | read-only configurável | "analise estes logs", "explore o codebase e resuma" — resposta direta no chat |

### Expected outcome

- O modelo decide delegar: `run_agent` enfileira um run visível como
  card com link para o terminal/run; `task` retorna o resultado inline.
- Status chips "Running agent"/"Running sub-agent" durante a execução.
- Recursão bloqueada: sub-agente não pode criar sub-agente; `run_agent`
  é sempre leaf em relação ao chat.
- Cancelamento do turno cancela a delegação.

### Out of scope

- Profiles de sub-agents especializados editáveis na UI (aaPanel tem
  14 `.md` fixos — começamos com 3 builtin + custom via config; edição
  visual é followup).
- Execução paralela de múltiplos `task` num único turno (sequencial,
  governado pelo tool loop).
- Delegação chat→chat (provider→provider).

---

## 2. Agent Role

> .NET engineer — tools, orquestração, streaming de status, testes.

---

## 3. Agent Autonomy Level

3

### Restrictions

- `run_agent` respeita elegibilidade existente (CLI autenticado,
  habilitado) e o permission/security gateway — nada de bypass.
- Sub-agent `task` é **read-only por padrão**: `read_file`, `list_dir`,
  `web_search`, `use_skill`. `write_file`/`shell_exec`/`run_agent`
  ficam fora do toolset do sub-agent (aaPanel exclui write/persist
  exatamente assim).
- Sem recursão: `task` nunca aparece no toolset de um `task`.

---

## 4. Product Context

### Technical context

- `IAgentOrchestrationService.EnqueueAsync(AgentExecutionRequest)` —
  hoje orientado a `issueId`; delegação usa `issueId =
  "chat-{conversationId}"` (o formato string já comporta) — runs ficam
  correlacionados à conversa e visíveis no histórico do board.
- `ChatService` tool loop — `ChatToolContext` ganha `ConversationId`,
  `IChatActivityReporter`, `IAgentOrchestrationService` (via factory —
  context não carrega serviços; tools resolvem via DI própria).
- Sub-agent reutiliza `OpenAiCompatibleClient` + mesmo loop, com flag
  `isSubAgent` que troca o toolset e limita `MaxIterations` a 4.
- aaPanel `task.py` referência: `task_id` para retomar sessão — nosso
  equivalente é `parent_tool_call` persistido no resultado.

### Relevant files

- `src/Taskboard.Integrations/Chat/Tools/RunAgentTool.cs` (novo)
- `src/Taskboard.Integrations/Chat/Tools/SubAgentTool.cs` (novo)
- `src/Taskboard.Application/Chat/ChatService.cs` (context + sub-loop)
- `src/Taskboard.Application/Chat/SubAgentProfiles.cs` (novo)

---

## 5. Functional Requirements

### FR-001: `run_agent` tool

```json
{ "name": "run_agent",
  "parameters": {
    "prompt": "string (required)",
    "agent_cli": "string? — id do CLI; omitido = primeiro elegível",
    "wait": "boolean? — default false" } }
```

- `wait=false` (default): enfileira via `EnqueueAsync` com
  `issueId = "chat-{conversationId}"`, retorna imediatamente
  `{ "run_id", "status": "queued", "link": "/ai-chat?run=<id>" }` —
  acompanhamento pelo painel de runs.
- `wait=true`: bloqueia até conclusão (máx `Taskboard:Chat:Delegation:WaitTimeoutSeconds`, default 120s) e retorna saída truncada;
  timeout → retorna `run_id` + `status: still_running` sem falhar o
  turno.
- Sem CLI elegível → `Refused("no eligible agent CLI")`.
- Status event: `running_agent` enquanto `wait=true`; caso contrário um
  card "Agent run queued" com link.

### FR-002: `task` tool (sub-agent)

```json
{ "name": "task",
  "parameters": {
    "prompt": "string (required)",
    "profile": "string? — explore|review|summarize|custom",
    "system": "string? — system prompt custom (só com profile=custom)" } }
```

- Executa um loop provider interno (mesmo provider/modelo do chat pai)
  com toolset read-only e `MaxIterations=4`.
- Profiles builtin (constantes, inspiradas nos `skill_agents/` do
  aaPanel, mas generic dev-focused):
  - `explore` — "explore o workspace e responda" (read/list/search)
  - `review` — "revise o artefato/diff e aponte problemas"
  - `summarize` — "resuma os logs/documentos referenciados"
- Resultado `{ "answer": "…", "iterations": n, "tools_used": […] }`
  truncado em 32KB.
- Status event `running_subagent` (profile) durante execução; o
  ToolCallCard mostra o toolset interno usado (lista de nomes).

### FR-003: Recursion guard

`ChatToolContext` carrega `DelegationDepth`. `task` executado com
`depth+1`; o toolset interno exclui `task`/`run_agent` sempre
(hard rule — não depende de depth). `run_agent` de um turno já em
depth>0 → refused.

### FR-004: Toggles (capability registry)

- `Taskboard:Chat:AgentDelegation:Enabled` — master (default `true`).
- `agent:run` / `agent:task` individuais em `Capabilities:Disabled`.
- Quando desabilitados, as tools não entram no payload nem executam.

### FR-005: Cancelamento e falha

- Cancel do turno (stop button) propaga `ct`: `run_agent` aguardando
  chama `CancelAsync(issueId)`; `task` aborta o loop interno.
- Run falha → card renderiza status `failed` com último erro
  (amigável, segredos já redatados pelo pipeline do orchestrator).

---

## 6. Business Rules

- `run_agent` não executa ações destrutivas por si só — é o CLI real
  com suas policies; o prompt vai verbatim + prefixo de contexto
  `"(delegated from chat conversation {id})"`.
- `task` nunca persiste mensagens na conversa — só o resultado volta
  como tool message do turno pai.
- Sub-agent usa o provider da conversa — sem provider → refused
  (capability check na resolução do tool set).

---

## 7. Expected Architecture

```
modelo chama run_agent ──► RunAgentTool ──► IAgentOrchestrationService
   │                          │  EnqueueAsync("chat-{convId}", prompt)
   │                          ▼
   │                    AgentRun (terminal/thread real)
   │                          │  status events → chat.status SSE
   ▼                          ▼
ToolCallCard "Agent run" ◄── run_id + link + status

modelo chama task ──► SubAgentTool ──► loop provider interno
                          │  toolset read-only, MaxIter 4
                          ▼
                    resultado inline (tool message)
```

---

## 8. Edge Cases

- Conversa sem provider configurado + modo chat de CLI → `task` refused;
  `run_agent` funciona (delega ao CLI).
- `wait=true` + usuário fecha a página → run continua (é orquestrado,
  não atrelado ao circuito); o card mostra o estado na volta.
- Sub-agent estoura `MaxIterations` → retorna o que tem +
  `"truncated": true`.
- `agent_cli` inválido → refused com lista dos elegíveis.

---

## 9. Non-Functional Requirements

- `Enqueue` <300ms; `task` herda latência do provider (streaming interno
  não vaza para o usuário — só eventos de status).
- Runs delegados aparecem em `GetRunsAsync("chat-{convId}")` —
  reconciliáveis com o histórico.

---

## 10. Expected Tests

- `Dado_RunAgent_Quando_WaitFalse_Entao_EnfileiraERetornaRunId`
- `Dado_RunAgent_Quando_SemCliElegivel_Entao_Refused`
- `Dado_Task_Quando_Executa_Entao_ToolsetSemWriteNemRecursao`
- `Dado_SubAgent_Quando_ChamaTask_Entao_Refused_RecursaoBloqueada`
- `Dado_Cancel_Quando_WaitTrue_Entao_CancelPropaga`

---

## 11. Acceptance Criteria

1. `run_agent` enfileira run real rastreável pela conversa.
2. `task` retorna resposta inline com toolset read-only.
3. Recursão impossível; toggles do registry respeitados.
4. Status "Running agent/sub-agent" emitido e renderizado.

---

## 12. Implementation Plan

1. RED: testes das duas tools + recursion guard.
2. `ChatToolContext` + `DelegationDepth` + `ConversationId`.
3. `SubAgentTool` (loop interno) + profiles.
4. `RunAgentTool` (enqueue/wait) + status events.
5. Wiring no registry + GREEN + build.

---

## 13. Rollback Strategy

- `AgentDelegation:Enabled=false` remove as tools do payload; revert
  limpa o resto.

---

## Pending Questions

1. `wait=true` default timeout 120s ok?
2. Permitir `task` com profile `custom` + system arbitrário já, ou só
   os 3 builtin primeiro?
3. `run_agent` deve ganhar card próprio com mini-terminal embutido
   (SPEC-ux-compact) ou só link?

---

## Human Approval Checklist

- [ ] Semântica `run_agent` (async, `issueId=chat-*`) aprovada.
- [ ] Sub-agent read-only + sem recursão aprovado.
- [ ] Profiles builtin aceitos.
