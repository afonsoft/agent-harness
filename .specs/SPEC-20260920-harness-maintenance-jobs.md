# SPEC-20260920-harness-maintenance-jobs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-maintenance-jobs` |
| Type | `Feature` (Background Jobs — batch 2 do catálogo) |
| Stack | `.NET 10 / BackgroundService / EF Core 10 + SQLite / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/Devin-20260920-maintenance-jobs` |
| Ticket | — |
| Status | `Done` |

---

## 1. User Story

**As a** operador do Harness
**I want** jobs recorrentes que limpem runs de agente travados e monitorem drift de specs
**So that** runs `Queued`/`Running` órfãos de processo morto não fiquem eternamente "em execução" e drift de specs seja detectado sem depender do usuário abrir `/specs`.

### Problem Context

1. **Runs zumbis:** `AgentRun` fica `Running`/`Queued` para sempre quando o servidor reinicia ou o job morre sem finalizar — nada reconcilia a tabela com os jobs realmente vivos.
2. **Drift só sob demanda:** `/api/specs/drift-report` recalcula ~100 specs a cada request e só roda quando alguém abre a página — drift passa despercebido.
3. **Catálogo §9 pendente:** `SPEC-20260920-harness-recurring-jobs` §9 listou candidatos; este batch implementa os dois que têm fundação pronta no código.

---

## 2. Scope

### In scope

- **`StaleAgentRunReaperService` (BackgroundService, 5min):** finaliza como `Failed` runs `Queued`/`Running` com `StartedAt` mais velho que o threshold **e** sem job vivo no orchestrator.
- **`IAgentOrchestrationService.GetLiveRunIds()`:** snapshot dos runIds vivos (enfileirados + em execução) — o reaper nunca mata um run que o processo atual está executando.
- **`IAgentRunRepository.GetStaleActiveRunsAsync(cutoffUtc)`:** runs ativos mais velhos que o cutoff (filtro de data em memória — `DateTimeOffset` não traduz em WHERE no SQLite).
- **`SpecDriftScanService` (BackgroundService, 1h):** roda `ISpecDriftDetector.BuildReportAsync`, guarda o último relatório em `SpecDriftReportCache` (singleton) e loga `Warning` quando surgem novos itens de drift.
- **`/api/specs/drift-report`** passa a servir o cache (fallback ao scan ao vivo quando vazio).

### Out of scope

- Auto-aplicar transições de status sugeridas pelo drift (decisão do E13 — aplicação manual via endpoint/UI).
- Refresh externo de `ModelPriceRates` (sem fonte de preços configurada — requer design próprio).
- Memory compaction agendada (E7 não tem primitive de compactação — requer SPEC próprio).
- Retenção de `CliSessionMetrics` — **já existe** no `CliMetricsService` (`RetentionDays`, purge no tick de sync).

---

## 3. Technical Context

### Where the change happens

