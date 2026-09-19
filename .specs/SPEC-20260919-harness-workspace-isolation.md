# SPEC-20260919-harness-workspace-isolation

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-workspace-isolation` |
| Type | `Feature` (Harness Infrastructure) |
| Stack | `.NET 10 / Git CLI / LibGit2Sharp or System.Diagnostics.Process / C# 14` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-harness-workspace-isolation` |
| Ticket | [#161 — E6](https://github.com/afonsoft/taskboard-ai/issues/161) |
| Status | `Done` |

---

## 1. User Story

**As an** orquestrador de agentes de IA (Harness Engine)
**I want** criar e gerenciar um Git Worktree isolado para cada execução de agente (`AgentRun`)
**So that** múltiplos agentes possam codificar em paralelo no mesmo repositório sem conflitos de working tree, dirty files ou perda de progresso, garantindo ambiente limpo, branch dedicada e sandbox seguro.

### Problem Context

Atualmente, `AgentOrchestrationService` e `KnownCliAgentAdapter` executam agentes CLI apontando o diretório de trabalho diretamente para o `RepoPath` raiz (ex: `/home/ubuntu/repos/taskboard-ai`).
Isso gera três grandes riscos críticos:
1. **Concorrência impossível:** Se dois agentes tentam trabalhar simultaneamente no mesmo repositório (ex: um corrigindo um bug em frontend e outro adicionando um endpoint em backend), eles sobrescrevem arquivos um do outro no mesmo diretório.
2. **Poluição do workspace humano:** Se o desenvolvedor humano estiver com o repositório aberto na máquina ou no VS Code, a execução do agente altera arquivos diretamente sob seus olhos, tornando difícil inspecionar diffs isolados antes de decidir aceitar ou descartar o trabalho.
3. **Falta de sandbox de ambiente:** Não há um ciclo de vida automatizado que crie branch isolada (`feature/agent-{runId}`), prepare dependências locais, capture diffs parciais e realize cleanup atômico ao final.

---

## 2. Scope

### In scope

- Criação e teardown automatizado de **Git Worktrees** por `AgentRun` em caminho padronizado: `~/.taskboard/worktrees/{runId}/`.
- Criação automática de branch dedicada associada ao run: `feature/agent-{runId}-{task-slug}` a partir da branch base informada (default `main`).
- Limpeza segura do worktree pós-execução (`git worktree remove --force`), com política de retenção configurável para inspeção humana em caso de falha.
- Extração de Git Diffs estruturados (`git diff`, `git status --porcelain`) a qualquer momento da execução.
- Confinamento e sanitização de variáveis de ambiente (`WithoutTaskboardEnv`, remoção de tokens sensíveis).
- Contrato `IWorkspaceIsolationService` injetável nos serviços de orquestração.

### Out of scope

- Máquinas virtuais ou containers Docker para cada agente nesta primeira fase (o foco é isolamento de workspace via Git Worktrees nativos no SO hospedeiro).
- Resolução de conflitos de merge automática para a branch base (o merge/PR é disparado pelo usuário ou por uma etapa específica de revisão).

---

## 3. Technical Context

### Where the change happens

- **Contracts:** `Taskboard.Application.Contracts/Harness/IWorkspaceIsolationService.cs`, `WorktreeSessionDto.cs`.
- **Integrations:** `Taskboard.Integrations/Harness/GitWorktreeManager.cs`, `GitCommandRunner.cs`.
- **Domain:** `Taskboard.Domain/Entities/Harness/WorktreeSession.cs`.
- **EF Core:** Entidade `WorktreeSession` persistida com status e caminhos.

### Files to read before implementing

- `src/Taskboard.Integrations/Workspace/WorkspaceService.cs`
- `src/Taskboard.Integrations/Execution/WithoutTaskboardEnv.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Harness/WorktreeStatus.cs                   [new]
src/Taskboard.Domain/Entities/Harness/WorktreeSession.cs               [new]
src/Taskboard.Application.Contracts/Harness/IWorkspaceIsolationService.cs [new]
src/Taskboard.Application.Contracts/Harness/Dtos/WorktreeSessionDto.cs [new]
src/Taskboard.Application.Contracts/Harness/Dtos/WorkspaceDiffDto.cs   [new]
src/Taskboard.Integrations/Harness/GitWorktreeManager.cs               [new]
src/Taskboard.Integrations/Harness/IGitCommandRunner.cs                [new]
src/Taskboard.Integrations/Harness/GitCommandRunner.cs                 [new]
src/Taskboard.EntityFrameworkCore/Configurations/WorktreeSessionConfiguration.cs [new]
tests/Taskboard.Tests.Unit/Harness/GitWorktreeManagerTests.cs          [new]
```

---

## 4. Requirements

### RF-001: Provisão de Worktree
- **Description:** Dado um `runId`, um `repoPath` e uma `baseBranch`, o serviço deve criar um novo Git Worktree em `~/.taskboard/worktrees/{runId}` com uma nova branch `feature/agent-{runId}-{slug}`.
- **Rules:** O comando `git worktree add -b <new-branch> <target-path> <base-branch>` deve ser executado no contexto do repositório base. Se a pasta de destino já existir, deve ser limpa previamente ou lançar exceção específica.
- **Input → Output:** `CreateWorktreeAsync(runId, repoPath, baseBranch, slug)` → `WorktreeSession(Path, Branch, Status.Active)`.

### RF-002: Inspeção de Diffs e Status
- **Description:** A qualquer momento, deve ser possível consultar as alterações pendentes ou commitadas no worktree em relação à branch base.
- **Rules:** O serviço deve rodar `git status --porcelain` e `git diff <base-branch>...HEAD` no diretório do worktree, retornando lista de arquivos modificados, novas inserções, deleções e o patch em texto.
- **Input → Output:** `GetDiffAsync(runId)` → `WorkspaceDiffDto(FilesCount, Insertions, Deletions, RawDiff)`.

### RF-003: Commit e Push de Alterações
- **Description:** Permitir que o agente ou o harness comite as mudanças do worktree com mensagem estruturada (ex: `feat(agent): [runId] automated task execution`).
- **Input → Output:** `CommitAsync(runId, message, author)` → `CommitSha`.

### RF-004: Teardown e Cleanup
- **Description:** Ao término do run (com sucesso ou descarte aprovado), o worktree deve ser removido com `git worktree remove --force <path>` e `git worktree prune`.
- **Rules:** Se o run falhar e a flag de diagnóstico `RetainOnFailure` estiver habilitada, o diretório é mantido e marcado como `WorktreeStatus.RetainedForInspection` para análise humana.

---

## 5. API Contract

```http
POST /api/harness/worktrees
Content-Type: application/json
{
  "runId": "run_01j7abcde",
  "repositoryPath": "/home/ubuntu/repos/taskboard-ai",
  "baseBranch": "main",
  "taskSlug": "fix-login-error"
}
→ 201 Created
{
  "worktreeId": "wt_123",
  "path": "/home/ubuntu/.taskboard/worktrees/run_01j7abcde",
  "branch": "feature/agent-run_01j7abcde-fix-login-error",
  "status": "Active"
}

GET /api/harness/worktrees/{runId}/diff
→ 200 OK
{
  "filesChanged": 2,
  "insertions": 15,
  "deletions": 3,
  "files": [
    { "path": "src/Taskboard.Server/Program.cs", "status": "Modified" }
  ],
  "patch": "diff --git a/src/Taskboard.Server/Program.cs..."
}

DELETE /api/harness/worktrees/{runId}?force=true
→ 204 No Content
```

---

## 6. Acceptance Criteria

- [x] **Given** um repositório git válido, **when** `CreateWorktreeAsync` é invocado, **then** um novo diretório de worktree é criado e `git branch` reflete a nova branch dedicada.
- [x] **Given** um worktree ativo com arquivos alterados, **when** `GetDiffAsync` é chamado, **then** retorna os arquivos e o patch exato gerado pelo agente.
- [x] **Given** múltiplos runs simultâneos para o mesmo repositório, **when** cada um altera arquivos distintos ou os mesmos arquivos, **then** nenhum interfere no outro ou no repositório base.
- [x] **Given** comando de remoção de worktree, **when** executado, **then** o diretório é deletado e o comando `git worktree list` não lista mais a entrada.

**Edge cases:**

| Scenario | Input | Expected behavior |
|---|---|---|
| Repositório com dirty working tree na base | Base com arquivos não commitados | Worktree é criado normalmente a partir do commit HEAD da base sem carregar o dirty state. |
| Processo preso segurando arquivo no worktree | `git worktree remove` falha por lock | O runner tenta identificar e matar o processo órfão antes do prune forçado. |
| Branch de destino já existente | Mesma branch por retry de runId | Sufixo incremental adicionado ou reutilização idempotente controlada. |

---

## 7. Task Plan

- [x] **T1 — Contracts & Value Objects:** Criar `IWorkspaceIsolationService`, `WorktreeStatus` enum, `WorktreeSessionDto` e modelos.
- [x] **T2 — Git Command Runner:** Implementar execução robusta de comandos git com tratamento de timeouts, exit codes e stdio assíncrono.
- [x] **T3 — GitWorktreeManager:** Implementar criação, leitura de diff, commit e cleanup do worktree com testes unitários usando mocks e git real em diretório temporário.
- [x] **T4 — Integração com AgentOrchestrator:** Plugar o worktree no `LocalCliAgentAcpClient` para que os agentes rodem dentro do path do worktree.
- [x] **T5 — Verificação & Testes:** Suíte de testes `GitWorktreeManagerTests` passando com 100% de cobertura no módulo.

---

## 8. Organization Guardrails

- Nunca executar comandos git com interpolação desprotegida de strings (prevenção contra command injection nos nomes de branch/slug).
- Todos os caminhos de worktree devem estar contidos sob o diretório aprovado `~/.taskboard/worktrees/`.
- Sanitizar ambiente de execução com `WithoutTaskboardEnv`.

---

## 9. Definition of Done

- [x] Métodos `CreateWorktreeAsync`, `GetDiffAsync`, `CommitAsync`, `RemoveWorktreeAsync` implementados e testados.
- [x] Testes passando em Linux (`git worktree`).
- [x] Tratamento de erros limpo e sem vazamento de paths sensíveis.
