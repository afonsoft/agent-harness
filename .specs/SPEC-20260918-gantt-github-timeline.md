# SPEC-20260918-gantt-github-timeline

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `gantt-github-timeline` |
| Type | `Feature` (refactor da tela Gantt) |
| Stack | `.NET 10 / Blazor / Octokit` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-gantt-github-timeline` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** usuário do Harness
**I want** que a tela Gantt leia as issues do GitHub (criação, fechamento, milestone, transições de coluna)
**So that** o timeline reflita o trabalho real do board, com métricas de kanban (lead time, cycle time, throughput, WIP, aging) em vez das tarefas locais que não são mais a fonte da verdade.

**Problem context:**
`Gantt.razor` lê `GetProjectsAsync`/`GetTasksAsync` — as entidades locais `Project`/`Task`, que serão removidas (SPEC-20260918-projects-removal). O board real vive em issues do GitHub (`IssueDto` com `CreatedAt`, `ClosedAt`, `Labels`, `Column`, `AssigneeLogin`, `Priority`). Issues não têm StartDate/DueDate nativos — a decisão aprovada é **barra = `CreatedAt` → `ClosedAt`** (issues abertas → hoje), com **milestone `due_on` como marcador de deadline**, e métricas de fluxo derivadas de eventos `labeled`/`unlabeled` do timeline do GitHub (Octokit) somados aos `IssueHistoryEvent` locais.

## 2. Scope

**In scope:**
- `Gantt.razor` reescrito: seletor de **repositório** (os repos configurados do board, mesma fonte do KanbanBoard) em vez de projeto.
- Barras por issue: `createdAt → closedAt`; abertas terminam em "hoje" (barra tracejada/em andamento); cor por coluna atual; tooltip com datas, coluna, assignee e lead time da issue.
- Marcador de deadline: milestone `due_on` (via `Issue.Milestone` do Octokit) como losango no fim da barra ou marcador vertical por milestone na timeline.
- Painel de métricas do kanban (cards no topo):
  - **Lead time** médio e mediana (created→closed, issues fechadas no período visível).
  - **Cycle time** médio (primeira saída do backlog → done/closed) reconstruído de `labeled`/`unlabeled` timeline events + `IssueHistoryEvent` local.
  - **Throughput**: issues fechadas por semana (série simples das últimas 8 semanas).
  - **WIP**: issues abertas fora do backlog agora; **Aging**: idade mediana das abertas.
- Endpoint(s) novos: `GET /api/github/{owner}/{repo}/timeline` (issues + milestones + transições) e `GET /api/github/{owner}/{repo}/metrics` (agregados já calculados — evita N chamadas no client).
- `GitHubService`/`IGitHubService`: métodos `GetMilestonesAsync`, `GetIssueTimelineEventsAsync` (paginado, `issues.Events.GetAllForIssue` ou timeline API).

**Out of scope:**
- Edição de datas pelo Gantt (read-only).
- Dependências entre issues (blocked-by) — futuro.
- Zoom/pan/escala semanal-mensal — manter a escala automática atual.
- Remoção das entidades `Project`/`Task` — SPEC-20260918-projects-removal.

## 3. Technical Context

**Files to read:**
- `src/Taskboard.Blazor/Components/Pages/Gantt.razor` (tela atual, CSS `gantt-*` reutilizável)
- `src/Taskboard.Application.Contracts/GitHub/IssueDto.cs`, `BoardColumnDto.cs`, `GitHubBoardColumnExtensions.cs`
- `src/Taskboard.Integrations/GitHub/GitHubService.cs` (MapToDto, Octokit client)
- `src/Taskboard.Domain/Issues/IssueHistoryEvent.cs` (transições locais registradas)
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` (fonte da lista de repos configurados)
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` + `HttpGitHubService`

**Files to create/modify:**
- `src/Taskboard.Application.Contracts/GitHub/` — `IssueTimelineEventDto`, `MilestoneDto`, `RepoMetricsDto`, `GanttIssueDto`.
- `IGitHubService` + `GitHubService` (Octokit) + `HttpGitHubService` (client).
- `src/Taskboard.Server/Program.cs` — endpoints.
- `src/Taskboard.Blazor/Components/Pages/Gantt.razor` — rewrite.
- `tests/` — unit (agregador de métricas) + integration (endpoints com `FakeGitHubService`).

## 4. Functional Requirements

- **RF-001** `GET /api/github/{owner}/{repo}/timeline` retorna `{ issues: [{ id, number, title, column, priority, assigneeLogin, createdAt, closedAt, milestoneNumber, milestoneDueOn, transitions: [{ at, from, to }] }], milestones: [{ number, title, dueOn, state }] }`. Transições = union ordenada dos eventos `labeled`/`unlabeled` do GitHub e `IssueHistoryEvent` (kind=Moved) locais, dedup por (at,from,to).
- **RF-002** `GET /api/github/{owner}/{repo}/metrics?days=90` retorna `{ leadTimeAvgDays, leadTimeMedianDays, cycleTimeAvgDays, throughputPerWeek: [{ weekStart, closed }], wip, openMedianAgeDays }` calculados no servidor a partir das transições do RF-001.
- **RF-003** Gantt renderiza uma barra por issue: `createdAt → closedAt ?? hoje`; aberta = estilo "em andamento"; milestone `dueOn` vira marcador; hoje = linha vertical (já existe). Issues fechadas há mais do range visível ficam fora (filtro `days` com default 90).
- **RF-004** Painel de métricas renderiza os 5 cards acima do gráfico, calculados sobre o mesmo período.
- **RF-005** Seletor de repositório lista os repos configurados do board; sem repo → EmptyState.
- **RF-006** `IGitHubService` ganha `GetIssueTimelineEventsAsync` (paginado; falha numa issue não derruba o conjunto — best-effort como `GetIssueHistoryAsync`) e `GetMilestonesAsync`. `FakeGitHubService` implementa stubs determinísticos.
- **RF-007** Timeline events do GitHub têm rate-limit cost — buscar em paralelo limitado (≤8 issues concorrentes) e só para issues do período (default ≤200).

## 5. API Contract

```
GET /api/github/{owner}/{repo}/timeline?days=90
→ 200 { issues: [...], milestones: [...] }  | 401 | 404 repo-not-configured

