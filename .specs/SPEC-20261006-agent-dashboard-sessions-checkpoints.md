# SPEC-20261006 — Agent Dashboard, CLI Session Resume, Worktree Checkpoints e Decision Gate (P3)

## Status

In progress

## Contexto

Série orca→harness, parte 3 (P1 = workspace/registry, P2 = DAG/mailbox/dispatcher).
O orca tem um *Agent Dashboard* (Needs You / Working / Done / Idle), varredura do
histórico de sessões dos CLIs com resume nativo, e checkpoints de worktree. Este
SPEC porta essas capacidades ao Harness usando o que já existe: mailbox +
DelegationTasks (P2), `IWorkspaceIsolationService`, `TerminalSessionManager`/`TerminalHub`
e a página `/agents`.

## Requisitos Funcionais

### RF-001 — Agent Dashboard

- Endpoint `GET /api/local/delegation/dashboard?scope=` agregando quatro colunas:
  - `needsYou`: mensagens `escalation`/`decision` não lidas + tasks `Failed`/`Stale`/`Cancelled` recentes.
  - `working`: tasks `Running` + runs de agente `Queued`/`Running`.
  - `done`: tasks `Done` recentes (limite configurável, default 20 por coluna).
  - `idle`: CLIs instalados sem task/run ativa.
- Implementado por `IDelegationDashboardService` (Application) compondo
  `IDelegationTaskRepository`, `IAgentMailboxRepository`,
  `IAgentOrchestrationService` e `IAgentDiscoveryService` — todos contratos.
- Seção "Agent Dashboard" na página `/agents` renderizando as quatro colunas com
  contagens e itens (task id/cli/status ou mailbox kind/from/payload truncado).

### RF-002 — CLI session history

- `IAgentSessionScanner` (Integrations): para cada CLI suportado, varre o
  diretório de transcripts em `$HOME` e retorna entradas `{cli, sessionId, cwd?,
  modifiedAt, sourcePath, resumeCommand}`:
  - Claude: `~/.claude/projects/*/*.jsonl` — id = nome do arquivo; cwd decodificado
    do nome do diretório (`-` → `/`).
  - Codex: `~/.codex/sessions/**/*.jsonl` — id = nome do arquivo rollout.
  - OpenCode: `~/.local/share/opencode/**/storage/session/*/` (dirs por sessão) —
    best-effort; ausência de diretório retorna lista vazia, nunca erro.
- Endpoint `GET /api/local/agents/sessions?cli=` (opcional filtro) — lista ordenada
  por `modifiedAt` desc, limite 50/CLI.

### RF-003 — Resume

- `AgentCliSpec.ResumeArgs`: template argv com `{id}` para CLIs que suportam
  resume nativo: `claude` → `["--resume","{id}"]`, `codex` → `["resume","{id}"]`,
  `opencode` → `["--session","{id}"]`. Spec sem `ResumeArgs` → `resumeCommand` nulo.
- A página `/agents` lista as sessões com botão **Resume** que navega para
  `/terminal?cmd=<resumeCommand url-encoded>`; `Terminal.razor` passa a aceitar o
  parâmetro `cmd`: ao montar, abre tab e envia o comando via `Input`.

### RF-004 — Worktree checkpoints

- `IWorkspaceCheckpointService` (Integrations, git via `IGitCommandRunner`):
  - `CreateCheckpointAsync(worktreePath, label?) → CheckpointDto(sha, label, createdAt)`
    — `add -A` + `commit --no-verify -m "harness-checkpoint: <label> <utc>"`;
    worktree limpa → checkpoint do HEAD atual (não falha).
  - `ListCheckpointsAsync(worktreePath)` — `git log --format=%H%x09%cI%x09%s`,
    filtra prefixo `harness-checkpoint:`.
  - `RestoreCheckpointAsync(worktreePath, sha)` — `reset --hard <sha>`; sha fora do
    histórico → erro.
- Tools do chat: `worktree_checkpoint` (runId ou path; resolve path via
  `IWorkspaceIsolationService.GetAsync`) e `worktree_checkpoints` (lista + restore
  opcional `restore_sha`).

### RF-005 — Decision gate

- `IDelegationService.PostDecisionAsync(scope, fromAgent, question)` — posta
  mensagem kind `decision` para `@all` (sistema; continua fora de `AgentWritable`).
- Tool `agent_decide` (chat): `{question}` → cria decisão e devolve `decision_id`;
  respostas humanas chegam como `text` no inbox (fluxo mailbox existente).
- Dashboard `needsYou` inclui decisões não lidas.

## Não-Objetivos

- Replay/visualização de transcripts (só metadados + resume).
- Aprovação de decisão com um clique na UI (resposta vai pelo mailbox/chat).
- Checkpoints fora de worktrees gerenciados (repos soltos não são alvo).

## Testes

- Scanner: diretórios sintéticos em tmpfs para cada CLI; ausência → vazio.
- ResumeArgs: `resumeCommand` montado por CLI; spec sem resume → null.
- Checkpoints: repo git real em tmp — create/list/restore roundtrip; limpo; sha inválido.
- Dashboard: agregação das quatro colunas com repos substituídos.
- `agent_decide`/`worktree_*` tools: happy path + recusas.
- Integração: endpoints 401/200.
