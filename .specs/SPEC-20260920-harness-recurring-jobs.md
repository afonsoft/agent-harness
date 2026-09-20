# SPEC-20260920-harness-recurring-jobs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-recurring-jobs` |
| Type | `Feature` (Background Jobs / FinOps freshness) |
| Stack | `.NET 10 / BackgroundService / EF Core 10 + SQLite / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260920-recurring-jobs-terminal-resize` |
| Ticket | — |
| Status | `Done` |

---

## 1. User Story

**As a** gestor de engenharia que acompanha o dashboard `/finops`
**I want** um job recorrente que projete custo sobre o uso de tokens ingerido dos CLIs e um catálogo de outros jobs recorrentes
**So that** o FinOps reflita dados frescos e custos reais a cada 30 segundos, sem depender de runs Harness despachados.

### Problem Context

1. **FinOps cego ao uso real:** `CliSessionMetrics` contém ~599 sessões reais com `TokensInput/Output/Cached`, mas nenhum custo calculado. `RunCostMetrics` (E14) só registra runs Harness — hoje 0 linhas. O `/finops` mostra sempre zeros.
2. **Sem projeção de custo:** tokens sem custo USD impedem o objetivo do dashboard (burn por agente/modelo em dólar).
3. **Demandas recorrentes dispersas:** syncs, sweeps e recomputações vivem embutidos em managers ou triggered manualmente — não há catálogo de jobs.

---

## 2. Scope

### In scope

- **`FinOpsAggregationService` (BackgroundService, 30s):** projeta `CostUsd` por sessão CLI usando `TokenCostCalculator` + `ModelPriceRates`, e mantém agregado diário.
- **Normalização de modelo:** `CliSessionMetrics.ModelName` armazena JSON (`{"id":"Opus","providerID":"omniroute"}`) — extrair `id` antes do lookup de rate.
- **Novos seed rates:** modelos observados em produção (free tiers = $0; `Opus` → claude-opus-4; `DeepSeek` → deepseek-chat; `gemini-3.8-flash` → gemini-2.5-flash).
- **Coluna `CostUsd`** em `CliSessionMetrics` + `CliDailyUsageAggregates` + migration.
- **`GET /api/harness/finops/summary`** ganha seção `cliUsage` (sessões, tokens, custo por CLI) — aditivo, não-breaking.
- **Catálogo de jobs recorrentes** (seção 9) — demais itens como futuro.

### Out of scope

- Scheduler externo (Hangfire/Quartz) — `BackgroundService` + `PeriodicTimer` basta (padrão já usado por `CliMetricsSyncService`/`PipelineEngineService`).
- Cobrança real / exportação para billing.

---

## 3. Technical Context

### Where the change happens

- `src/Taskboard.Server/Services/FinOpsAggregationService.cs` (novo — padrão `CliMetricsSyncService`).
- `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs`, `CliDailyUsageAggregate.cs` (+ `CostUsd`).
- `src/Taskboard.EntityFrameworkCore/Configurations/` + nova migration.
- `src/Taskboard.EntityFrameworkCore/Configurations/ModelPriceRateConfiguration.cs` (seeds).
- `src/Taskboard.Application/Harness/FinOpsService.cs` (seção `cliUsage` no summary).
- `tests/Taskboard.Tests.Unit/` (job + normalização de modelo).

### Key conventions

- Hosted services em `Server/Services/`, singleton, escopo EF via `IServiceScopeFactory`, single-flight gate, `PeriodicTimer`.
- `decimal` para dinheiro; `DateTime` em colunas SQLite (não `DateTimeOffset`).
- Calculator faz exact → longest-prefix → `*`.

---

## 4. Requirements

### RF-001: Job `FinOpsAggregationService`

- **Description:** `BackgroundService` com `PeriodicTimer(30s)` + run imediato na subida. Single-flight. Por iteração: sessões `CliSessionMetrics` com `CostUsd IS NULL` e tokens > 0 → normaliza `ModelName` (JSON → `id`; string simples → direto) → `TokenCostCalculator.Calculate` (provider/model de `ModelPriceRates`) → persiste `CostUsd`. Depois re-agrega `CliDailyUsageAggregates.CostUsd` por (Day, Kind).
- **Rules:** batch máx 500 sessões/iteração; exceção logada e próximo tick continua; sem tokens → `CostUsd = 0`; re-ingest (`CliSessionMetric.Update`) reseta `CostUsd` → re-costa no próximo tick; agregado diário é **recomputado** por (Kind, Day) afetado — nunca soma delta (evita double-count em re-ingest).
- **CLI/UI Feedback:** log `FinOps aggregation: N sessions costed, $X total` em Debug.

