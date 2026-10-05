# SPEC-20261005-delegation-dag-mailbox: Task DAG + mailbox + dispatch paralelo + fan-out

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Delegation task DAG + agent mailbox + parallel dispatch + fan-out |
| Product / System | agent-harness |
| Module / Bounded Context | Domain + Application.Contracts + Integrations + EntityFrameworkCore + Server + Chat tools |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Status | `Done` — entregue via [PR #463](https://github.com/afonsoft/agent-harness/pull/463) (merged 2026-10-04) |
| Date | 2026-10-05 |
| Target agent | Devin |
| Related SPECs | SPEC-20261004-chat-workspace-cli-registry, SPEC-20261003-ai-code-agent-chat, SPEC-20261001-chat-agent-delegation, SPEC-20260919-worktree-sessions |
| Reference | análise comparativa stablyai/orca — coordinator/mailbox/dispatch (§6 do orca-analysis.md) |

---

## 1. Executive Summary

### Problem

1. **Delegação é one-shot**: `run_agent`/`task` disparam uma execução e acabam —
   não há como um agente declarar "rode A, depois B quando A terminar, depois C
   com os dois". O orca tem task DAG com `depends_on` → `ready` e dispatcher
   com `maxConcurrent`.
2. **Sem comunicação entre agentes**: sub-agents e delegações não podem trocar
   mensagens entre si nem sinalizar `worker_done`/`escalation`/`heartbeat` —
   o pai só vê o resultado final. Orca tem mailbox tipado com endereços
   `@all`/`@idle`/agent.
3. **Sem retry nem stale-base guard**: task falha → recomeço manual sem
   vínculo; base do repo moveu desde a criação → o dispatch roda em cima de
   base velha sem aviso.
4. **Sem fan-out**: mandar o mesmo prompt para N CLIs em worktrees isoladas e
   comparar os diffs (pick winner do orca) não existe — nosso worktree
   manager (SPEC-20260919) já dá a infra.

### Solution

Persistir `DelegationTask` (DAG com deps + retryOf + stale-base + worktree
opcional) e `AgentMailboxMessage` (mailbox tipado por escopo de conversa),
mais um `DelegationDispatcherService` (hosted service) que executa ready
tasks até `MaxConcurrent`, e as chat tools `delegate_task`, `delegate_fanout`,
`delegate_status`, `delegate_compare`, `agent_send`, `agent_inbox`.

### Scope

- IN: entities + repos + migration; dispatcher hosted service; stale-base
  guard + retry-of; fan-out com worktrees + diff compare; mailbox tools.
- OUT: decision gate bloqueante (P3), dashboard de agents (P3), sessões
  estruturadas por CLI, mobile/SSH.

---

## 2. Requisitos

**RF-001 — `DelegationTask` (Domain).** Entity: `Id (task-<ulid>)`,
`ConversationId?`, `Prompt`, `CliName` (AgentType name ou def id/name —
resolução no dispatch), `DependsOn` (lista de task ids), `RetryOf`
(task id), `FanoutGroupId?`, `UseWorktree`, `WorktreeRunId?`,
`WorkspacePath`, `RepositoryPath?`, `BaseCommitSha?`, `Status`
(`pending|ready|running|done|failed|stale|cancelled`), `ResultSummary`,
`Error`, `CreatedAt`, `StartedAt`, `FinishedAt`, `LastHeartbeatAt`.
Ciclo de vida: `pending` (deps abertas) → `ready` (todas deps `done`) →
`running` → `done`|`failed`; `stale` quando o guard dispara. Validações:
prompt obrigatório; `depends_on` só ids existentes (checado na criação,
sem ciclos); `retry_of` aponta para task `failed`/`stale`/`cancelled`.

**RF-002 — `AgentMailboxMessage` (Domain).** `Id`, `Scope` (conversationId
ou `"harness"`), `FromAgent` (nome do remetente), `ToAgent` (`@all`,
`@idle`, nome de cli/def, task id ou conversation id), `Kind`
(`text|worker_done|heartbeat|escalation|decision`), `Payload`, `CreatedAt`,
`ReadAt?`. Índice por (Scope, ToAgent, ReadAt).

**RF-003 — DAG ready.** `IDelegationTaskRepository` expõe
`ListReadyAsync(now)`: tasks `pending` cujas deps estão todas `done`
→ promovidas a `ready` pelo dispatcher. Dependência em task
`failed`/`cancelled`/`stale` **não** desbloqueia — a task fica `pending`
até a dep ser retomada (`retry_of` cria task nova que referencia a
original; quando a nova completa, a original vira `done`…). Decisão
simples: dep em estado terminal-falho marca a task `cancelled` com
`Error="dependency failed: <id>"`.

**RF-004 — Dispatcher.** `DelegationDispatcherService` (BackgroundService,
`Taskboard:Delegation:MaxConcurrent` default 4, poll 3s): a cada ciclo,
promove pending→ready, captura running `< MaxConcurrent`, e inicia cada
ready task como execução assíncrona (task-local, não queue global).
Execução: CLI builtin → `IAgentOrchestrationService.EnqueueAsync` com
`IssueId="task:<id>"` e polling de `GetRunsAsync` até terminal; def
custom → exec inline via `CustomCliRunner` (template/stdin, timeout
120s, redact). `LastHeartbeatAt` atualiza a cada poll (heartbeat). No
terminal: status final + `ResultSummary` (saída truncada 4k) + mailbox
`worker_done` com payload `{taskId, status, summary}` para `ToAgent=@all`
do scope da conversa; falha → `escalation`.

**RF-005 — Stale-base guard.** Na criação com `RepositoryPath` git:
`BaseCommitSha = git rev-parse HEAD`. Antes do dispatch, se o repo ainda
é git e `HEAD` atual difere do gravado → status `stale` (não despacha).
`retry_of` recria a task com base nova. Sem repo git → guard não aplica.

**RF-006 — Fan-out.** `delegate_fanout{prompt, clis[], use_worktree?}`:
cria uma task por CLI com `FanoutGroupId` comum; `use_worktree` (default
true) cria uma git worktree por task via `GitWorktreeManager`
(`repositoryPath` requerido e deve ser git; sem repo → erro claro no
result da tool). Cada task executa o mesmo prompt na sua worktree.

**RF-007 — Diff compare.** `delegate_compare{group_id|task_ids[]}`:
para cada task com worktree → `GitWorktreeManager.GetDiffAsync(runId)`
→ resumo `{taskId, cli, status, files, insertions, deletions}` + patch
truncado (8k/leg). Task sem worktree → `files: null, note: "no worktree"`.

**RF-008 — Mailbox tools.** `agent_send{to, text, kind?}` escreve mensagem
com `Scope=conversationId` e `FromAgent=DefaultAgentCli||"assistant"`.
`agent_inbox{unread_only?, limit?, to?}` lista mensagens do scope cujo
`ToAgent` ∈ `{@all, @idle, <meu cli>, <conversationId>, <task ids do
scope>}` e marca lidas (`ReadAt`). `kind` default `text`; `worker_done`/
`heartbeat`/`escalation` só o sistema emite (tool rejeita com refusal).

**RF-009 — Task tools.** `delegate_task{prompt, cli?, depends_on?, retry_of?,
use_worktree?}` → cria + retorna `{taskId, status}`. `cli` omitido usa
`DefaultAgentCli`. `delegate_status{task_id?|group_id?|all?}` → tasks do
scope (status, cli, heartbeat, resultado). Sem gestão de kill nesta SPEC.

**RF-010 — Endpoint de inspeção.** `GET /api/local/delegation/tasks?scope=`
→ tasks recentes (para futura UI/P3); `GET /api/local/delegation/mailbox?scope=`
→ mensagens. Ambos auth.

**Edge cases** — dep id inexistente → tool refusal; ciclo em depends_on →
recusado na criação; `retry_of` de task ainda running → refusal; dispatcher
crash deixa tasks `running` → sweep `LastHeartbeatAt` velho (>5min) marca
`failed`("dispatcher lost") na próxima passagem; worktree create falha →
task `failed` com erro; def desabilitada após criação → `failed` no
dispatch; conversa apagada → tasks seguem (scope preservado).

---

## 3. Arquitetura

```
AiChat tools
 ├─ delegate_task / delegate_fanout / delegate_status / delegate_compare
 ├─ agent_send / agent_inbox
 └─ ChatToolContext.ConversationId = Scope da mailbox/tasks

Domain
 ├─ DelegationTask (AggregateRoot<string>) + DelegationTaskStatus
 └─ AgentMailboxMessage (AggregateRoot<string>) + AgentMailboxKind

Application.Contracts
 ├─ IDelegationTaskRepository, IAgentMailboxRepository
 └─ DTOs (DelegationTaskDto, MailboxMessageDto, WorkspaceDiffSummaryDto)

Integrations
 └─ DelegationDispatcherService (hosted): ready-promote, dispatch,
    heartbeat, stale-guard, terminal→worker_done/escalation
    ExecutionStrategy: builtin → IAgentOrchestrationService;
                       def → CustomCliRunner inline

Server
 └─ POST já existente do orchestration continua; novos GETs de inspeção
```

## 4. Config

`Taskboard:Delegation:MaxConcurrent` (default 4), `Taskboard:Delegation:
HeartbeatTimeoutSeconds` (default 300), `Taskboard:Delegation:
PollIntervalSeconds` (default 3).

## 5. Segurança

- CLI alvo resolvido no dispatch contra AgentCliMap/defs habilitadas —
  nome livre nunca vira argv.
- Worktrees só dentro de `worktreeRoot` (guard já existente).
- Mailbox não carrega secrets — payload passa por `ISecretRedactor` no
  worker_done/escalation (o texto do `agent_send` é verbatim do agente).
- `depends_on`/`retry_of` só referenciam tasks do mesmo scope.

## 6. Testes

- `DelegationTaskTests`: create válido, deps inexistentes, ciclo,
  retry_of em task running → erro.
- `AgentMailboxTests`: create + marcação de lida.
- `DelegationDispatcherTests` (unit, dispatcher fake/poll manual):
  pending→ready só quando deps done; dep failed → cancelled;
  maxConcurrent respeitado; stale-base → status stale; retry-of cria
  com base nova; heartbeat velho → failed.
- `DelegationToolsTests` (additions): delegate_task cria e retorna id;
  delegate_fanout cria N tasks no mesmo grupo; agent_send/agent_inbox
  round-trip; delegate_compare sem worktree → note.
- Integration: GETs de inspeção 401/200; migration aplica limpa.
