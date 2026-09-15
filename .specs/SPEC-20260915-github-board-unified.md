# SPEC-20260915-github-board-unified

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `github-board-unified` |
| Type | `Frontend / Feature` |
| Stack | `.NET 10 / Blazor Server / Blazor.Bootstrap / Octokit` |
| Repository | `taskboard-ai` |
| Branch | `feature/devin-20260915-github-board-unified` |
| Ticket | N/A |
| Status | Approved |

## 1. User Story

**As a** Taskboard user,
**I want** the main board (`/`) to be a GitHub-only board where I type or pick a repository and see issues in the same layout used for local projects (search + priority filter + full status columns),
**So that** I have a single, consistent board experience and can target any repository — including ones not in my repo list — without a separate `/github-board` page.

**Problem context:**

- The board selector (`BoardView.razor`) mixes two groups: `Projects` (local SQLite tasks) and `GitHub` (repositories). The user wants the dropdown simplified to GitHub only, with a free-text repo field.
- The GitHub board (`KanbanBoard`) renders only 4 columns (`backlog`, `in-progress`, `review`, `done`) and has no search/priority filters, while the local-projects board has text + priority filters and 7 status columns + archived. The projects layout is the reference ("correct") layout.
- **Bug already fixed in working tree (precondition):** `KanbanBoard.LoadIssuesAsync` passed all 4 column labels to `GetIssuesAsync`; the GitHub issues API treats `labels` as an AND filter, so only issues carrying *all* labels were returned — newly created issues never appeared. Fix applied: fetch without label filter and group client-side; `EnsureLabelExistsAsync` now guards `CreateIssueAsync`/`UpdateIssueColumnAsync` against HTTP 422 when a column label does not exist in the repo.

## 2. Scope

**In scope:**

- Replace the `<select>` (Projects + GitHub optgroups) in `BoardView` with a GitHub-only repository field: text input with `<datalist>` autocomplete listing the authenticated user's repositories; free-form `owner/repo` entry allowed.
- Unify the GitHub board rendering with the projects layout: search filter, priority filter, priority legend, status-column styling, archived column.
- Extend `GitHubBoardColumn` to 9 columns: `backlog`, `todo`, `in-progress`, `in-review`, `in-pullrequest`, `blocked`, `done`, `canceled`, `archived` (labels kebab-case; `archived` is a presentation-only pseudo-column with no label).
- Show closed GitHub issues from the last 3 months: label `done` → `done`, label `canceled` → `canceled`, any other/no status label → `archived`.
- Priority via labels `priority:urgent`, `priority:high`, `priority:medium`, `priority:low`; no label → `None`.
- Move the local-projects board to a new `/projects` page; remove `/github-board` page, its NavMenu entry, and `RepositorySelector`.
- Keep GitHub board interactions: "Nova Tarefa" dialog, drag-and-drop, agent-selection modal (triggered when dropping into `in-progress` or `backlog`), task detail dialog.

**Out of scope:**

- No changes to local task domain/persistence, REST API endpoints, CLI or MCP.
- No persistence of the last selected repository (follow-up).
- No GitHub Projects (v2) integration.
- No authentication/token UI on the board — token remains `GITHUB_TOKEN` env / Settings.
- No date fields (Start/End/Duration) for GitHub issues — GitHub issues have no such fields; the header legend area shows the priority legend only.

## 3. Technical Context

**Where the change happens:** `Taskboard.Blazor` UI (board page, Kanban components, NavMenu) and `Taskboard.Application.Contracts`/`Taskboard.Integrations` (column enum, label map, issue DTO/fetch semantics). Server-side Blazor — `IGitHubService` is injected directly, no REST endpoints involved.

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `.specs/SPEC-20260913-board-repo-selector.md` (current selector behavior)
- `.specs/SPEC-010-integrations.md` (GitHub service contract)
- `src/Taskboard.Blazor/Components/BoardView.razor`
- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor`
- `src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor`
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor`
- `src/Taskboard.Blazor/Components/Pages/GitHubBoard.razor`
- `src/Taskboard.Blazor/Components/GitHub/RepositorySelector.razor`
- `src/Taskboard.Blazor/Layout/NavMenu.razor`
- `src/Taskboard.Application.Contracts/GitHub/{GitHubBoardColumn,GitHubBoardColumnExtensions,IssueDto,IGitHubService}.cs`
- `src/Taskboard.Integrations/GitHub/GitHubService.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs` (uses `GitHubBoardColumn.Review`)