- `src/Taskboard.Application.Contracts/Agents/IAgentRunRepository.cs` — `GetStaleActiveRunsAsync`.
- `src/Taskboard.EntityFrameworkCore/Agents/EfCoreAgentRunRepository.cs` — implementação.
- `src/Taskboard.Application.Contracts/Agents/IAgentOrchestrationService.cs` — `GetLiveRunIds()`.
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` — `_liveRunIds` (add no enqueue, remove no `finally` de `RunAsync`).
- `src/Taskboard.Blazor/Services/HttpAgentOrchestrationService.cs` — stub `[]` (proxy WASM, nunca usado server-side).
- `src/Taskboard.Application/Agents/StaleRunReaper.cs` (novo — lógica testável, espelha `FinOpsAggregator`).
- `src/Taskboard.Server/Services/StaleAgentRunReaperService.cs`, `SpecDriftScanService.cs`, `SpecDriftReportCache.cs` (novos).
- `src/Taskboard.Server/Program.cs` — DI + endpoint drift-report via cache.

### Key conventions

- Hosted services em `Server/Services/`, escopo EF via `IServiceScopeFactory`, `PeriodicTimer`, exceção contida no tick (padrão `CliMetricsSyncService`/`FinOpsAggregationService`).
- `AgentRunState` persistido como valor do enum; filtro de `StartedAt` (`DateTimeOffset`) em memória — convenção do repo (SQLite não traduz `DateTimeOffset` em `WHERE`).
- Live set cobre runs **enfileirados** (RunId no canal) e **em execução** (`_running`) — um run `Queued` recém-criado nunca é stale.

---

## 4. Requirements

### RF-001: Reaper de runs stale

- **Description:** `StaleAgentRunReaperService` (`PeriodicTimer` 5min + passada inicial) → escopo → `IAgentRunRepository.GetStaleActiveRunsAsync(now - threshold)` → remove os presentes em `GetLiveRunIds()` → `FinishAsync(Failed)` nos restantes → log `Warning` por run.
- **Rules:** threshold default `15min`, configurável via `Taskboard:RecurringJobs:StaleRunThresholdMinutes`; lógica em `StaleRunReaper` (Application) — o hosted service só orquestra escopo/timer; run sem job vivo **e** mais novo que o threshold não é tocado (janela de pickup do canal).

### RF-002: `GetLiveRunIds` no orchestrator

- **Description:** `_liveRunIds` (`ConcurrentDictionary<Guid,byte>`) — `TryAdd` no `EnqueueAsync` quando `TryCreateRunAsync` retorna id; `TryRemove` no `finally` de `RunAsync`. `GetLiveRunIds()` retorna snapshot `Keys.ToArray()`.
- **Rules:** cobre queued + running; run sem persistência (`runId == null`) não entra.

### RF-003: Spec drift scan + cache

- **Description:** `SpecDriftScanService` (`PeriodicTimer` 1h + passada inicial) → `ISpecDriftDetector.BuildReportAsync` → `SpecDriftReportCache.Update(report)` → log `Warning` listando itens novos (ids não presentes no relatório anterior). Endpoint serve `cache.Last ?? detector.BuildReportAsync`.
- **Rules:** exceção contida no tick; detector é singleton — sem escopo EF.

---

## 5. Data Model / Contracts

- `IAgentRunRepository.GetStaleActiveRunsAsync(DateTimeOffset cutoffUtc, CancellationToken) → IReadOnlyList<AgentRunDto>` — aditivo.
- `IAgentOrchestrationService.GetLiveRunIds() → IReadOnlyCollection<Guid>` — aditivo.
- `SpecDriftReportCache` — singleton, `SpecDriftReportDto? Last`, `Update(report)`.
- Sem mudança de schema/migration.

---

## 6. Acceptance Criteria

- [x] **AC1:** Run `Running` há >15min sem job vivo → `Failed` no próximo tick (teste: repo com run velho + live set vazio → `FinishAsync(Failed)`).
- [x] **AC2:** Run `Running` há >15min **com** job vivo → não tocado (live set contém o id → skip).
- [x] **AC3:** `GET /api/specs/drift-report` responde do cache após o primeiro scan; novos drifts geram log `Warning` com os ids.
- [x] **AC4:** Falha numa iteração de qualquer job não mata o hosted service.

---

## 7. Implementation Tasks

- [x] **T1:** `GetLiveRunIds` (interface + orchestrator + stub WASM) + `_liveRunIds`.
- [x] **T2:** `GetStaleActiveRunsAsync` (contrato + EF) + `StaleRunReaper` + `StaleAgentRunReaperService` + DI.
- [x] **T3:** `SpecDriftReportCache` + `SpecDriftScanService` + endpoint via cache + DI.
- [x] **T4:** Testes unit (reaper, repo, cache/scan, live ids) + build + format.

## 8. Non-Goals / Guardrails

- Reaper nunca cancela jobs vivos — só reconcilia linhas órfãs do banco.
- Drift scan nunca altera arquivos de spec — só reporta e cacheia.
- `GetLiveRunIds` é best-effort dentro do processo; após restart todos os runs Running são legítimamente stale (o reaper resolve — feature, não bug).

---

## 9. Catálogo de jobs recorrentes (estado após este batch)

| Job | Intervalo | Status |
| --- | --- | --- |
| `CliMetricsSyncService` | 15 min (config) | existe |
| `PipelineEngineService` | 5 s | existe |
| `TerminalSessionManager` idle/orphan sweep | 1 min | existe |
| `SkillsSyncHostedService` | boot/periodic | existe |
| `FinOpsAggregationService` | 30 s | existe (#234) |
| **`StaleAgentRunReaperService`** | 5 min | **esta SPEC** |
| **`SpecDriftScanService`** | 1 h | **esta SPEC** |
| Retenção de `CliSessionMetrics` | no sync tick | existe (`RetentionDays`) |
| Refresh externo de `ModelPriceRates` | diário | futuro (requer fonte) |
| Memory compaction (E7) | sob demanda | futuro (requer design) |

---

## 10. Definition of Done

- [x] Jobs registrados e rodando; ACs cobertos por testes; build 0/0; format limpo; SPEC → `Done`.
