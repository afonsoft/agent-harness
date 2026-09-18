# SPEC-20260918-workflow-github-actions

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `workflow-github-actions` |
| Type | `Feature` (refactor da tela Workflow) |
| Stack | `.NET 10 / Blazor / Octokit Actions API` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-workflow-github-actions` |
| Ticket | — |
| Status | `Approved` |

## 1. User Story

**As a** usuário do Harness
**I want** que a tela Workflow mostre os GitHub Actions de cada repositório — workflows, runs recentes, status, duração e link direto
**So that** eu monitore a CI/CD de todos os repos do board num único lugar, sem abrir o github.com repo a repo.

**Problem context:**
`/workflow` hoje renderiza `WorkflowWorkspaceDto` — um grafo JSON **local e obsoleto** (nós sequenciais de execuções de agente, legado do clone). A decisão aprovada: **a tela vira um monitor de GitHub Actions** — a verdadeira definição de "workflow" num produto centrado no GitHub. Octokit expõe `client.Actions.Workflows` (`GetAll`, `List`) e `client.Actions.Runs` (`ListWorkflowRunsForRepository`, `Get`), cobrindo nome, path, state, conclusion, branch, SHA, created/updated — dá para renderizar status verde/vermelho, duração e link `HtmlUrl` direto pro run.

## 2. Scope

**In scope:**
- `Workflow.razor` reescrito: seletor de repositório (mesma fonte dos configurados do board) → lista de workflows do repo (nome, path `.github/workflows/...`, state, último run com badge de conclusão) → expandir um workflow mostra runs recentes (branch, SHA curto, actor, created→duration, conclusion colorido, link ↗ pro run no GitHub).
- Endpoints: `GET /api/github/{owner}/{repo}/workflows` (workflows + last run embutido) e `GET /api/github/{owner}/{repo}/workflows/{workflowId}/runs` (últimos 10).
- `IGitHubService`: `GetWorkflowsAsync` (Octokit `Actions.Workflows.GetAll` + `Runs.ListWorkflowRunsForRepository` por workflow para o last-run) — best-effort: repo sem Actions/permissão → lista vazia com mensagem, não erro.
- Badges de conclusão mapeados (`success` verde, `failure` vermelho, `in_progress`/`queued` amarelo pulsante, `cancelled`/`skipped` cinza); duração = updated−created quando completed, senão "running hX".
- Refresh manual + auto-refresh a cada 60s enquanto houver run `in_progress`/`queued`.

**Out of scope:**
- Trigger de workflow (`workflow_dispatch`), rerun/cancel — read-only nesta iteração.
- Job-level detail / logs (o link ↗ resolve).
- Remoção do `WorkflowWorkspace` legado (entidade sai quando nada mais usar — avaliar no projects-removal; a tela só deixa de lê-lo).

## 3. Technical Context

**Files to read:**
- `src/Taskboard.Blazor/Components/Pages/Workflow.razor` (atual — grafo JSON local)
- `GitHubService` (padrão de paginação + FakeGitHubService + HttpGitHubService)
- Octokit `Actions.Workflows`/`Actions.Runs` API surface (verificar versão do pacote no Directory.Packages.props)
- `KanbanBoard.razor` — fonte de repos configurados

**Files to create/modify:**
- `Application.Contracts/GitHub/` — `WorkflowDto` (`{id,name,path,state,htmlUrl,lastRun:{id,name,status,conclusion,headBranch,headSha,actor,createdAt,updatedAt,htmlUrl}}`), `WorkflowRunDto`.
- `IGitHubService` + impl + client + endpoints + `Workflow.razor` rewrite + tests.

## 4. Functional Requirements

- **RF-001** `GET /api/github/{owner}/{repo}/workflows` → `{ workflows: [WorkflowDto] }` ordenados por último run desc (sem runs por último); `lastRun` = run mais recente do workflow.
- **RF-002** `GET /api/github/{owner}/{repo}/workflows/{id}/runs` → últimos 10 `WorkflowRunDto`.
- **RF-003** Tela: seletor de repo → cards de workflow (nome, path curto, state, badge do último run + quando) → expand abre tabela de runs (status/conclusion/branch/SHA/actor/duração/link).
- **RF-004** Mapeamento de status: `success`✓verde `failure`✗vermelho `cancelled`⊘cinza `in_progress`/`queued`⟳amarelo-animado `skipped`/outros cinza; "No runs yet" quando lastRun nulo.
- **RF-005** Auto-refresh 60s apenas com runs vivos; refresh manual sempre. Erro de permissão/repo sem Actions → mensagem amigável (não exception na tela).
- **RF-006** `FakeGitHubService` stubs + unit tests do mapeador de conclusão + integration dos endpoints.

## 5. API Contract

```
GET /api/github/{owner}/{repo}/workflows
→ 200 { workflows: [{ id, name, path, state, htmlUrl,
       lastRun: { id, status, conclusion, headBranch, headSha,
                  actor, createdAt, updatedAt, htmlUrl } | null }] }
GET /api/github/{owner}/{repo}/workflows/{workflowId}/runs
→ 200 { runs: [WorkflowRunDto] } | 404
```

## 6. Acceptance Criteria

- **AC1** Repo com Actions → lista de workflows com badge do último run; clicar expande os 10 últimos runs com link pro GitHub.
- **AC2** Repo sem workflows → EmptyState claro, sem erro.
- **AC3** Run in_progress → badge animado e auto-refresh ativo.
- **AC4** `FakeGitHubService` retorna fixtures → endpoints e UI testáveis sem token real.

## 7. Task Plan

- T1: DTOs + `IGitHubService.GetWorkflowsAsync`/`GetWorkflowRunsAsync` (Octokit) + Fake + client.
- T2: Endpoints + integration tests.
- T3: `Workflow.razor` rewrite (selector, cards, expand, badges, auto-refresh).
- T4: docs + suites + deploy.

## 8. Organization Guardrails

- Branch dedicada; read-only (nenhum write na Actions API).
- Máximo 1 chamada Octokit por workflow (last-run) — repos com muitos workflows: cap 20 ou parallel limitado.
- CSS novo mínimo; reusar badges/padrões existentes.

## 9. Definition of Done

- [ ] Tela mostra Actions reais dos repos configurados.
- [ ] Suites verdes; docs en/pt-br; SPEC → Done; deploy verificado.