**Files to create or modify:**

```text
src/Taskboard.Blazor/Components/BoardView.razor                     # repo input + datalist, filters, unified GitHub board
src/Taskboard.Blazor/Components/Pages/ProjectsBoard.razor           # NEW: local projects board moved from /
src/Taskboard.Blazor/Components/Pages/GitHubBoard.razor             # DELETE
src/Taskboard.Blazor/Components/GitHub/RepositorySelector.razor     # DELETE
src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor            # unified layout: filters + 9 columns
src/Taskboard.Blazor/Components/GitHub/NewTaskDialog.razor          # initial column list = label-backed columns
src/Taskboard.Blazor/Layout/NavMenu.razor                           # - GitHub Board, + Projects
src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumn.cs     # 9 members
src/Taskboard.Application.Contracts/GitHub/GitHubBoardColumnExtensions.cs # kebab labels + legacy aliases + priorities
src/Taskboard.Application.Contracts/GitHub/IssueDto.cs              # + ClosedAt, + Priority
src/Taskboard.Application.Contracts/GitHub/IGitHubService.cs        # fetch open + recently closed
src/Taskboard.Integrations/GitHub/GitHubService.cs                  # state=all, ClosedAt/Priority mapping, column resolution
src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs      # Review -> InReview
tests/Taskboard.Tests.Unit/Application/Contracts/GitHub/GitHubBoardColumnExtensionsTests.cs
tests/Taskboard.Tests.Unit/Integrations/GitHub/GitHubBoardGrouperTests.cs   # NEW (pure grouping/filter logic)
```

## 4. Requirements

### RF-001: Repository field with autocomplete

- **Description:** The board header "Project Name" select is replaced by a text input (`form-control`, accessible label "Repository") bound to a `<datalist>` containing every repository `FullName` (`owner/repo`) from `IGitHubService.GetRepositoriesAsync()`, sorted alphabetically. Typing a value not in the list is allowed.
- **Rules:** load triggers on input change (commit) and Enter; value must match `owner/repo` (two non-empty segments) — otherwise show `alert-warning` and keep the current board; datalist empty/absent (list load failure) still allows free typing.
- **Input → Output:** `owner/repo` string → board loads that repo's issues.

### RF-002: Local projects board moved to `/projects`

- **Description:** The local-task board (project selector with local projects only, Start/End/Duration fields, priority legend, text + priority filters, 7 status columns + archived) moves unchanged to a new page `ProjectsBoard.razor` at route `/projects`. NavMenu gains a "Projects" link and loses "GitHub Board"; `/github-board` and `RepositorySelector.razor` are deleted.
- **Input → Output:** `/projects` → identical behavior to today's local-project board; `/github-board` → 404/removed.

### RF-003: Extended status model (contract)

- **Description:** `GitHubBoardColumn` becomes `Backlog, Todo, InProgress, InReview, InPullRequest, Blocked, Done, Canceled, Archived`. `GitHubBoardColumnExtensions.LabelMap` (kebab-case):
  `backlog`, `todo`, `in-progress`, `in-review`, `in-pullrequest`, `blocked`, `done`, `canceled`. `Archived` has no label and is excluded from `ToLabel()`/`GetAllLabels()` (label-backed columns only).
- **Rules:** legacy label `review` resolves to `InReview` for backward compatibility; `UpdateIssueColumnAsync` rejects `Archived` as target (cannot drop into archived — archived is derived from close-without-done/canceled).

### RF-004: Issue fetch includes recently closed

- **Description:** `IGitHubService.GetIssuesAsync` fetches issues with `State = ItemStateFilter.All` and returns open issues (any age) plus closed issues whose `ClosedAt` is within the last 3 months (90 days). `IssueDto` gains `DateTimeOffset? ClosedAt` and `string? Priority` (derived from `priority:*` labels).
- **Rules:** `labels` parameter may remain for API compat but the board no longer passes a label filter; closed-issue cutoff is computed server-side against `DateTimeOffset.UtcNow`.

