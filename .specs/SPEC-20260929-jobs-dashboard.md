# SPEC-20260929-jobs-dashboard

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `jobs-dashboard` |
| Type | `Feature / Platform` |
| Stack | `.NET 10 / EF Core SQLite / Blazor WASM` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-jobs-dashboard` |
| Ticket | [#383](https://github.com/afonsoft/agent-harness/issues/383) (Epic [#381](https://github.com/afonsoft/agent-harness/issues/381)) |
| Status | `Approved` |
| Related | SPEC-20260928-agent-cli-probe-background, SPEC-20260920-harness-recurring-jobs, SPEC-20260929-cli-probe-hardening |

## 1. User Story

**As a** operador do Harness
**I want** uma tela `/jobs` listando todos os jobs em background com seus logs recentes e configurações (enabled + intervalo), onde a atualização dos modelos dos CLIs/agents rode a cada 1h por padrão e seja reconfigurável sem restart
**So that** eu controle o agendamento dos jobs e inspecione suas execuções sem mexer em arquivos de configuração nem reiniciar o servidor.

**Problem context:**

- Os probes de versão/modelo dos CLIs rodam só no startup e sob demanda via TTL nas leituras — sem job periódico, um servidor long-running pode servir catálogo velho (RF-001 de `cli-probe-hardening` só reage a leituras).
- Intervals de jobs são fixos em código/config (`PeriodicTimer`) — mudar exige redeploy.
- Não há visibilidade operacional: qual job rodou quando, com que resultado.

## 2. Scope

- `src/Taskboard.Domain/Entities/JobSchedule.cs` (novo) + `TaskboardDbContext` + EF config + migration.
- `src/Taskboard.Application.Contracts/Jobs/` — `JobDefinition` (catálogo estático), `JobStatusDto`, `IJobRegistry`/`IJobScheduler` (registrar, ler overrides, reportar execução, trigger manual).
- `src/Taskboard.Server/Services/ManagedJobService.cs` (novo) — base `BackgroundService`: loop `Delay(GetInterval(key))` re-lendo enabled/interval a cada iteração + tracking + trigger manual via canal; converter os timers existentes.
- `src/Taskboard.Server/Program.cs` — endpoints `GET /api/jobs`, `PUT /api/jobs/{key}`, `POST /api/jobs/{key}/run`.
- `src/Taskboard.Blazor/Components/Pages/Jobs.razor` (nova tela) + `NavMenu.razor` + `TaskboardClient`.
- Tests unitários do registry/schedule/conversão + guards.

## 3. Technical Context

- Hosted services hoje: `CliMetricsSyncService` (15min config), `FinOpsAggregationService` (30s), `StaleAgentRunReaperService` (5min), `SpecDriftScanService` (1h), `AgentRunEventRetentionService` (6h), `SkillsSyncHostedService` (startup), `CliProbeRefreshHostedService` (startup one-shot), `PipelineEngineService` (event-driven — fora do registry de jobs agendados; pode aparecer como "service" read-only se trivial).
- Persistência: EF migrations reais (`dotnet ef migrations add`); `Database.MigrateAsync()` no boot.
- `CliMetricsOptions` já tem Enabled/SyncIntervalMinutes via config — o override de DB deve vencer o valor de config.
- Autenticação: endpoints de API protegidos pelo middleware existente (`HARNESS_API_KEY`).

## 4. Requirements

### RF-001: Job de refresh de CLI/agents+models

- **Description:** `CliProbeRefreshHostedService` vira `ManagedJobService` `cli-probe-refresh` (display "CLI & model probe refresh") com intervalo default **60min**: kick no startup + ticks periódicos chamando `CliProbeSnapshotService.RefreshAsync()`. Intervalo reconfigurável em runtime (lido a cada iteração) e persistido.

### RF-002: Registry + tracking

- **Description:** `IJobRegistry` (singleton Server) com catálogo `JobDefinition` (key, nome, descrição, intervalo default/min, `Configurable`), estado runtime (`IsRunning`, `LastStartedAt`, `LastCompletedAt`, `LastOutcome` ok/error, `LastMessage`, `RunCount`) e ring buffer de log por job (~50 entradas: ts + outcome + message). `ManagedJobService` reporta start/end automaticamente.

### RF-003: Schedule persistido + dinâmico

- **Description:** Entidade `JobSchedule { JobKey unique, Enabled, IntervalMinutes? }`; `PUT /api/jobs/{key}` persiste override (validação: interval ≥ min do job, ≥1min) e aplica sem restart — o loop do job lê o valor efetivo (override ?? config ?? default) a cada iteração. `Enabled=false` suspende o loop sem matar o hosted service.

### RF-004: API

- **Description:** `GET /api/jobs` → lista `JobStatusDto` (key, nome, descrição, enabled, interval efetivo, default, IsRunning, LastStartedAt/CompletedAt, outcome, mensagem, tail do log ~50 linhas). `PUT /api/jobs/{key}` → 400 invalid key/interval. `POST /api/jobs/{key}/run` → 202 + dispara execução imediata (single-flight com o tick — se rodando, resposta indica `already-running`).

### RF-005: Tela `/jobs`

- **Description:** Item "Jobs" no NavMenu (seção 2, após Skills, antes de Prompts — atualizar `NavMenuOrderTests`). Tabela: nome, estado (Running/Idle/Disabled badge), intervalo efetivo editável (minutos), toggle enabled, botão Run now, último resultado + timestamp, painel expansível com log recente. i18n dos labels em pt-br (consistente com Agents/Settings) — ou en se o padrão da página for en; verificar convenção da página Agents (pt-br misto — manter consistente com a página vizinha).

### RF-006: Conversão dos jobs existentes

- **Description:** Converter a base `ManagedJobService`: `cli-metrics-sync` (default 15min — override vence `CliMetricsOptions`), `finops-aggregation` (30s — permitir intervalo em segundos para este job; campo `IntervalMinutes` aceita fração? — decisão: `IntervalSeconds` no override para suportar 30s), `stale-run-reaper` (5min), `spec-drift-scan` (60min), `agent-run-event-retention` (6h), `skills-sync` (startup-only → registrar como `RunOnce`/intervalo grande, configurável off/on + run-now). `PipelineEngineService` fica fora (event-driven).

### RF-007: Testes

- **Description:** Unit: `JobSchedule` entity+config; registry (report run, ring buffer cap, override precedence); `ManagedJobService` (interval dinâmico, disabled suspende, trigger manual single-flight); API 400/404/202; guard da página + NavMenu order.

## 5. API Contract

```
GET  /api/jobs                      → JobStatusDto[]
PUT  /api/jobs/{key}                → { enabled?: bool, intervalSeconds?: int } → JobStatusDto | 400/404
POST /api/jobs/{key}/run            → 202 { started: bool }
```

## 6. Acceptance Criteria

- [ ] **Given** servidor no ar >1h sem leituras de catálogo **when** o job `cli-probe-refresh` roda **then** snapshot é atualizado (visível no log do job e em `LastCompletedAt`).
- [ ] **Given** `PUT /api/jobs/cli-probe-refresh` com intervalSeconds=300 **when** confirmado **then** próximo tick usa 5min sem restart.
- [ ] **Given** job disabled **when** page `/jobs` **then** badge Disabled e nenhuma execução nova.
- [ ] **Given** Run now **when** clicado **then** execução imediata aparece no log.
- [ ] **Given** build/test/migration **then** verde.

## 7. Task Plan

- [ ] **T1 — Domain/EF:** `JobSchedule` + config + migration.
- [ ] **T2 — Registry:** contracts + `JobRegistry` + `ManagedJobService` base.
- [ ] **T3 — Conversão:** 6 timers + cli-probe-refresh 1h.
- [ ] **T4 — API + client.**
- [ ] **T5 — Página `/jobs` + NavMenu + docs.**
- [ ] **T6 — Testes + Done + PR.**

## 8. Organization Guardrails

- Sem mudança de rotas existentes; novas rotas sob `/api/jobs` (documentar em `docs/api.md` se aplicável).
- `PipelineEngineService`/`AgentOrchestrationService` não entram no registry (event-driven).
- Não quebrar `CliMetricsSyncCoordinator` single-flight.