### RF-002: Normalização de `ModelName`

- **Description:** helper `CliModelName.Normalize` — se `ModelName` parseia como JSON com `id`, retorna `id` + `providerID`; senão retorna string como está. Provider para lookup = `providerID` do JSON ou `Kind` da sessão.
- **Rules:** JSON malformado → fallback string; `null` → rate `*`.

### RF-003: Seeds de preço observados

- **Description:** `HasData` adiciona: `omniroute|free-stack|0`, `omniroute|auto/best-free|0`, `opencode|mimo-v2.5-free|0`, `opencode|hy3-free|0`, `opencode|muse-spark-1.2-contributor-free|0`, `omniroute|Opus|15/75` (claude-opus-4), `omniroute|DeepSeek|0.27/1.1`, `google|gemini-3.8-flash|0.3/2.5`, `omniroute|auto/best-coding|3/15` (conservador — auto-route pode cair em modelo pago).

### RF-004: Summary com `cliUsage`

- **Description:** `FinOpsSummaryDto` ganha `CliUsageSummaryDto? CliUsage` — `{ sessions, tokensInput, tokensOutput, tokensCached, costUsd, costByCli }` agregado de `CliDailyUsageAggregates`/`CliSessionMetrics` no período.
- **Rules:** resposta permanece compatível (campo opcional); `period` filtra por `Day`.

---

## 5. Data Model / Contracts

- `CliSessionMetric.CostUsd` (`decimal?`, TEXT SQLite).
- `CliDailyUsageAggregate.CostUsd` (`decimal`, default 0).
- `ModelPriceRateConfiguration` seeds (RF-003).

```json
// summary (trecho novo)
"cliUsage": {
  "sessions": 599,
  "tokensInput": 339272132,
  "tokensOutput": 1903428,
  "tokensCached": 375283936,
  "costUsd": 196.73,
  "costByCli": { "OpenCode": 196.73 }
}
```

---

## 6. Acceptance Criteria

- [x] **AC1:** Após o primeiro tick, sessões com tokens têm `CostUsd` calculado e o agregado diário reflete a soma (sessão OpenCode `Opus` 12,7M in + 31k out → ~$193,24). *(unit: sessão `Opus` 1M in → $15,00; agregado recomputado por dia)*
- [x] **AC2:** `GET /api/harness/finops/summary` retorna `cliUsage` com custo agregado por CLI; campo ausente/zero quando não há dados. *(`CliUsage` null sem agregados; populado com CostByCli)*
- [x] **AC3:** Tick a cada ~30s; falha numa iteração não mata o service (log + próximo tick). *(`PeriodicTimer(30s)` + `SafeRunAsync` com catch por tick; escopo EF por iteração)*

---

## 7. Implementation Tasks

- [x] **T1:** `CliModelName.Normalize` + colunas `CostUsd` + seeds + migration.
- [x] **T2:** `FinOpsAggregationService` + DI + single-flight.
- [x] **T3:** `cliUsage` no `FinOpsService`/DTO + testes.

## 8. Non-Goals / Guardrails

- Não alterar schema de `RunCostMetrics` — CLI usage e run costs são fontes distintas no summary.
- Job nunca bloqueia requests; toda exceção fica contida no tick.

---

## 9. Catálogo de jobs recorrentes (survey — futuro)

| Job | Intervalo sugerido | Status |
| --- | --- | --- |
| `CliMetricsSyncService` | 15 min (config) | **existe** |
| `PipelineEngineService` | 5 s | **existe** |
| `TerminalSessionManager` idle sweep | interno (timer) | **existe** |
| `FinOpsAggregationService` | 30 s | **esta SPEC** |
| Spec drift scan (`SpecDriftDetector`) | 1 h | futuro |
| Reaper de `AgentRuns` stale (`Running` > N h sem heartbeat) | 5 min | futuro |
| Retenção/rollup de `CliSessionMetrics` antigas | diário | futuro |
| Refresh externo de `ModelPriceRates` | diário | futuro |
| Memory compaction (E7) agendada | sob demanda | futuro |

---

## 10. Definition of Done

- [x] Job roda a cada 30s em produção; `cliUsage` populado no summary.
- [x] Migration aplicada; build 0/0; testes novos verdes; docs en/pt-br.