### RF-005: Unified board layout

- **Description:** `KanbanBoard` renders the same layout idiom as the projects board: a filter row (text search + priority `<select>`) above a `.kanban-board` with one `.kanban-column` per status in fixed order `backlog, todo, in_progress, in_review, in_pullrequest, blocked, done, canceled, archived`, using the existing `GetStatusClass` mapping and `.kanban-label` counters. Column headers display the underscore names. Cards show title, `#number`, `@assignee`, priority swatch and labels; drag-and-drop, agent modal, detail dialog and "Nova Tarefa" preserved. The board header shows the repository field + priority legend (no Start/End/Duration).
- **Input → Output:** issues → 9 columns; filters narrow visible cards.

### RF-006: Column resolution and grouping rules

- **Description:**
  - Open issue: column = highest-precedence status label present (`done` > `canceled` > `in-pullrequest` > `in-review` > `in-progress` > `blocked` > `todo` > `backlog`; legacy `review` counts as `in-review`); no status label → `backlog`. An open issue labeled `done`/`canceled` is displayed in that column.
  - Closed issue (≤ 90 days): `done` label → `done`; `canceled` label → `canceled`; otherwise → `archived`.
  - Closed issue (> 90 days): not displayed.
- **Rules:** grouping/filtering implemented as a pure, testable helper (e.g. `GitHubBoardGrouper`) so unit tests cover every rule without Octokit.

### RF-007: New task dialog columns

- **Description:** `NewTaskDialog` initial-column select lists the 8 label-backed columns (no `archived`); default stays `Backlog`.

### RF-008: Error and empty states

- **Description:** `GITHUB_TOKEN` absent → repo input disabled + `alert-warning` hint to configure the token; repo list failure → `alert-warning`, free typing still works; issue load failure → `alert-danger`, previous selection retained; repo with no matching issues → columns render with `EmptyState`.

## 5. API Contract

No new/changed HTTP endpoints. Internal contract changes only:

- `IGitHubService.GetIssuesAsync(repositoryFullName, labels, cancellationToken)` — returns open + recently-closed issues (≤ 90 days); `labels` parameter becomes unused by the board.
- `IssueDto` gains `ClosedAt` and `Priority`.
- `GitHubBoardColumn` extended (9 members) — breaking for `Review` (renamed `InReview`); update `AgentOrchestrationService` accordingly.

## 6. Acceptance Criteria

