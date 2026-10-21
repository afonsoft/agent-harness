# SPEC-20261021-agent-automations: automações agendadas mirando agentes/CLIs

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agent automations — prompts agendados que abrem sessões de CLI |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Server (dispatcher) + Domain + EF + Blazor (Automations page) + Integrations (tools) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/agent-automations` |
| Status | `Draft` |
| Depends on | `ChatSchedule`/`ChatScheduleService` (cron+tz+missed-fire), `delegate_task`/`CliWorktreeService`, `AgentCliKind` |
| Reference | Orca `docs/cli/automations` (`orca automations create/edit/list/run`, `--precheck`, `--reuse-session`, `--extra-agent-args`, `--missed-run-grace-minutes`); README "Compared to Orca" gap ② |

## 1. Executive Summary

### Problem

`ChatSchedule` agenda follow-ups dentro de uma conversa existente e
`JobSchedule` afina hosted services internos — nenhum dos dois agenda um
**prompt novo para um agente CLI** num repo/worktree. Rotinas recorrentes
("triage de issues toda manhã", "resumo do diff às 18h") exigem disparo
manual hoje.

### Solution

Nova entidade `AgentAutomation` + dispatcher hosted service: cron/one-shot
agenda → `precheck` shell opcional → cria (ou reusa) uma sessão PTY/delegation
task do CLI alvo no repo/worktree alvo → run history com outcome
(`succeeded|failed|skipped`) → feed `AutomationRun` + página `/automations`.

### Scope

In scope: CRUD + dispatcher + precheck + reuse-session + extra-agent-args +
run history + página Blazor + tool MCP. Out of scope: execução em host
remoto (depende de SPEC-remote-hosts — campo `Host` já fica no schema),
RRULE (só cron 5-campos + presets), retry com backoff (P2 considera).

## 2. Requisitos

### Funcionais

- RF-001 `AgentAutomation` entity: `Name` (≤120), `Prompt` (≤4000),
  `AgentCli` (claude/codex/… — mesmo enum `AgentCliKind`), `Target`
  (`repo:<name>` | `worktree:<path>` | `project:<id>`), `Schedule`
  (`cron` expr + `TimeZoneId` IANA | `at` utc | presets `hourly|daily|
  weekdays|weekly`), `Enabled`, `MissedRunGraceMinutes` (0–1440, default
  60), `ReuseSession` (bool), `ExtraAgentArgs` (≤2000, whitelist),
  `Precheck` (cmd ≤500), `CreatedBy`, timestamps.
- RF-002 `AgentAutomationRun` entity: `AutomationId`, `FiredAtUtc`,
  `Outcome(succeeded|failed|skipped|running)`, `ExitCode?`,
  `SessionId?`/`TaskId?`, `SkipReason?`, `DurationMs`.
- RF-003 `AutomationDispatcher` hosted service (tick 30s): para cada
  automation enabled com `NextFireAtUtc <= now` →
  1. `precheck` definido → executa via `IProcessRunner` (cwd=repo, 30s
     timeout); exit≠0 → run `skipped` + `SkipReason=precheck`.
  2. `ReuseSession` + run anterior `succeeded` + sessão viva → injeta o
     prompt na sessão existente (reuso do caminho `task-message`); senão
     cria worktree session/delegation task conforme `Target`.
  3. Marca run `running` → `succeeded` ao `worker_done`/exit 0, `failed`
     demais casos; recompute `NextFireAtUtc`.
- RF-004 Missed-run: servidor offline passa do fire → dispara na boot se
  `now - NextFireAtUtc <= MissedRunGraceMinutes`, senão marca `skipped`
  `missed_window` e recomputa.
- RF-005 `ExtraAgentArgs` whitelist por CLI (espelha Orca): só opções de
  model/effort + `--add-dir` (claude) + equivalentes; permission/config/
  prompt/session flags rejeitados com `invalid_extra_args`. Incompatível
  com `ReuseSession` (erro de validação no save).
- RF-006 Endpoints `/api/automations`: CRUD + `POST /{id}/run` (manual,
  ignora precheck? não — executa precheck, força fire) + `GET /{id}/runs`;
  auth mesmo esquema `agents`/`usage` (scoped user).
- RF-007 Tool MCP `automation_create`/`automation_list`/`automation_run`
  (mesmo padrão `schedule_*`/`job_*`); capability `agent:automation`.
- RF-008 UI `/automations`: tabela (name, agent, target, schedule, host,
  enabled, last-run outcome+tempo relativo), filtros por agent/outcome,
  create/edit drawer, botões Run now / Enable / Rerun (runs falhadas),
  busca por nome/prompt. Feed `AutomationRun` em fired/skipped (badge
  automation, sound category `job`).

### Não-funcionais

- Dispatcher single-writer via `IDistributedLock`-equivalente local
  (semaphore por AutomationId; multi-instância futura usa row version).
- Máximo 200 automations por usuário; runs retidos 90 dias
  (reuso do padrão `AgentRunEventRetentionService`).
- Precheck nunca recebe stdin do prompt; env sanitizado (`SecretScrubber`).
- Presets resolvidos para cron na gravação (`hourly`→`0 * * * *`,
  `weekdays`→`0 9 * * 1-5` com `--time` override) — armazena expr + tz.

## 3. Arquitetura

```mermaid
flowchart LR
  A[AgentAutomation] --> D[AutomationDispatcher 30s]
  D --> P{precheck ok?}
  P -->|no| S[run skipped]
  P -->|yes| T{reuse-session?}
  T -->|live session| Inject[task-message inject]
  T -->|no| New[worktree session / delegate_task]
  Inject --> R[run outcome]
  New --> R
  R --> Feed[AutomationRun feed] & UI[/automations]
