# SPEC-20260919 — Legacy Workflow Surface: remover WorkflowWorkspace + projeto Taskboard.Workflow vazio

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `legacy-workflow-surface` |
| Type | `Architecture` (dead-code removal) |
| Stack | `.NET 10 / EF Core 10 / ASP.NET Core Minimal APIs` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260919-legacy-workflow-surface` |
| Ticket | `GAP-architecture-legacy-workflow-surface` (gap-analysis-20260919) — Issue #141, Epic #140 |
| Status | `Done` — entregue neste PR |

Origin: gap-analysis-20260919. `SPEC-20260918-workflow-github-actions`
(§Out of scope, linha 36) deferiu explicitamente: "Remoção do
`WorkflowWorkspace` legado (entidade sai quando nada mais usar — avaliar no
projects-removal; a tela só deixa de lê-lo)". `projects-removal` está Done —
a tela `/workflow` virou monitor read-only de GitHub Actions e **nada mais
consome** a superfície legada. O `RemoveLocalProjectsAndTasks` dropou apenas
o FK `FK_WorkflowWorkspaces_Projects_Id`; a tabela segue no model snapshot
(linhas 258-273).

## 1. User Story

**As a** maintainer do Harness,
**I want** a superfície legada de workflow (entidade, tabela, endpoints,
client methods e projeto vazio) removida,
**so that** a API, o modelo EF e a solution reflitam a arquitetura real —
sem endpoints órfãos nem projeto morto no build.

## 2. Scope

### In scope

- Remover entidade `WorkflowWorkspace` (`src/Taskboard.Domain/Entities/WorkflowWorkspace.cs`),
  `IEntityTypeConfiguration` (`EntityFrameworkCore/Configurations/WorkflowWorkspaceConfiguration.cs`),
  `DbSet` no `TaskboardDbContext` e migration `DropTable("WorkflowWorkspaces")`.
- Remover endpoints `GET/PUT /api/device-workspaces` e
  `GET/PUT /api/workflow-capabilities` (`Program.cs` ~662-700) +
  `src/Taskboard.Server/Services/WorkflowCapabilityService.cs` +
  `UpdateWorkflowCapabilitiesRequest` / `WorkflowWorkspaceDto` /
  `WorkflowWorkspaceListResponse` e mapeamentos associados
  (`Application/Mapping/DomainMappingExtensions.cs`,
  `Server/Mapping/DomainMappingExtensions.cs`).
- Remover métodos órfãos em `src/Taskboard.Blazor/Services/TaskboardClient.cs`
  (`/api/device-workspaces`, linha ~34).
- Remover o projeto vazio `src/Taskboard.Workflow/` (só `.csproj` + bin/obj,
  zero `.cs`, zero `ProjectReference` apontando para ele) e a entrada no
  `Taskboard.sln` (linha 34).
- Atualizar `docs/features.md` (`## Automation`) e mirrors pt-br se a seção
  mencionar workflow workspaces (cobre parcialmente o gap de docs; o drift
  maior de stack é SPEC separada).

### Out of scope

- A tela `/workflow` (monitor GitHub Actions) — já entregue, não muda.
- `Taskboard.Workflow` como conceito futuro — se um engine voltar, será um
  SPEC novo.
- Tabelas/entidades ainda em uso (`AgentRun`, `IssueHistoryEvent`, etc.).

## 3. Technical Context

### Evidência de órfão (gap-analysis-20260919)

- Zero `.razor` referencia `WorkflowWorkspace`, `DeviceWorkspace` ou
  `workflow-capabilities` (`grep` em `src/Taskboard.Blazor`).
- `TaskboardClient.cs` tem o método `GET /api/device-workspaces` sem callers.
- `src/Taskboard.Cli` e `src/Taskboard.Mcp` não referenciam a superfície.
- `src/Taskboard.Workflow/` contém apenas `Taskboard.Workflow.csproj` —
  nenhum `.cs` fora de bin/obj; nenhum `.csproj` o referencia.
- `TaskboardDbContextModelSnapshot.cs:258-273` ainda mapeia
  `WorkflowWorkspaces`.

### Ler antes de implementar

- `src/Taskboard.Server/Program.cs` — bloco dos endpoints (~linha 662-700).
- `src/Taskboard.EntityFrameworkCore/Data/TaskboardDbContext.cs`.
- Padrão de migration: `dotnet ef migrations add` no projeto EFCore
  (ver `SPEC-20260918-projects-removal` / migration `RemoveLocalProjectsAndTasks`).

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | `WorkflowWorkspaces` removida do modelo EF via nova migration (`DropTable`), sem tocar em outras tabelas. |
| RF-002 | `GET/PUT /api/device-workspaces` e `GET/PUT /api/workflow-capabilities` retornam 404 (rotas removidas; o catch-all `/api/*` já cobre). |
| RF-003 | `WorkflowCapabilityService`, DTOs e mappings exclusivos removidos; nenhum símbolo `WorkflowWorkspace*` permanece fora de migrations históricas. |
| RF-004 | `Taskboard.Workflow` fora da `Taskboard.sln` e diretório removido; `dotnet build` segue verde. |
| RF-005 | `docs/features.md` `## Automation` reflete o monitor GitHub Actions (remove "Workflow workspaces (JSON board config)"). |

## 5. Acceptance Criteria

- **AC-1** *Given* a solution, *when* `dotnet build Taskboard.sln` roda, *then* compila sem warnings e `Taskboard.Workflow` não consta mais no sln.
- **AC-2** *Given* a migration aplicada num SQLite fresco, *when* `sqlite3 .tables`, *then* `WorkflowWorkspaces` não existe.
- **AC-3** *Given* o servidor em pé, *when* `GET /api/device-workspaces` (autenticado), *then* responde 404.
- **AC-4** *Given* `grep -r "WorkflowWorkspace" src/ --include="*.cs"`, *when* executado, *then* só resta menção em migrations históricas.
- **AC-5** *Given* a suíte, *when* `dotnet test` roda, *then* verde (445 unit + 156 integration) com ajustes mínimos onde testes referenciavam a superfície.

## 6. DoD

- [ ] RF-001..005 implementados.
- [ ] `dotnet build` clean (0 warnings); `dotnet test` verde.
- [ ] Migration gerada e revisada (apenas `DropTable("WorkflowWorkspaces")` + metadados).
- [ ] SPEC → `Status: Done`; PR merged; `taskboard-server` redeployed.
