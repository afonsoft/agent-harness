# SPEC-20260922-workflow-actions-resilience

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `workflow-actions-resilience` |
| Type | `Bugfix` (Frontend + Integrations; inclui adição de strip "Latest runs") |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Blazor WASM / Octokit 14 / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260922-workflow-actions-resilience` |
| Ticket | [#324 — E19](https://github.com/afonsoft/agent-harness/issues/324) |
| Status | `Done` — delivered in PR #327 |
| Referência | SPEC-20260918-workflow-github-actions (tela atual), SPEC-20260911-refine-github-actions (mesma área — Actions), SPEC-20260919-legacy-workflow-surface (grafo legado removido) |

## 1. User Story

**As a** Harness operator
**I want** a tela `/workflow` para carregar de forma confiável — workflows do
repo selecionado, último run por workflow e os últimos 5 runs do repo —
sem o erro `Could not load workflows: net_http_request_timedout`
**So that** eu possa monitorar CI/CD de todos os repos do board num único
lugar, mesmo com rede lenta ou rate-limit do GitHub.

**Problem context:**

1. **Bug observado:** a tela falha com
   `Could not load workflows: net_http_request_timedout, 100` — o endpoint
   `GET /api/github/repos/{o}/{r}/workflows` executa **1 + até 20 chamadas
   Octokit** (`Actions.Workflows.List` + um `Runs.ListByWorkflow` por
   workflow para o last-run, 8 concorrentes). Com rede lenta ou rate-limit,
   o total excede o timeout (~100s) do `HttpClient` WASM → página morre sem
   dados.
2. **Ideia da tela (confirmada):** monitor de GitHub Actions — por workflow:
   card com nome/path/estado/badge do último run, expandível para os últimos
   10 runs; **+ strip "Latest runs" no topo com os últimos 5 runs do repo**
   (cross-workflow). Grafo de fluxo foi descartado (o grafo legado foi
   removido de propósito em SPEC-20260918; link ↗ resolve job detail).
3. Falha total é pior que dado parcial: hoje um timeout zera a tela
   (`_workflows = null`) mesmo que a lista de workflows já tenha chegado.

## 2. Scope

**In scope:**

- `GitHubService.GetWorkflowsAsync` reescrito: **2 chamadas Octokit** —
  `Actions.Workflows.List` + `Actions.Workflows.Runs.List` (repo-level,
  `PageSize` ≥ workflows, agrupado por `WorkflowId` para derivar o last-run
  de cada workflow). Elimina o N+1.
- Deadline server-side no endpoint de workflows
  (`Taskboard:GitHub:WorkflowsDeadlineSeconds`, default `25`): timeout ou
  falha do runs-call retorna workflows com `LastRun = null` (parcial) em vez
  de erro 500/timeout.
- Resposta do endpoint ganha `recentRuns` — os últimos 5 runs do repo
  (derivados da mesma chamada repo-level, custo zero).
- `Workflow.razor`: strip "Latest runs" (5) no topo; erro exibe alerta com
  botão **Retry** e **mantém o último resultado válido** (não zera a tela);
  mensagem amigável para timeout.
- `FakeGitHubService` fixtures + testes do novo mapeamento/degradação.

**Out of scope:**

- Trigger (`workflow_dispatch`), rerun, cancel — continua read-only.
- Job-level detail/logs — o link ↗ para o GitHub resolve.
- Gráfico de fluxo/pipeline — explicitamente descartado.
- Cache server-side (não escolhido; reavaliar se rate-limit persistir).
- Paginação de workflows além do cap atual de 20 no last-run enrichment.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Integrations/GitHub/GitHubService.cs` — `GetWorkflowsAsync`
  colapsa para 2 chamadas (`Runs.List` repo-level → `GroupBy(WorkflowId)` →
  `LastRun` = run mais recente por workflow); `MapRun`/`MapWorkflow`
  reutilizados.
- `Taskboard.Server/Program.cs` (~linha 1907) — endpoint de workflows ganha
  deadline CTS + resposta `{ workflows, recentRuns }`.
- `Taskboard.Application.Contracts/GitHub/` — response/DTO ganha
  `RecentRuns` (reusa `WorkflowRunDto`).
- `Taskboard.Blazor/Services/HttpGitHubService.cs` — desserializa
  `recentRuns`; `TaskboardClient`/contrato do client conforme o padrão.
- `Taskboard.Blazor/Components/Pages/Workflow.razor` — strip Latest runs +
  tratamento de erro com Retry + preservação do último dado válido.
- `FakeGitHubService` — fixtures de workflows+runs.

**Files to read before implementing:**

- `src/Taskboard.Integrations/GitHub/GitHubService.cs` (lines ~338-422) —
  N+1 atual e mappers.
- `src/Taskboard.Server/Program.cs` (~1907-1941) — endpoints.
- `src/Taskboard.Blazor/Components/Pages/Workflow.razor` — tela atual.
- `src/Taskboard.Application.Contracts/GitHub/` — `WorkflowDto`,
  `WorkflowRunDto`, `IGitHubService`.
