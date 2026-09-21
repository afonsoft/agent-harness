# SPEC-20260919-ade-observability-finops

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ade-observability-finops` |
| Type | `Feature` (ADE Control Plane & FinOps Analytics) |
| Stack | `.NET 10 / OpenTelemetry / System.Diagnostics.Activity / Blazor WASM / SQLite / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260919-ade-observability-finops` |
| Ticket | [#169 — E14](https://github.com/afonsoft/agent-harness/issues/169) |
| Status | `Done` |

---

## 1. User Story

**As a** líder técnico ou gestor de engenharia
**I want** rastreabilidade distribuída via OpenTelemetry e controle financeiro (FinOps) de consumo de tokens e custos por modelo/agente
**So that** eu tenha visibilidade completa da eficiência dos agentes, identifique gargalos de latência, controle orçamentos máximos de gastos por tarefa e evite loops infinitos que gerem custos inesperados.

### Problem Context

No estado atual do `agent-harness`:
1. **Opacidade de Custos:** Não há nenhum registro de quantos tokens de input, output ou cache foram consumidos em um run ou issue. O usuário não sabe se uma execução custou $0.05 ou $5.00.
2. **Ausência de Travas de Orçamento (Budget Caps):** Um agente preso em um loop de retry ou gerando saídas repetidas pode esgotar a cota da API sem nenhum limite de segurança.
3. **Falta de Rastreamento Distribuído:** A depuração de pipelines multi-agente é difícil sem spans de OpenTelemetry correlacionando etapas, chamadas de ferramentas e tempos de resposta do modelo.

---

## 2. Scope

### In scope

- **Instrumentação OpenTelemetry Nativa (.NET 10):**
  - Criação da fonte de atividade `Taskboard.Harness`.
  - Spans estruturados para cada fase: `harness.run`, `harness.stage`, `harness.tool_call`, `harness.verification`.
- **Catálogo de Preços de Modelos e Cálculo de Custo:**
  - Tabela configurável de preços por 1M de tokens (Input, Output, Cache Write/Read) para provedores suportados (Anthropic, OpenAI, DeepSeek, Google).
- **Mecanismo de Budget Cap (Teto de Gastos):**
  - Limite configurável por `AgentRun` (ex: máx. $2.00 USD ou 100.000 tokens).
  - Interrupção segura do run caso o teto seja atingido, com status `BudgetExceeded`.
- **Dashboard FinOps na UI do ADE (`/finops`):**
  - Gráficos de queima de tokens (burn-down/burn-up), distribuição de custos por agente/modelo e histórico de despesas por issue/projeto.

### Out of scope

- Faturamento real com cartão de crédito ou gateways de pagamento externos (foco no controle contábil e de limites locais).

---

## 3. Technical Context

### Where the change happens

- **Contracts:** `Taskboard.Application.Contracts/Harness/FinOps/`, DTOs de métricas e custos.
- **Domain:** `Taskboard.Domain/Entities/Harness/RunCostMetric.cs`, `ModelPriceRate.cs`.
- **Integrations:** `Taskboard.Integrations/Harness/FinOps/TokenCostCalculator.cs`, `HarnessTelemetrySource.cs`.
- **Blazor WASM:** Página `FinOpsDashboardPage.razor`, componentes de métricas.

### Files to read before implementing

- `src/Taskboard.Domain.Shared/ValueObjects/ModelRef.cs`
- `src/Taskboard.Server/Program.cs` (configurações de telemetria)
- `.specs/SPEC-20260918-agent-model-tiers.md`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Harness/TokenUsageType.cs                  [new]
src/Taskboard.Domain/Entities/Harness/RunCostMetric.cs               [new]
src/Taskboard.Domain/Entities/Harness/ModelPriceRate.cs              [new]
src/Taskboard.Application.Contracts/Harness/FinOps/IFinOpsService.cs [new]
src/Taskboard.Application.Contracts/Harness/Dtos/FinOpsDtos.cs       [new]
src/Taskboard.Integrations/Harness/FinOps/TokenCostCalculator.cs     [new]
src/Taskboard.Integrations/Harness/FinOps/HarnessTelemetrySource.cs  [new]
src/Taskboard.Blazor/Pages/FinOps/FinOpsDashboardPage.razor          [new]
tests/Taskboard.Tests.Unit/Harness/TokenCostCalculatorTests.cs       [new]
```

---

## 4. Requirements

### RF-001: Rastreamento Granular de Tokens
- **Description:** A cada turno do agente ou finalização de etapa, o harness deve extrair tokens de entrada, tokens de saída e tokens de cache (se reportados pelo CLI/API).
- **Input → Output:** Log de uso → Registro no banco associado ao `runId` e ao `stageId`.

