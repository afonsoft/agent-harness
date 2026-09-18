# SPEC-20260918-projects-removal

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `projects-removal` |
| Type | `Refactor` (remoção completa) |
| Stack | `.NET 10 / Blazor / EF Core / taskctl / MCP` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-projects-removal` |
| Ticket | — |
| Status | `Draft` |

## 1. User Story

**As a** mantenedor do Harness
**I want** remover a tela Projects e todo o subsistema de Project/Task local
**So that** o produto fique centralizado no GitHub como fonte única da verdade, sem código morto competindo com o board de issues.

**Problem context:**
A página `/projects` (`ProjectsBoard.razor`) gerencia entidades locais `Project`/`Task` — relictos do clone inicial. O usuário aprovou **remoção completa** (não só a UI). O blast radius mapeado: página + NavMenu + endpoints `/api/local/projects*` + `/api/local/tasks*` + entidades `Project`, `Task` (+ dependentes: `EfCoreProjectRepository`, EF configurations, `AiChatThread.OriginProjectId`, `WorkflowWorkspace.ProjectId`, `WorkflowNode`/`WorkflowSequence` se acoplados) + comandos taskctl `project:*`/`task:*` (locais) + MCP tools de task local + client methods + testes. O board Kanban (GitHub) **não** depende dessas entidades — é seguro remover desde que todas as referências sejam tratadas.

**Dependência:** SPEC-20260918-gantt-github-timeline reescreve o Gantt para GitHub — este SPEC pode rodar em paralelo pois o Gantt será substituído de qualquer forma; se este merge primeiro, o Gantt quebra até o outro entrar (executar o Gantt ANTES ou junto).

## 2. Scope

**In scope:**
- Remover `ProjectsBoard.razor` + item `projects` do `NavMenu.razor` + rota `/projects` (redirect para `/` caso alguém acesse).
- Remover endpoints locais de project/task em `Program.cs` (listar/criar/mover task, map de projeto, etc. — identificar todos os `local/projects`/`local/tasks`/`projectId` usages).
- Remover entidades `Project`, `Task` (+ value objects exclusivos: `ProjectId`, `TaskId`, `TaskStatus`, `TaskPriority` se só usados por elas), `EfCoreProjectRepository` (+ interface se exclusiva), EF configurations + **migration drop das tabelas** `Projects`/`Tasks`.
- `AiChatThread.OriginProjectId` → remover a propriedade + coluna (migration) e o campo `OriginProjectId` dos DTOs/requests; `AiChatService` para de validar `Project`.
- `WorkflowWorkspace.ProjectId` → tornar string livre/legacy ou associar a repo (ver uso real — se só exibido, manter coluna como string sem FK); `WorkflowNode`/`WorkflowSequence` entram na remoção se existirem apenas para o grafo local de projeto.
- taskctl: remover comandos `project:*` e `task:*` locais (manter `ghissue:*` e demais comandos GitHub/MCP).
- MCP `TaskboardTools`: remover tools de project/task locais (manter board/history/comments/agents).
- Client: remover `GetProjectsAsync`, `GetTasksAsync`, DTOs `ProjectDto`/`TaskDto`/`WorkflowWorkspaceDto` se órfãos.
- Skill `manage-taskboard`: atualizar doc removendo comandos de task local e apontando GitHub como fonte.
- docs en/pt-br: remover seções de Projects/local tasks.

**Out of scope:**
- Tabela `IssueHistoryEvent`, `AgentRun`, `ConfigurationOverrides` — ficam.
- `ghissue:*` (taskctl) e tools MCP do board GitHub — ficam.
- `WorkflowWorkspace` — sobrevive (SPEC workflow o usa); só perde a FK para Project.

## 3. Technical Context

**Files to read (mapa de dependências):**
- `src/Taskboard.Blazor/Components/Pages/ProjectsBoard.razor`, `Layout/NavMenu.razor`
- `src/Taskboard.Server/Program.cs` — todos os endpoints `local/project*`/`local/task*`
- `src/Taskboard.Domain/Entities/{Project,Task}.cs`, `Domain/ValueObjects/*`, `Domain/Issues/*`
- `src/Taskboard.EntityFrameworkCore/` — configurations, repositories, migrations
- `src/Taskboard.Cli/` — `project:*`/`task:*` commands
- `src/Taskboard.Mcp/TaskboardTools.cs` — tools locais
- `AiChatService` (OriginProjectId), `Gantt.razor` (substituído pelo SPEC gantt)
- `tests/**` — fixtures que criam Project/Task (integration factory pode semear projetos!)

**Riscos:**
- `FakeGitHubService`/factory de testes pode semear `Project`/`Task` para outros testes (Gantt, AiChat) — ajustar fixtures.
- `CreateAiChatThreadRequest.OriginProjectId` — remover do contrato é breaking; client nunca enviou (UI não usa).

## 4. Functional Requirements

- **RF-001** `/projects` e o item de menu somem; rota `/projects` redireciona para `/`.
- **RF-002** Nenhum endpoint `/api/local/projects*`, `/api/local/tasks*` responde (404); `Program.cs` limpo das rotas.
- **RF-003** Migration drop `Projects`/`Tasks` (+ tabelas filhas exclusivas); `OriginProjectId` sai de `AiChatThreads`.
- **RF-004** `dotnet build` compila sem nenhuma referência a `Project`/`Task`/`taskctl project:*`/`task:*`; grep `Project` residual só em contextos não-entidade (ex.: "projectId" de workflows legacy a remover também).
- **RF-005** taskctl `--help` não lista `project:*`/`task:*`; MCP `tools/list` não expõe tools locais de task.
- **RF-006** Suites verdes com fixtures ajustadas (sem seeds de Project/Task).

## 5. API Contract

Remoção — sem contrato novo. Endpoints removidos documentados no api.md como "removed".

## 6. Acceptance Criteria

- **AC1** Menu não mostra Projects; acessar `/projects` direto redireciona para o board.
- **AC2** `curl /api/local/projects` → 404.
- **AC3** `taskctl --help` e `tools/list` do MCP sem comandos de task local.
- **AC4** Migration aplica limpa em banco de produção (backup antes do deploy); dados de Project/Task descartados (tabelas vazias em produção — confirmado via query).
- **AC5** Board, Gantt(novo), Agents, Settings, Skills seguem funcionando — smoke test pós-deploy.

## 7. Task Plan

- T1: Inventário completo de referências (`grep -rn "Project\b" src/ tests/`) — lista fechada no PR.
- T2: Remover UI + endpoints + client + DTOs.
- T3: Remover entidades/repos/configs + migration drop; ajustar `AiChatThread`/`AiChatService`.
- T4: taskctl + MCP + skill manage-taskboard + docs.
- T5: Fixtures de teste + suites + deploy com backup do banco.

## 8. Organization Guardrails

- **Backup do banco de produção antes do deploy** (drop de tabelas é irreversível).
- Se QUALQUER referência fora do escopo aparecer (ex.: algum consumidor de TaskDto esquecido), parar e re-avaliar — não deixar build quebrado.
- Executar depois ou junto do SPEC gantt-github-timeline (Gantt depende de GetProjectsAsync hoje).

## 9. Definition of Done

- [ ] Nenhuma referência residual a Project/Task no build.
- [ ] Migration aplicada; `/projects` redirect; menu limpo.
- [ ] Suites verdes; docs atualizados; deploy com backup prévio.
