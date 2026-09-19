# SPEC-20260919-ade-harness-platform

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ade-harness-platform` |
| Type | `Feature` (Architecture & Core Platform) |
| Stack | `.NET 10 / ASP.NET Core / ABP N-Layer / Blazor WASM / SignalR / Git / MCP` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260919-ade-harness-platform` |
| Ticket | `[A DEFINIR]` |
| Status | `Draft` |

---

## 1. User Story

**As a** engenheiro de software e líder técnico (Human Architect / Director)
**I want** uma plataforma unificada que atue como **ADE (Agentic Development Environment)** e **Agent Harness**
**So that** eu possa orquestrar times de agentes de IA autônomos em pipelines estruturados (especificação, codificação, testes, revisão), mantendo controle estrito via isolamento em Git worktrees, gates de aprovação humana (HITL), loop fechado de auto-correção/verificação e observabilidade completa de tokens e diffs.

### Problem Context

O `taskboard-ai` (sob branding **Harness**) consolidou um Kanban integrado ao GitHub, suporte a 13 CLIs de agentes, terminal PTY, VS Code Web e servidor MCP. Contudo:
1. **Limitação de unidade de trabalho:** A execução atual ainda é predominantemente orientada a disparos isolados (*fire-and-forget* de 1 CLI por issue) com terminal raw de logs (`TaskLogTab`), enquanto um verdadeiro **ADE** tem como unidade primária o **Ciclo de Vida do Run do Agente (Agent Run)** e a coordenação multi-agente (equipes de agentes especializados).
2. **Ausência de isolamento do workspace (Sandbox):** Agentes executam comandos diretamente no diretório do repositório (`RepoPath`), criando risco de concorrência, dirty working tree e perda de código se múltiplos agentes trabalharem em paralelo.
3. **Falta de loop determinístico de auto-verificação:** Quando o agente gera código com erros de compilação ou quebras de testes, não há um loop estruturado no harness que capture as falhas de build/test e re-injete no contexto do agente para correção incremental antes de reportar conclusão.
4. **Falta de cockpit estruturado de decisão (HITL):** A experiência do usuário ainda se assemelha a um leitor de logs, sem visualizador de Git Diffs integrado, cards de tool calls estruturados e aprovações interativas granulares de segurança.

Esta SPEC define a **Arquitetura Mestre e Capability Map** da evolução para ADE + Harness.

---

## 2. Scope

### In scope

- **Definição dos 4 módulos pilares da evolução ADE + Harness:**
  1. `harness-workspace-isolation`: Gestão do ciclo de vida de Git Worktrees dedicados por Agent Run, sandbox de variáveis de ambiente e confinamento de filesystem.
  2. `ade-multi-agent-orchestration`: Orquestrador de pipelines e DAGs multi-agente (ex: Specifier/Architect → Builder/Coder → Test Engineer → Code Reviewer) com handoff de contexto.
  3. `harness-verification-loop`: Motor de auto-verificação pós-execução (build com `TreatWarningsAsErrors`, `dotnet test`, ratchet do `COVERAGE_THRESHOLD` e re-injeção de erros em loop fechado).
  4. `ade-cockpit-hitl`: Cockpit Blazor WASM interativo com stream de eventos estruturados (thought, tool_call, diff, patch), visualizador side-by-side de Git Diff, controles de steer/pause/resume e gate de permissões.
- **Integração com Living Specifications (.specs):** Vínculo bidirecional entre SPECs em `.specs/`, issues do GitHub, branches em worktrees e execuções de agentes.
- **Capability Map e matriz de dependências** formalizados para orientar a implementação incremental em TDD.

### Out of scope

- Reescrever ou quebrar a arquitetura ABP N-Layer existente (`Domain` → `Application.Contracts` → `Application` → `EntityFrameworkCore` → `Server`).
- Quebrar compatibilidade com endpoints existentes de Kanban e `taskctl`.
- Provedores proprietários de sandbox em nuvem (foco em isolamento local-first via Git worktrees e isolamento de processo no host/Linux/macOS/Windows).

---

## 3. Technical Context

### Where the change happens

- **Domain Layer:** Novos agregados e entidades para `AgentRun`, `AgentPipeline`, `WorktreeSession`, `VerificationReport`, `RunEvent`.
- **Application Layer:** Handlers MediatR para orquestração de DAGs, ciclo de vida de worktrees e triggers de verificação.
- **Integrations Layer:** `GitWorktreeManager`, `ProcessSandboxConfinement`, `AutomatedVerificationEngine`, `AcpMultiAgentCoordinator`.
- **Server Layer:** SignalR Hub de eventos estruturados (`/harness-cockpit-hub`), endpoints REST para gerenciamento de runs, pipelines e diffs.
- **Blazor WASM UI:** Cockpit view integrada (`/runs`, `/runs/{id}`), componente `GitDiffViewer`, `ToolCallInspector`, modal de aprovação HITL.