### RF-002: Cálculo Automático de Custo em USD
- **Description:** Com base no modelo utilizado e na tabela `ModelPriceRate`, calcular o custo monetário exato da execução.
- **Rules:** `Custo = (TokensInput * PrecoInput) + (TokensOutput * PrecoOutput) + (TokensCache * PrecoCache)`.

### RF-003: Interrupção por Teto de Orçamento (Budget Enforcement)
- **Description:** O usuário pode definir `MaxBudgetUsd` ao iniciar um run.
- **Rules:** Se o custo acumulado ultrapassar o teto, o orquestrador cancela o processo imediatamente e marca o run como `Status.BudgetExceeded`.

### RF-004: Métricas OpenTelemetry
- **Description:** Exportar métricas e traces padrão (compatíveis com Prometheus, Jaeger, OTLP).
- **Rules:** Atributos de span incluem `harness.run_id`, `agent.type`, `model.name`, `tokens.total`, `cost.usd`.

---

## 5. API Contract

```http
GET /api/harness/finops/summary?period=last-30-days
→ 200 OK
{
  "totalCostUsd": 14.85,
  "totalTokens": 2450000,
  "runsCount": 42,
  "costByAgent": {
    "Claude": 9.20,
    "Codex": 4.10,
    "Devin": 1.55
  },
  "costByModel": {
    "claude-3-7-sonnet": 9.20,
    "gpt-5.6-luna": 4.10
  }
}

GET /api/harness/runs/{id}/telemetry
→ 200 OK
{
  "runId": "run_01j7abcde",
  "totalTokens": 38400,
  "inputTokens": 31200,
  "outputTokens": 7200,
  "costUsd": 0.198,
  "durationSeconds": 142.5,
  "budgetCapUsd": 1.50
}
```

---

## 6. Acceptance Criteria

- [x] **Given** um run que consome 10k tokens de input e 2k tokens de output em Claude 3.7 Sonnet, **when** consultado o custo, **then** o valor calculado corresponde exatamente à taxa configurada. (`TokenCostCalculatorTests` + `FinOpsServiceTests` — $0.06 exato via taxa seedada)
- [x] **Given** um run com teto de $0.50, **when** a execução atinge $0.51, **then** o harness interrompe o agente e registra motivo `BudgetExceeded`. (`AgentOrchestrationServiceTests` — mid-flight via stream e pós-run)
- [x] **Given** spans gerados pelo harness, **when** inspecionados via OpenTelemetry listener, **then** os metadados de `runId` e `agent` estão presentes em todos os spans filhos. (`HarnessTelemetrySourceTests` — `harness.run`/`harness.stage`/`harness.verification`)

---

## 7. Task Plan

- [x] **T1 — Telemetry Source & Activity Tags:** Configurar `HarnessTelemetrySource` com OpenTelemetry .NET 10.
- [x] **T2 — Domain & Pricing Tables:** Implementar `ModelPriceRate`, seed com preços padrão de mercado e entidade de métricas.
- [x] **T3 — Cost Calculator & Budget Guard:** Implementar `TokenCostCalculator` com interceptor de teto de gastos.
- [x] **T4 — FinOps Blazor Dashboard:** Desenvolver página `/finops` com cards de totais e gráficos de distribuição.
- [x] **T5 — Unit Tests:** Validar cálculos de custo e enforcement de budget cap com xUnit e Shouldly.

### Deviation notes (implemented)

- `HarnessTelemetrySource`, `TokenCostCalculator`, `TokenUsageParser`, `TokenUsage`, `TokenUsageType`, `ModelPriceRateInfo` live in `Taskboard.Domain.Shared` (not `Taskboard.Integrations`) — `Application` (`PipelineEngine`, `FinOpsService`) cannot reference `Integrations`; these are pure primitives shared across layers (same precedent as E13's `LivingSpecification`).
- Blazor page at `Components/Pages/FinOps.razor` (all pages live under `Components/Pages/`).
- `harness.tool_call` span factory exists for future tool-level instrumentation; runs/stages/verification are instrumented today.
- Pipeline budget enforcement is per-stage boundary: cumulative cost gates the next dispatch and cancels the execution; single-agent runs additionally cancel mid-flight when the CLI streams usage lines.

---

## 8. Organization Guardrails

- Não enviar métricas ou telemetria para endpoints externos sem consentimento configurado explicitamente no `appsettings.json`.
- Valores monetários devem usar tipos `decimal` para evitar imprecisões de ponto flutuante.

---

## 9. Definition of Done

- [x] Telemetria OpenTelemetry funcional em todos os runs.
- [x] Cálculo de custos validado com testes unitários.
- [x] Tela de FinOps disponível na UI e teto de orçamento operacional.