```

- Dispatcher reusa cron-eval de `ChatScheduleService` (extrair
  `ICronEvaluator` compartilhado em Domain.Shared).
- Launch usa `IAgentLaunchResolver` + `ExtraAgentArgs` mergeado após
  defaults do host (extras vencem model/effort default).
- `Target` parseado por `AutomationTargetResolver` → `repo` cria worktree
  session nova, `worktree` reusa a path, `project` resolve repo default.

## 4. Fases

- **P1** — entities + EF + dispatcher (cron/at/presets) + create/run/list
  endpoints + precheck + missed-run grace + run history.
- **P2** — `ReuseSession` + `ExtraAgentArgs` whitelist + tool MCP + feed.
- **P3** — página `/automations` + filtros + Rerun + `--on host` stub
  (campo `Host` aceito, validado `local` até remote-hosts chegar).

## 5. Testes

- Unit: cron recompute (presets, tz, DST); missed-run grace boundary
  (≤grace dispara, >grace skipped); extra-args whitelist rejeita
  `--permission-mode`/`--config`.
- Unit: precheck exit≠0 → `skipped` sem criar sessão; `ReuseSession` +
  sessão morta → cria nova.
- Integration: POST create + POST run → sessão criada, run `succeeded`;
  disabled automation não dispara; rerun de run falhada fila novo run.
- Integration: 200-automation cap retorna 409 `automation_limit`.

## 6. Open questions

1. Reuse-session deve injetar via mailbox (`task-message`) ou keystroke no
   PTY? Proposta: mailbox quando a sessão é delegation task, keystroke só
   para PTY solta (menos confiável — P2 decide).
2. Runs falhadas devem auto-retry? Proposta: não — `Rerun` manual + outcome
   no feed; auto-retry vira `RetryPolicy` field no P4 se pedirem.
3. `Target=project` com multi-repo (SPEC-project-groups) — resolver para
   repo default ou exigir `repo:` explícito? Proposta: exigir explícito
   quando o projeto tiver >1 repo.