- `src/Taskboard.Blazor/Services/HttpGitHubService.cs` + `FakeGitHubService`.
- Octokit 14: confirmar `Actions.Workflows.Runs.List(owner, name,
  WorkflowRunsRequest, ApiOptions)` e `WorkflowRun.WorkflowId`.

**Files to create or modify:**

```text
src/Taskboard.Integrations/GitHub/GitHubService.cs          [modify]
src/Taskboard.Server/Program.cs                             [modify — endpoint + deadline]
src/Taskboard.Application.Contracts/GitHub/WorkflowDtos.cs  [modify — RecentRuns / WorkflowMonitorDto]
src/Taskboard.Blazor/Services/HttpGitHubService.cs          [modify]
src/Taskboard.Blazor/Components/Pages/Workflow.razor        [modify]
src/Taskboard.Integrations/GitHub/FakeGitHubService.cs      [modify — fixtures]
tests/Taskboard.Tests.Unit/GitHub/*                         [new/modify]
tests/Taskboard.Tests.Integration/*                         [modify — endpoint tests]
docs/features.md / docs/features.pt-br.md                   [modify]
docs/api.md / docs/api.pt-br.md                             [modify]
```

## 4. Requirements

### RF-001: Last-run sem N+1

- **Description:** `GetWorkflowsAsync` deriva o last-run de cada workflow a
  partir de **uma** chamada repo-level de runs, agrupada por `WorkflowId`.
- **Rules:**
  - Chamadas: `Actions.Workflows.List` + `Actions.Workflows.Runs.List` com
    `PageSize = min(100, max(5 × workflowCount, 20))` — nunca mais de 2
    requests.
  - `LastRun` = run com maior `CreatedAt` dentro do grupo do workflow;
    workflow sem run no retorno → `LastRun = null` ("No runs yet").
  - Ordenação do resultado mantém o comportamento atual: `LastRun.CreatedAt`
    desc, workflows sem runs por último.
  - `GetWorkflowRunsAsync` (runs por workflow, expand) permanece inalterado.
- **Input → Output:** `owner/repo` → `IReadOnlyList<WorkflowDto>` em ≤ 2
  chamadas GitHub.

### RF-002: Deadline + degradação parcial

- **Description:** o endpoint nunca devolve timeout cru; em falha/estouro de
  deadline no runs-call retorna a lista de workflows sem badges.
- **Rules:**
  - `Taskboard:GitHub:WorkflowsDeadlineSeconds` (default `25`) cria um CTS
    linkado ao `CancellationToken` da request ao redor do runs enrichment.
  - `OperationCanceledException`/`ApiException`/timeout no runs-call →
    workflows retornados com `LastRun = null`; log `Warning` sem PII.
  - Falha no `Workflows.List` (repo inexistente/sem permissão) → comportamento
    atual preservado (`404 repo-not-found` / lista vazia).
  - O deadline aplica-se ao endpoint, não ao serviço — `IGitHubService` segue
    recebendo o `ct` do caller.
- **Input → Output:** runs-call lento → `200 { workflows[], recentRuns: [] }`
  parcial; nunca exception vazando como timeout na tela.

### RF-003: `recentRuns` — últimos 5 runs do repo

- **Description:** a resposta do endpoint inclui os 5 runs mais recentes do
  repo (cross-workflow), derivados da mesma chamada de RF-001 — custo zero.
- **Rules:**
  - `recentRuns` = top 5 por `CreatedAt` desc do resultado repo-level; vazio
    quando o runs-call falhou/degradou.
  - Item = `WorkflowRunDto` existente + `WorkflowId`/`WorkflowName` (campo
    necessário para rotular o run na strip — adicionar ao DTO ou wrapper).
- **Input → Output:** runs do repo → top 5 `WorkflowRunDto`+workflow name.

### RF-004: UI — strip "Latest runs" + erro resiliente

- **Description:** `/workflow` ganha uma seção "Latest runs" (5) acima dos
  cards de workflow; falhas mostram alerta com Retry e preservam o dado.
