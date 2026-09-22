# SPEC-20260922-living-specs-default-view

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `living-specs-default-view` |
| Type | `Feature` (Frontend) |
| Stack | `.NET 10 / Blazor WASM / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-living-specs-default-view` |
| Ticket | [#325 — E20](https://github.com/afonsoft/agent-harness/issues/325) |
| Status | `Approved` |
| Referência | SPEC-20260919-ade-living-specs (tela atual), SPEC-20260918-issue-history (formato) |

## 1. User Story

**As a** Harness operator / arquiteto
**I want** que a visão default de `/specs` mostre todas as specs não-Done
**mais apenas as últimas 10 Done** — com um expansor "Show all Done"
**So that** a lista não seja dominada por dezenas de specs finalizadas e o
trabalho ativo (Draft/Approved/InImplementation/Deprecated) fique visível
de imediato.

**Problem context:**

`/specs` lista todo o corpus de `.specs/` ordenado por `Id` desc — hoje ~90+
arquivos, maioria `Done`. A visão "All" mistura trabalho ativo com histórico
encerrado, escondendo specs em Draft/Approved. Não há cap nem agrupamento;
filtros por status existem, mas o default é ruidoso.

## 2. Scope

**In scope:**

- Regra de partição da visão default (filtro `All`, sem query):
  **todos os specs com `Status != Done`** (inclui `Deprecated` — sempre
  visível) seguidos dos **últimos 10 `Done`** (ordem `Id` desc já retornada
  pelo endpoint).
- Expansor inline **"Show all N Done specs"** no fim da lista quando o cap
  trunca; ao expandir mostra todos os Done e vira "Show fewer".
- Helper puro e testável em `Application.Contracts`
  (`LivingSpecView.Partition`) — a lógica não fica presa no `.razor`
  (o projeto de testes não referencia `Taskboard.Blazor`).
- Filtros de status e busca inalterados: com filtro/query ativos, a listagem
  mostra tudo que casa (sem cap).

**Out of scope:**

- Mudança no endpoint `GET /api/specs` (payload segue completo — o cap é
  regra de apresentação).
- Paginação server-side, virtualização ou lazy-loading.
- Persistência da preferência "show all" (reseta a cada refresh).
- Reordenação por data/RFs ou novas colunas.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Application.Contracts/Specs/` — novo helper estático
  `LivingSpecView.Partition(specs, doneCap)` →
  `(IReadOnlyList<LivingSpecDto> Visible, int HiddenDoneCount)`:
  `Visible = non-Done (ordem original) ++ Done.Take(doneCap)`;
  `HiddenDoneCount = Done.Count - min(cap, Done.Count)`.
- `Taskboard.Blazor/Components/Pages/Specs.razor` — aplica `Partition` na
  visão default (`_statusFilter == "" && _query` vazio), flag local
  `_showAllDone` e a linha/botão expansor.
- Constante `DoneCap = 10`.

**Files to read before implementing:**

- `src/Taskboard.Blazor/Components/Pages/Specs.razor` — tela atual.
- `src/Taskboard.Application/Specs/SpecAppService.cs` — `ListAsync`
  (ordenação `Id` desc — a partição assume essa ordem).
- `src/Taskboard.Application.Contracts/Specs/SpecDtos.cs` —
  `LivingSpecDto`.

**Files to create or modify:**

```text
src/Taskboard.Application.Contracts/Specs/LivingSpecView.cs   [new]
src/Taskboard.Blazor/Components/Pages/Specs.razor             [modify]
tests/Taskboard.Tests.Unit/Specs/LivingSpecViewTests.cs       [new]
docs/features.md / docs/features.pt-br.md                     [modify — seção Living Specs]
```

## 4. Requirements

### RF-001: Partição da visão default

- **Description:** `LivingSpecView.Partition` separa a listagem em specs
  visíveis + contagem de Done ocultos.
- **Rules:**
  - Entrada: `IReadOnlyList<LivingSpecDto>` (ordem `Id` desc do endpoint) +
    `doneCap`.
  - Saída: `Visible` = todos `Status != "Done"` na ordem original,
    depois `Done.Take(doneCap)`; `HiddenDoneCount` = Done restantes.
  - Comparação de status case-insensitive (`spec.Status` é string no DTO).
  - `doneCap <= 0` → nenhum Done visível; lista vazia → `( [], 0 )`.
- **Input → Output:** lista completa → `(Visible, HiddenDoneCount)`.

### RF-002: UI — lista particionada + expansor

- **Description:** `/specs` renderiza `Visible` na visão default e o
  expansor quando `HiddenDoneCount > 0`.
- **Rules:**
  - Aplicação do cap somente quando `_statusFilter == ""` **e** `_query`
    vazio — filtro ou busca ativos exibem todos os matches (sem partição).
  - Expansor: linha final `Show all {HiddenDoneCount + shown} Done specs` →
    toggle `_showAllDone`; expandido mostra todos os Done e o botão vira
    "Show fewer Done".
  - `_showAllDone` reseta em `RefreshAsync`/troca de repo/filtro.
  - Ordem preservada: specs aparecem exatamente na ordem de `Visible`.
- **Input → Output:** `_specs` + flags → tabela particionada.

### RF-003: Testes

- **Description:** unit tests do `Partition` (o projeto de testes referencia
  `Application.Contracts`).
- **Rules:**
  - `Dado_ListaMista_Quando_Particiona_Entao_NaoDonePrimeiroDoneCap10`.
  - `Dado_MenosDe10Done_Quando_Particiona_Entao_HiddenZero`.
  - `Dado_Deprecated_Quando_Particiona_Entao_SempreVisivel`.
  - `Dado_CapZero_Quando_Particiona_Entao_NenhumDone`.
  - Ordem interna (não-Done e Done) preservada.
- **Input → Output:** red → green.

**Business rules / invariants:**

- Nunca esconder specs não-Done na visão default.
- `Deprecated` é sempre visível (decisão confirmada — não entra no cap).
- Nenhuma mudança de contrato/API — `GET /api/specs` inalterado.

## 5. API Contract

N/A — sem mudança de endpoint ou DTO; a partição é client-side sobre a lista
já retornada.

## 6. Acceptance Criteria

- [ ] **Given** 40 specs (30 Done + 10 ativas) **when** `/specs` abre sem
  filtro **then** a tabela mostra as 10 ativas + 10 Done mais recentes e o
  expansor "Show all 30 Done specs".
- [ ] **Given** o expansor clicado **when** renderiza **then** todos os Done
  aparecem e o botão vira "Show fewer Done".
- [ ] **Given** filtro `Done` selecionado **when** a lista carrega **then**
  todos os Done aparecem sem cap e sem expansor.
- [ ] **Given** uma busca ativa **when** há matches Done além de 10 **then**
  todos aparecem (busca desativa o cap).
- [ ] **Given** 5 Done apenas **when** a visão default carrega **then** os 5
  aparecem e não há expansor.
- [ ] **Given** specs `Deprecated` **when** a visão default carrega **then**
  aparecem integralmente na seção não-Done.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Status `Done` com casing diverso | `"done"` no DTO | conta como Done (comparação case-insensitive) |
| Apenas Done no repo | 25 Done, 0 ativas | 10 visíveis + expansor "Show all 25" |
| Refresh com expansor aberto | auto/manual refresh | `_showAllDone` reseta → volta ao cap |
| Lista vazia | repo sem `.specs/` | EmptyState existente, sem expansor |

## 7. Task Plan (agent execution)

- [x] **T1 — Red:** `LivingSpecViewTests` cobrindo RF-003.
- [x] **T2 — Helper:** `LivingSpecView.Partition` em
  `Application.Contracts/Specs/`.
- [x] **T3 — UI:** `Specs.razor` — partição na visão default, `_showAllDone`,
  expansor, reset em refresh/filtro.
- [x] **T4 — Docs + validação:** `docs/features*`; `dotnet build` + `dotnet
  test` (coverage ≥ 77%); DoD + PR.

**7.1 Validation strategy**

Frontend `.NET`: unit tests do helper puro; verificação manual da tela
(render + toggle + filtros); suite completa verde.

## 8. Organization Guardrails

- Branch `feature/devin-20260922-living-specs-default-view`; nunca em
  `main`/`master`/`develop`.
- Sem mudança de API/contrato — comportamento server-side intocado.
- Escopo fechado: nada de paginação, persistência de preferência ou novas
  colunas.

## 9. Definition of Done

- [ ] RF-001…RF-003 implementados.
- [ ] Critérios de aceite (seção 6) cobertos.
- [ ] Edge cases tratados.
- [ ] `dotnet build` limpo, `dotnet test` verde, cobertura ≥ gate.
- [ ] `docs/features*` atualizados.

**Next action after DoD is complete:** set `Status = Done` e abrir o PR na
branch `feature/...` referenciando o ticket.

## Open Questions / Pending Ambiguity

- Nenhuma — decisões confirmadas (Deprecated sempre visível; expansor
  "Show all Done" inline).