- [ ] **Dado** um token `GITHUB_TOKEN` configurado **quando** abro `/` **então** vejo um campo de texto com autocomplete dos meus repositórios e nenhum grupo "Projects".
- [ ] **Dado** que digito `owner/repo` não presente na lista **quando** confirmo **então** o board carrega as issues desse repositório.
- [ ] **Dado** um repositório carregado **quando** crio uma issue via "Nova Tarefa" **então** ela aparece na coluna correta sem precisar recarregar a página.
- [ ] **Dado** issues abertas com labels `todo`, `in-progress`, `in-review`, `in-pullrequest`, `blocked` **quando** o board carrega **então** cada uma aparece na coluna correspondente; issues sem label de status aparecem em `backlog`.
- [ ] **Dado** issues fechadas nos últimos 3 meses **quando** o board carrega **então** as com label `done` aparecem em `done`, com `canceled` em `canceled`, e as demais em `archived`; fechadas há mais de 3 meses não aparecem.
- [ ] **Dado** issues com labels `priority:high` e sem label de prioridade **quando** filtro por "High" **então** só as `priority:high` permanecem; "None" mostra as sem label.
- [ ] **Dado** o board GitHub **quando** comparo com `/projects` **então** filtros (texto + prioridade), legenda de prioridade, estilo das colunas e coluna archived são idênticos.
- [ ] **Dado** uma issue arrastada para `in_progress` **quando** o drop ocorre **então** o modal de seleção de agente abre e a label `in-progress` é aplicada.
- [ ] **Dado** a página `/projects` **quando** acesso **então** o board de projetos locais funciona como antes; `/github-board` não existe mais no menu nem como rota.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Repo inválido | `owner` sem `/name` | `alert-warning`, board anterior preservado |
| Repo inacessível | `owner/repo` sem permissão | `alert-danger` com erro, seleção anterior mantida |
| Label inexistente no repo | criar issue com coluna `todo` | label `todo` criada automaticamente (EnsureLabelExists) |
| Issue aberta com label `done` | estado open + label done | aparece na coluna `done` |
| Issue com `review` (legado) | label `review` | coluna `in_review` |
| `GITHUB_TOKEN` ausente | env sem token | input desabilitado + alerta; `/projects` continua funcional |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler arquivos da seção 3 e confirmar padrões (Modal Blazor.Bootstrap, `GetStatusClass`, `TaskCard`).
- [ ] **T2 — Contracts:** estender `GitHubBoardColumn`, `GitHubBoardColumnExtensions` (labels kebab + alias `review`→`in-review` + prioridades), `IssueDto` (`ClosedAt`, `Priority`), `IGitHubService`; ajustar `AgentOrchestrationService` (`Review` → `InReview`).
- [ ] **T3 — Service:** `GitHubService` busca `State=All`, mapeia `ClosedAt`/`Priority`, resolve coluna com precedência e alias legado, filtra closed > 90d; extrair `GitHubBoardGrouper` puro para agrupamento/filtro.
- [ ] **T4 — Tests (red→green):** testes de unidade para `GitHubBoardGrouper` (resolução de coluna, janela de 90d, prioridade) e para `GitHubBoardColumnExtensions` (mapa de labels, aliases).
- [ ] **T5 — UI:** `BoardView` (input+datalist, header, legenda), `KanbanBoard` (filtros + 9 colunas + cards), `NewTaskDialog` (8 colunas), `ProjectsBoard` novo, deletar `GitHubBoard`/`RepositorySelector`, atualizar `NavMenu`.
- [ ] **T6 — Validation:** `dotnet build -c Release` (0 warnings) + `dotnet test`; smoke no container Docker (`docker build -t taskboard-ai .` + restart com `--env-file .env`).
- [ ] **T7 — Done + PR:** DoD completo → `Status = Done`, PR em `feature/devin-20260915-github-board-unified`.

**7.1 Validation strategy:** .NET — unit tests for business rules (grouper, extensions), integration where applicable; build with `TreatWarningsAsErrors`; manual smoke test of `/`, `/projects` in Docker.

## 8. Organization Guardrails

- **Branches:** nunca commitar em `main`, `master` ou `develop`; usar `feature/devin-20260915-github-board-unified`.
- **Workflows:** não modificar `.github/workflows/**`.
- **Secrets:** `GITHUB_TOKEN` somente via `.env`/ambiente; nunca logar ou commitar.
- **Specs:** esta SPEC documenta a mudança de contrato (`GitHubBoardColumn`, `IssueDto`, `IGitHubService`) — manter `.specs/` atualizado se o desenho mudar.
- **Tests:** cobertura das novas regras de agrupamento (RF-006) obrigatória.
- **Architecture:** sem regra de negócio em componentes — agrupamento/filtro em helper puro testável.

## 9. Definition of Done

- [ ] Todos os requisitos (seção 4) implementados.
- [ ] Critérios de aceitação (seção 6) cobertos por testes ou validação manual evidenciada.
- [ ] Edge cases tratados.
- [ ] `dotnet build -c Release` e `dotnet test` verdes localmente.
- [ ] Guardrails da seção 8 respeitados.
- [ ] Smoke em Docker: criar issue no board → aparece na coluna; seletor aceita repo digitado; `/projects` íntegro.

## Open Questions / Pending Ambiguity

- Resolvido: colunas = `backlog, todo, in_progress, in_review, in_pullrequest, blocked, done, canceled, archived`; fechadas ≤3 meses → done/canceled/archived; seletor = texto com autocomplete; `/projects` recebe o board local; prioridades via `priority:*`; labels canônicas kebab-case; drag + modal de agente mantidos; `/github-board` removido.
- Assumido (baixo risco): label legada `review` mapeia para `in_review`; issue aberta com label `done`/`canceled` aparece na coluna correspondente; `archived` não recebe drop nem é opção no NewTaskDialog.