- **Rules:**
  - Strip: card compacto com linhas `run name · workflow · badge · branch ·
    tempo relativo · link ↗`; oculta quando `recentRuns` vazio.
  - Em erro de carga: alert `danger` com mensagem amigável ("GitHub is
    taking too long — try again") + botão Retry; **`_workflows` anterior é
    preservado** (não atribuir `null`) — stale data + aviso em vez de tela
    vazia.
  - Auto-refresh de 60s e regras de badge existentes permanecem.
- **Input → Output:** `GetWorkflowsAsync` falha → alerta + lista anterior;
  sucesso → strip + cards atualizados.

### RF-005: Testes

- **Description:** cobrir o novo agrupamento, degradação e DTO.
- **Rules:**
  - Unit (NSubstitute/fake Octokit boundary conforme padrão existente):
    last-run por `WorkflowId` correto; runs-call falha → `LastRun = null` em
    todos; `recentRuns` top-5; ordenação.
  - Integração: endpoint retorna `workflows`+`recentRuns`; 401 sem auth;
    404 repo inexistente.
  - Reprodução primeiro: teste vermelho simulando runs-call lenta/timeout.
- **Input → Output:** red → green.

**Business rules / invariants:**

- Nunca mais de 2 chamadas GitHub por carga da tela.
- Falha parcial nunca esconde a lista de workflows já obtida.
- Read-only: nenhuma write na Actions API.
- Sem secrets/PII em logs.

## 5. API Contract

**Endpoint:** `GET /api/github/repos/{owner}/{repo}/workflows` (rota inalterada)
**Auth:** `RequireAuthorization` (existente).

**Response (success):**

```json
{
  "workflows": [
    {
      "id": 1, "name": "dotnet", "path": ".github/workflows/dotnet.yml",
      "state": "active", "htmlUrl": "https://github.com/o/r/actions/...",
      "lastRun": { "id": 9, "name": "dotnet", "displayTitle": "fix: ...",
        "runNumber": 42, "event": "push", "status": "completed",
        "conclusion": "success", "headBranch": "main", "headSha": "abc",
        "actor": "dev", "createdAt": "...", "updatedAt": "...",
        "runStartedAt": "...", "htmlUrl": "..." }
    }
  ],
  "recentRuns": [
    { "id": 9, "workflowId": 1, "workflowName": "dotnet", "...": "WorkflowRunDto" }
  ]
}
```

**Expected errors:** `401` unauthenticated; `404 { error: "repo-not-found" }`;
degradação parcial retorna `200` com `lastRun`/`recentRuns` vazios — nunca
`5xx` por timeout do enrichment.

## 6. Acceptance Criteria

- [ ] **Given** um repo com 3 workflows e runs **when** a tela carrega
  **then** ≤ 2 chamadas Octokit ocorrem e cada card mostra o badge do último
  run correto.
- [ ] **Given** o runs-call excede o deadline **when** o endpoint responde
  **then** retorna `200` com workflows e `lastRun`/`recentRuns` nulos/vazios
  — sem timeout na UI.
- [ ] **Given** runs no repo **when** a tela renderiza **then** a strip
  "Latest runs" mostra os 5 mais recentes com workflow, badge, branch e link.
- [ ] **Given** uma falha de carga **when** já existia lista renderizada
  **then** o alerta com Retry aparece e a lista anterior permanece visível.
- [ ] **Given** repo sem Actions **when** carrega **then** EmptyState
  inalterado ("No GitHub Actions workflows in this repository.").

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| > 20 workflows | repo grande | runs-call único cobre todos; sem cap de enrichment (a chamada é uma só) |
| Run sem `WorkflowId` mapeado | item órfão no group | entra em `recentRuns`; não quebra last-run |
| `Workflows.List` OK, `Runs.List` 403 rate-limit | resposta parcial | `200` com `lastRun` nulos + log warn |
| Retry após erro | clique em Retry | nova carga completa; alerta some no sucesso |
| Troca de repo durante carga | stale response | descarte existente preservado (comparação de `RepoService.Selected`) |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** confirmar surface Octokit 14 (`Runs.List`
  repo-level + `WorkflowRun.WorkflowId`); ler arquivos da seção 3.
- [x] **T2 — Red:** teste reproduzindo o timeout (runs-call lenta → exceção
  hoje) + testes do agrupamento/degradação/top-5.
- [x] **T3 — Integrations:** `GetWorkflowsAsync` com 2 chamadas + group-by +
  `recentRuns`; DTOs.
- [x] **T4 — Server:** deadline CTS + resposta parcial + log.
- [x] **T5 — Client/UI:** `HttpGitHubService` + `Workflow.razor` (strip,
  Retry, preservação de dados).
- [x] **T6 — Docs + validação:** `docs/api*`, `docs/features*`;
  `dotnet build` + `dotnet test` (coverage ≥ 77%); DoD + PR.

**7.1 Validation strategy**

Bugfix: teste de reprodução antes do fix; unit tests do agrupamento e da
degradação; integration tests do endpoint; suite completa verde.

## 8. Organization Guardrails

- Branch dedicada `feature/devin-20260922-workflow-actions-resilience`;
  nunca em `main`/`master`/`develop`.
- `.github/workflows/` intocado (a tela só lê a API do GitHub).
- Read-only na Actions API; sem secrets/PII em logs.
- Sem gráfico de fluxo, sem writes, sem cache (escopo fechado).

## 9. Definition of Done

- [ ] Todos os requisitos (seção 4) implementados.
- [ ] Critérios de aceite (seção 6) cobertos por testes verdes.
- [ ] Edge cases tratados.
- [ ] `dotnet build` limpo (warnings as errors), `dotnet test` verde,
  cobertura ≥ gate.
- [ ] Guardrails respeitados; logs sem PII/tokens.
- [ ] `docs/api*` + `docs/features*` atualizados.

**Next action after DoD is complete:** set `Status = Done` e abrir o PR na
branch `feature/...` referenciando o ticket.

## Open Questions / Pending Ambiguity

- `WorkflowName` em `recentRuns`: adicionar campo a `WorkflowRunDto` (leve
  breaking additive) ou wrapper `RecentRunDto` — decidir na implementação
  conforme o consumo existente do DTO.