### Files to read before implementing

- `CLAUDE.md`, `AGENTS.md`, `.claude/rules/global-rules.md`
- `.specs/CAPABILITY-MAP.md`
- `.specs/SPEC-015-agent-orchestration.md`
- `.specs/SPEC-20260919-web-cli-agent.md`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `src/Taskboard.Integrations/Workspace/WorkspaceService.cs`

### Files to create or modify

```text
.specs/CAPABILITY-MAP.md                                                [mod: adicionar fase ADE & Harness]
.specs/SPEC-20260919-ade-harness-platform.md                            [new: esta spec mestre]
.specs/SPEC-20260919-harness-workspace-isolation.md                     [new: spec isolamento worktree]
.specs/SPEC-20260919-ade-multi-agent-orchestration.md                   [new: spec pipeline DAG]
.specs/SPEC-20260919-ade-cockpit-hitl.md                                [new: spec cockpit Blazor]
.specs/SPEC-20260919-harness-verification-loop.md                       [new: spec feedback loop]
src/Taskboard.Domain.Shared/Harness/*                                   [new: VOs e enums de Harness/ADE]
src/Taskboard.Domain/Entities/Harness/*                                 [new: Run, Worktree, Pipeline]
src/Taskboard.Application.Contracts/Harness/*                           [new: DTOs e Interfaces]
src/Taskboard.Application/Harness/*                                     [new: Commands/Queries]
src/Taskboard.EntityFrameworkCore/Configurations/Harness*               [new: Mapeamentos EF]
src/Taskboard.Integrations/Harness/*                                    [new: Git, Sandbox, Verification]
src/Taskboard.Server/Hubs/HarnessCockpitHub.cs                          [new: Hub SignalR]
src/Taskboard.Blazor/Pages/Harness/                                     [new: Cockpit, Runs, Diffs]
```

---

## 4. Requirements

### RF-001: Capability Map & Decomposição Arquitetural
- **Description:** O sistema deve estruturar a evolução para ADE e Harness em módulos independentes e testáveis, com ordem estrita de dependências *forward-only*.
- **Rules:** Nenhum módulo superior pode ser construído antes do módulo inferior estar validado com testes verdes.
- **Input → Output:** Arquitetura descrita em `.specs/CAPABILITY-MAP.md` atualizada com os novos módulos.

### RF-002: Ciclo de Vida do Agent Run (Unidade Central do ADE)
- **Description:** A unidade fundamental de execução da plataforma deve ser o `AgentRun`, desacoplado do editor de texto. O `AgentRun` deve conter: ID único, Issue vinculada (opcional), Spec vinculada, Worktree isolado, lista de agentes participantes, DAG de etapas, eventos estruturados, custo/tokens acumulados e status (`Queued`, `Running`, `WaitingApproval`, `Verifying`, `Succeeded`, `Failed`, `Cancelled`).
- **Input → Output:** Criação de `AgentRun` gera um workspace isolado e inicia o pipeline conforme o plano aprovado.

### RF-003: Separação Formal entre ADE e Harness
- **Description:** A camada **Harness** é responsável pela infraestrutura do agente (ambiente de execução, sandbox, injeção de contexto/prompts, MCPs/skills, execução de tools, verificação de saída). A camada **ADE** é responsável pelo plano de controle (painel humano, gerenciamento de múltiplos runs concorrentes, DAG de delegação entre agentes, inspeção de diffs e gates de decisão).
- **Rules:** O Harness não conhece detalhes de UI; expõe contratos via `Application.Contracts` e eventos via `SignalR`/SSE. O ADE consome esses contratos para fornecer o cockpit de direção.

### RF-004: Living Spec Engine
- **Description:** Toda execução orientada a funcionalidades ou correções complexas deve ser associada a uma especificação em `.specs/SPEC-*.md`. O agente deve reportar conformidade com os critérios de aceitação BDD definidos na SPEC.

---

## 5. API Contract

O plano de controle do ADE expõe contratos REST e SignalR para gerenciar runs e inspecionar o harness:

```http
POST /api/harness/runs
Content-Type: application/json
{
  "pipelineId": "feature-standard",
  "issueId": "145",
  "specPath": ".specs/SPEC-20260919-coverage-gate-ratchet.md",
  "repositoryFullName": "afonsoft/taskboard-ai",
  "baseBranch": "main",
  "prompt": "Implementar o ratchet do gate de cobertura conforme a spec"
}
→ 201 Created
{
  "runId": "run_01j7abcde...",
  "status": "Queued",
  "worktreePath": "~/.taskboard/worktrees/run_01j7abcde",
  "branchName": "feature/agent-run_01j7abcde-coverage-gate-ratchet"
}

GET /api/harness/runs/{id}
→ 200 OK
{
  "runId": "run_01j7abcde...",
  "status": "Running",
  "currentStep": "builder",
  "tokensUsed": 14250,
  "estimatedCostUsd": 0.042,
  "activeAgent": "Claude",
  "worktreeBranch": "feature/agent-run_01j7abcde-coverage-gate-ratchet"
}

GET /api/harness/runs/{id}/diff
→ 200 OK
{
  "filesChanged": 3,
  "insertions": 45,
  "deletions": 12,
  "diffContent": "diff --git a/src/... b/src/..."
}

POST /api/harness/runs/{id}/steer
{
  "instruction": "Lembre-se de adicionar testes para o caso de erro 404"
}
→ 202 Accepted
```

---

## 6. Acceptance Criteria

- [ ] **Given** uma requisição de run criada via API ou UI, **when** o run é inicializado, **then** o harness provisiona um Git worktree exclusivo sem afetar o repositório principal.
- [ ] **Given** múltiplos runs em execução concorrente para o mesmo repositório, **when** ambos alteram arquivos, **then** eles operam em worktrees e branches distintas sem conflito local de workspace.
- [ ] **Given** um pipeline configurado (Architect → Builder → Verifier → Reviewer), **when** a etapa Builder conclui com sucesso, **then** a etapa Verifier executa compilação e suíte de testes automaticamente.
- [ ] **Given** falha de compilação ou testes na etapa Verifier, **when** o loop de auto-correção estiver dentro do limite de iterações (max 2), **then** os erros são retroalimentados ao Builder para correção antes de notificar o usuário.
- [ ] **Given** o término do pipeline com sucesso, **when** o usuário inspeciona o run no Cockpit, **then** é apresentado o diff completo, logs estruturados e opção para abrir Pull Request ou realizar merge.

**Edge cases:**

| Scenario | Input | Expected behavior |
|---|---|---|
| Falha catastrófica no Git worktree | Diretório bloqueado por processo | O harness isola o erro, tenta liberação segura e marca o run como Failed sem corromper o repositório base. |
| Estouro de iterações de auto-correção | Teste continua falhando após 2 tentativas | O harness encerra o loop, emite evento de escalonamento humano no Cockpit e mantém o worktree para inspeção. |
| Cancelamento abrupto pelo usuário | Post no endpoint `/cancel` | Encerramento em cascata da árvore de processos (`Kill(entireProcessTree: true)`), status Cancelled e preservação opcional do worktree. |

---

## 7. Task Plan

- [ ] **T1 — Capability Map Formalization:** Atualizar `.specs/CAPABILITY-MAP.md` adicionando os 4 módulos da evolução ADE + Harness.
- [ ] **T2 — Detailed Sub-Specs Creation:** Criar as 4 SPECs detalhadas (`harness-workspace-isolation`, `ade-multi-agent-orchestration`, `ade-cockpit-hitl`, `harness-verification-loop`).
- [ ] **T3 — Domain & Contracts Modeling:** Modelar entidades base (`AgentRun`, `WorktreeSession`, `VerificationResult`, `PipelineStage`) e DTOs correspondentes.
- [ ] **T4 — Integration Architecture & Verification:** Implementar os adapters de Worktree, Verification Runner e SignalR Hub.
- [ ] **T5 — Cockpit UI & E2E Validation:** Desenvolver os componentes Blazor do Cockpit (Runs, Diff Viewer, Approval Prompts) e validar a suíte completa de testes.

---

## 8. Organization Guardrails

- **Branches:** Nunca commitar em `main`, `master` ou `develop`.
- **Workflows:** Não editar `.github/workflows/**` sem aprovação humana.
- **Arquitetura:** Preservar a estratificação ABP (Domain, Application.Contracts, Application, EntityFrameworkCore, Server, Integrations, Blazor).
- **Segurança:** Nunca gravar credenciais ou segredos em disco nos worktrees dos agentes; sanitizar variáveis de ambiente com `WithoutTaskboardEnv`.
- **Qualidade:** Build com `TreatWarningsAsErrors` e cobertura ≥ 66.26% (ratchet para 80%).

---

## 9. Definition of Done

- [ ] Arquitetura e Capability Map aprovados pelo usuário.
- [ ] Sub-SPECs criadas e consistentes com o template SDD.
- [ ] Entidades de domínio e contratos compilando sem warnings.
- [ ] Testes unitários e de integração cobrindo o fluxo de lifecycle do Run e Worktrees.
- [ ] Cockpit funcional em Blazor WASM.