GET /api/github/{owner}/{repo}/metrics?days=90
→ 200 { leadTimeAvgDays, leadTimeMedianDays, cycleTimeAvgDays,
        throughputPerWeek: [...], wip, openMedianAgeDays } | 401 | 404
```

## 6. Acceptance Criteria

- **AC1** Dado repo com issues fechadas, quando abro `/gantt`, então vejo barras de created→closed ordenadas por data, hoje marcado e milestones como marcadores.
- **AC2** Dada issue aberta, sua barra termina em hoje com estilo distinto (em andamento).
- **AC3** Dado o painel, lead time e cycle time batem com os eventos de label do `FakeGitHubService` nos testes de integração.
- **AC4** Dada falha do timeline de uma issue, as demais ainda carregam e a issue aparece sem transições (best-effort).
- **AC5** Repo sem issues/milestones → EmptyState, métricas zeradas sem erro.

## 7. Task Plan

- T1: DTOs + `IGitHubService.GetMilestonesAsync`/`GetIssueTimelineEventsAsync` (Octokit) + `FakeGitHubService` + client HTTP.
- T2: `TimelineMetricsService` (Application) — monta transições (GitHub + IssueHistoryEvent) e calcula agregados; unit tests.
- T3: Endpoints `/timeline` + `/metrics`; integration tests.
- T4: `Gantt.razor` rewrite (repo selector, barras, milestone markers, metrics cards).
- T5: docs en/pt-br + build + suites.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-gantt-github-timeline`; sem commit em main.
- Octokit token nunca logado; timeline fetch best-effort (não bloquear tela por rate-limit).
- Manter CSS `gantt-*` existente; padrões Blazor.Bootstrap.

## 9. Definition of Done

- [x] Tela mostra timeline real do repo com métricas corretas sobre dados fake/reais.
- [x] Unit + integration verdes; `dotnet build` limpo. (462 unit + 147 integration)
- [x] docs/features.md + api.md en/pt-br atualizados; SPEC → Done.
- [ ] Deploy + verificação em produção.
