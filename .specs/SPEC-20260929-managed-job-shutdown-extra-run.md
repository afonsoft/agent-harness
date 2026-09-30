# SPEC-20260929-managed-job-shutdown-extra-run

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `managed-job-shutdown-extra-run` |
| Type | `Bugfix` |
| Stack | `.NET 10 / ASP.NET Core (BackgroundService) / xUnit + Shouldly + NSubstitute` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `fix/devin-20260929-managed-job-shutdown-run` |
| Ticket | [#396](https://github.com/afonsoft/agent-harness/issues/396) — epic [#395](https://github.com/afonsoft/agent-harness/issues/395) |
| Status | `Approved` |

## 1. User Story

**As a** Harness operator
**I want** managed background jobs to not execute an extra run during service
shutdown
**So that** disabled jobs never run, and enabled jobs run exactly once per
schedule/startup — matching what the `CliMetricsSyncTests` contract expects.

**Problem context:**

Since the jobs-dashboard refactor (SPEC-20260929-jobs-dashboard, PR #383/#388),
`ManagedJobService.ExecuteAsync` parks on
`Task.WhenAny(delayTask, signalTask)` where `delayTask = Task.Delay(wait,
stoppingToken)`. When `StopAsync` cancels `stoppingToken`, the **cancelled**
`delayTask` still completes and wins the `WhenAny` — and the code treats
`winner == delayTask` as "interval elapsed", calling `RunOnceSafeAsync` one
extra time during shutdown.

Reproduced on `main` (`09eddba`), deterministic:

- `Dado_Enabled_Quando_HostedServiceInicia_Entao_SincronizaNoStartup` —
  `service.Calls` should be `1` but was `2` (startup run + spurious shutdown
  run).
- `Dado_Disabled_Quando_HostedServiceInicia_Entao_NaoSincroniza` —
  `service.Calls` should be `0` but was `1` (the disabled job ran once, on
  shutdown).

Impact: every `ManagedJobService` subclass (cli-metrics-sync and any future
managed job) executes once per host stop — including jobs whose effective
schedule is `Enabled=false`. On a `dotnet publish`/restart cycle this means
one extra sync/run per restart.

## 2. Scope

**In scope:**

- Fix `ManagedJobService.ExecuteAsync` so a `delayTask` cancelled by shutdown
  is never treated as "interval elapsed" — run only when the delay completed
  successfully (`delayTask.Status == TaskStatus.RanToCompletion` /
  `delayTask.IsCompletedSuccessfully`) or the token isn't cancelled.
- Keep the test contract: enabled → exactly 1 run at startup; disabled → 0
  runs ever (startup or shutdown).
- Keep manual `RunRequested` semantics unchanged (operator-triggered runs are
  allowed even when the schedule is disabled — existing behavior; verify it's
  intentional and document in the test or keep as-is).

**Out of scope:**

- Changing `JobRegistry.TryBeginRun` single-flight semantics.
- Refactoring the signal/channel plumbing.
- Retrying/quarantining the tests instead of fixing the root cause (rejected —
  the tests are correct; the implementation has the defect).
- Any changes to `CliMetricsSyncCoordinator` or `ICliMetricsService`.

## 3. Technical Context

**Where the change happens:** `src/Taskboard.Server/Services/ManagedJobService.cs`
— the `ExecuteAsync` loop's `WhenAny` winner check (~lines 40-70).

**Files to read before implementing:**

- `src/Taskboard.Server/Services/ManagedJobService.cs` — the buggy loop.
- `src/Taskboard.Server/Services/JobRegistry.cs` — `TryBeginRun`,
  `GetEffectiveAsync`, `JobSignal` semantics.
- `src/Taskboard.Server/Services/CliMetricsSyncService.cs` — the subclass under
  test.
- `tests/Taskboard.Tests.Unit/CliMetrics/CliMetricsSyncTests.cs` — the failing
  contract.
- `tests/Taskboard.Tests.Unit/Jobs/JobRegistryTestHost.cs` — test host.

**Files to create or modify:**

```text
src/Taskboard.Server/Services/ManagedJobService.cs   [mod — winner check]
tests/Taskboard.Tests.Unit/CliMetrics/CliMetricsSyncTests.cs  [mod — only if the loop change makes the assertions stricter; likely none needed]
tests/Taskboard.Tests.Unit/Jobs/  [maybe — a generic ManagedJobService shutdown test for any subclass]
```

## 4. Requirements

### RF-001: Delay cancelado não conta como intervalo decorrido
- **Description:** In `ExecuteAsync`, the branch that fires
  `RunOnceSafeAsync` on `winner == delayTask` must also require that the delay
  completed normally (not cancelled). A cancelled `stoppingToken` must exit
  the loop without running the job.
- **Rules:** `OperationCanceledException` on `stoppingToken` still unwinds via
  the existing catch; no behavioral change to `RunRequested`/`ScheduleChanged`
  handling; single-flight via `TryBeginRun` unchanged.
- **Input → Output:** `StopAsync` during a parked wait → no `RunJobAsync`
  invocation.

### RF-002: Disabled job nunca executa
- **Description:** A job whose effective schedule is `Enabled=false` must
  produce zero `RunJobAsync` calls across `StartAsync` → `StopAsync`,
  including the shutdown path covered by RF-001.
- **Input → Output:** `Dado_Disabled_Quando_HostedServiceInicia_Entao_NaoSincroniza`
  → `service.Calls == 0`.

### RF-003: Enabled job executa exatamente 1× no startup
- **Description:** With `Enabled=true` and a long interval, exactly one run at
  startup; shutdown adds zero.
- **Input → Output:** `Dado_Enabled_Quando_HostedServiceInicia_Entao_SincronizaNoStartup`
  → `service.Calls == 1`.

### RF-004: Cobertura da regressão
- **Description:** The existing two failing tests must pass deterministically
  (run 10× locally without a flake). If a generic guard test is cheap, add
  `ManagedJobServiceTests` asserting no shutdown-run for a stub subclass.

## 5. API Contract

N/A — internal hosted service.

## 6. Acceptance Criteria

- [ ] **Dado** um job habilitado **quando** o serviço inicia e para **então**
  `SyncAsync` é chamado exatamente 1 vez.
- [ ] **Dado** um job com `EnabledByDefault=false` **quando** o serviço inicia
  e para **então** `SyncAsync` nunca é chamado.
- [ ] **Dado** um sinal `RunRequested` **quando** o loop está parado **então**
  o job roda (comportamento manual preservado).
- [ ] `dotnet test --filter CliMetricsSyncTests` verde 10× consecutivas.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Stop durante delay | token cancelado | nenhuma execução extra |
| Stop durante run | IsRunning=true | `TryBeginRun` já guarda; sem mudança |
| `RunOnce=true` | wait=Infinite | sinal ScheduleChanged não executa |

## 7. Task Plan

- [ ] **T1 — Discovery:** ler `ManagedJobService.cs`, `JobRegistry.cs`,
  `CliMetricsSyncTests.cs`; confirmar que a causa é `winner == delayTask` em
  task cancelada.
- [ ] **T2 — Fix:** exigir `delayTask.IsCompletedSuccessfully` (ou check
  explícito de `stoppingToken.IsCancellationRequested`) antes de
  `RunOnceSafeAsync` no branch do delay.
- [ ] **T3 — Testes:** rodar `CliMetricsSyncTests` 10×; adicionar teste de
  shutdown genérico se aplicável.
- [ ] **T4 — Validação:** `dotnet build` (TreatWarningsAsErrors) + suíte unit.
- [ ] **T5 — Done + PR:** `Status = Done`, PR a partir de
  `fix/devin-20260929-managed-job-shutdown-run`.

## 8. Organization Guardrails

- Branch `fix/devin-20260929-managed-job-shutdown-run`; nunca push em `main`.
- `dotnet build` com TreatWarningsAsErrors limpo.
- Testes em pt-BR `Dado_Quando_Entao`.
- Não modificar `.github/workflows/**`.

## 9. Definition of Done

- [ ] `ManagedJobService` não executa job em shutdown por cancelamento de delay.
- [ ] `CliMetricsSyncTests` 4/4 verdes, 10 execuções seguidas sem flake.
- [ ] Build limpo; suíte unitária sem regressão.
- [ ] SPEC `Status: Done`; PR aberto.
