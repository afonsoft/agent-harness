# SPEC-20260928-ef-value-comparers

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ef-value-comparers` |
| Type | `Backend / Persistence` |
| Stack | `.NET 10 / EF Core 10 / SQLite` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-ef-value-comparers` |
| Ticket | [#357](https://github.com/afonsoft/agent-harness/issues/357) (Epic [#356](https://github.com/afonsoft/agent-harness/issues/356)) |
| Status | `Done` |
| Related | `SPEC-20260919-harness-context-memory` (E7 `Tags`), `SPEC-20260919-ade-multi-agent-orchestration` (E12 `DependsOn`/`TriedAgents`) |

## 1. User Story

**As a** mantenedor do Harness
**I want** que as propriedades de coleção persistidas via `ReadOnlyListStringJsonValueConverter` declarem um `ValueComparer`
**So that** o EF Core detecte mutações na coleção (change tracking por snapshot) e pare de emitir o warning de modelo "collection without value comparer" nos logs.

**Problem context:**

Três propriedades `IReadOnlyList<string>` usam `ReadOnlyListStringJsonValueConverter` sem `ValueComparer` (registrado como warning pré-existente nas verificações E7/E12 em `.claude/memory/orchestrator_stats.md`):

- `PipelineStageExecution.DependsOn` — `src/Taskboard.EntityFrameworkCore/Configurations/PipelineConfigurations.cs:78-80`
- `PipelineStageExecution.TriedAgents` — `src/Taskboard.EntityFrameworkCore/Configurations/PipelineConfigurations.cs:82-84`
- `ProjectMemoryItem.Tags` — `src/Taskboard.EntityFrameworkCore/Configurations/ProjectMemoryItemConfiguration.cs:40-42`

Sem comparer, o EF Core compara a coleção por referência: mutações in-place (ou uma nova lista com o mesmo conteúdo) não são detectadas corretamente — `SaveChanges` pode ignorar mudanças reais ou gerar updates desnecessários. `grep -rn "ValueComparer" src/` retorna 0 ocorrências.

## 2. Scope

**In scope:**

- Novo `ListStringValueComparer : ValueComparer<IReadOnlyList<string>>` em `src/Taskboard.EntityFrameworkCore/ValueConverters/` (compara por sequência; snapshot imutável).
- `.HasConversion(converter, comparer)` (ou `.Metadata.SetValueComparer`) nas três propriedades listadas.
- Teste de unidade provando que uma entidade "modified" com coleção mutada gera UPDATE (mudança detectada) e que coleção equivalente não gera update.

**Out of scope:**

- Migration de schema — o comparer não altera o modelo persistido (nenhuma migration nova; se o EF gerar diff vazio, documentar).
- Outras propriedades `HasConversion<string>` (enums/ids) — comparer default já é correto.
- Refatorar `ListStringJsonValueConverter`/`ReadOnlyListStringJsonValueConverter` existentes além de adicionar o comparer.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.EntityFrameworkCore/ValueConverters/` — novo arquivo `ListStringValueComparer.cs`.
- `src/Taskboard.EntityFrameworkCore/Configurations/PipelineConfigurations.cs` — `DependsOn`, `TriedAgents`.
- `src/Taskboard.EntityFrameworkCore/Configurations/ProjectMemoryItemConfiguration.cs` — `Tags`.
- `tests/Taskboard.Tests.Unit/` ou `tests/Taskboard.Tests.Integration/` — teste de change detection.

**Files to read before implementing:**

- `src/Taskboard.EntityFrameworkCore/ValueConverters/ReadOnlyListStringJsonValueConverter.cs`
- `src/Taskboard.EntityFrameworkCore/ValueConverters/ListStringJsonValueConverter.cs` (se existir variante `List<string>`)
- As duas configurações citadas.

**Files to create or modify:**

```text
src/Taskboard.EntityFrameworkCore/ValueConverters/ListStringValueComparer.cs          # new
src/Taskboard.EntityFrameworkCore/Configurations/PipelineConfigurations.cs             # modified
src/Taskboard.EntityFrameworkCore/Configurations/ProjectMemoryItemConfiguration.cs     # modified
tests/.../ValueComparerChangeTrackingTests.cs                                          # new
```

## 4. Requirements

### RF-001: Comparer de coleção de strings

- **Description:** `ListStringValueComparer` implementa `ValueComparer<IReadOnlyList<string>>`: `Equals` por `SequenceEqual` (null-safe), `GetHashCode` agregando elementos, snapshot retornando a lista (imutável por convenção — converter desserializa nova instância).

### RF-002: Aplicação nas três propriedades

- **Description:** `DependsOn`, `TriedAgents` e `Tags` passam a usar `HasConversion(converter, comparer)` — ou `HasConversion(converter)` + `builder.Property(...).Metadata.SetValueComparer(comparer)`.

### RF-003: Change detection real

- **Description:** Mutar `Tags`/`DependsOn` (lista nova ou mesmo conteúdo alterado) em uma entidade tracked deve produzir UPDATE no `SaveChanges`; reatribuir coleção equivalente não deve sujar a entidade.

## 5. API Contract

Sem mudança de API ou schema de banco — somente comportamento interno do EF Core.

## 6. Acceptance Criteria

- [x] **Given** as três configurações **when** o modelo é construído **then** nenhuma warning "value converter without value comparer" é emitida para `DependsOn`/`TriedAgents`/`Tags`.
- [x] **Given** uma entidade tracked **when** a coleção é mutada **then** `SaveChanges` persiste a mudança (teste verde).
- [x] **Given** coleção reatribuída com mesmo conteúdo **when** `SaveChanges` roda **then** nenhum UPDATE desnecessário ocorre (verificado via `ChangeTracker.HasChanges()` ou log).
- [x] **Given** `dotnet build` + `dotnet test` **when** executados **then** build limpo (TreatWarningsAsErrors) e suíte verde.

## 7. Task Plan (agent execution)

- [x] **T1 — Implementation:** criar `ListStringValueComparer` e aplicar nas 3 propriedades.
- [x] **T2 — Tests:** teste de change detection (mutação detectada; equivalência ignorada).
- [x] **T3 — Verification:** `dotnet build` + `dotnet test`; confirmar ausência do warning de modelo em output/log.
- [x] **T4 — Done + PR:** `Status = Done` e PR na branch do spec.

## 8. Organization Guardrails

- `src/Taskboard.EntityFrameworkCore/**` — camada de infraestrutura; nenhuma mudança em Domain/Application.
- `.github/workflows/**` intocado.
- Sem migration nova — se `dotnet ef migrations` gerar diff não-vazio, parar e escalar.

## 9. Definition of Done

- [ ] Comparer aplicado nas 3 propriedades com teste provando detecção de mutação.
- [ ] Build/test verde; nenhum warning novo.
- [ ] `Status = Done` no mesmo PR.
