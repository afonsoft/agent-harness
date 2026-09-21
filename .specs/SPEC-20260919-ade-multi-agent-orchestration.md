# SPEC-20260919-ade-multi-agent-orchestration

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ade-multi-agent-orchestration` |
| Type | `Feature` (ADE Control Plane & Orchestration) |
| Stack | `.NET 10 / ASP.NET Core / MediatR / Channel<T> / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260919-ade-multi-agent-orchestration` |
| Ticket | [#167 — E12](https://github.com/afonsoft/agent-harness/issues/167) |
| Status | `Done` |

---

## 1. User Story

**As a** arquiteto de software ou líder técnico
**I want** orquestrar equipes de agentes de IA especializados configurados em pipelines orientados a DAG (Directed Acyclic Graph)
**So that** uma tarefa de engenharia complexa seja dividida sequencial ou paralelamente entre agentes com papéis distintos (ex: Arquiteto/Specifier, Desenvolvedor/Builder, Engenheiro de Testes/QA, e Revisor de Código), transferindo contexto estruturado e artefatos de uma etapa para a outra automaticamente.

### Problem Context

O `agent-harness` hoje trata a orquestração de agentes (`AgentOrchestrationService.cs`) como um enfileiramento linear de um único agente executando um prompt em batch. Não há:
1. **Conceito de papéis de agentes (Role Specialization):** Um único modelo/CLI recebe todo o escopo e frequentemente tenta planejar, codificar, testar e revisar ao mesmo tempo, levando a estouro de contexto, alucinações de API e perda de foco.
2. **Pipelines / DAG de execução:** Não é possível definir um fluxo onde o Agente 1 (ex: Claude 3.7 Sonnet / Thinking) cria a especificação ou plano, o Agente 2 (ex: Codex / OpenCode) escreve o código no worktree, o Agente 3 (ex: Devin / Test-Engineer) cria testes de cobertura, e o Agente 4 (Code Reviewer) valida a conformidade com as regras antes de liberar o PR.
3. **Context Handoff:** Cada agente começa do zero ou requer intervenção manual do usuário para copiar os resultados de um agente para outro.

---

## 2. Scope

### In scope

- Definição do modelo de domínio para **Agent Pipeline & DAG**:
  - `PipelineDefinition`: conjunto de etapas (`PipelineStage`) com dependências (`DependsOn`), agentes alocados, modelos configurados e regras de transição.
  - Papéis suportados nativamente: `Architect` (Spec/Plan), `Builder` (Coding/Implementation), `Tester` (QA/Test authoring), `Reviewer` (Quality & Security Review).
- **Handoff de Contexto estruturado entre etapas:**
  - O output de uma etapa (ex: arquivos gerados, plano em markdown, logs de erro) é automaticamente sintetizado e injetado como input/contexto para a etapa subsequente.
- **Transições com Gates de Decisão (HITL ou Automáticos):**
  - Configuração por etapa: transição automática (`AutoAdvance`), transição condicionada a testes verdes (`RequiresGreenVerification`), ou transição que exige aprovação humana (`RequiresHumanApproval`).
- **Estado e Persistência do Pipeline:**
  - Persistência em SQLite via EF Core do status de cada etapa (`Pending`, `Running`, `WaitingApproval`, `Completed`, `Failed`, `Skipped`).

### Out of scope

- Loops infinitos ou ciclos de recursão descontrolados (o pipeline é estritamente um grafo acíclico - DAG).
- Interface de edição visual drag-and-drop de grafos (nesta fase, os pipelines são definidos via modelos pré-configurados e JSON/C# definitions na UI).

---

## 3. Technical Context

### Where the change happens

- **Domain:** `Taskboard.Domain/Entities/Harness/PipelineExecution.cs`, `PipelineStageExecution.cs`, `AgentRole.cs`.
- **Contracts:** `Taskboard.Application.Contracts/Harness/IPipelineOrchestrator.cs`, DTOs de pipeline.
- **Application:** Handlers de avanço de estágio, resolução de dependências e despacho de agentes.
- **Integrations:** Coordenação com `IWorkspaceIsolationService` para que todos os agentes do mesmo run compartilhem o mesmo worktree isolado.

### Files to read before implementing

- `src/Taskboard.Domain.Shared/ValueObjects/AiChatRunId.cs`
- `src/Taskboard.Application.Contracts/Agents/AgentExecutionRequest.cs`
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
- `.specs/SPEC-015-agent-orchestration.md`

### Files to create or modify

```text
src/Taskboard.Domain.Shared/Harness/AgentRole.cs                       [new]
src/Taskboard.Domain.Shared/Harness/StageStatus.cs                     [new]
src/Taskboard.Domain/Entities/Harness/PipelineDefinition.cs           [new]
src/Taskboard.Domain/Entities/Harness/PipelineStage.cs                [new]
src/Taskboard.Domain/Entities/Harness/PipelineExecution.cs           [new]
src/Taskboard.Domain/Entities/Harness/PipelineStageExecution.cs      [new]
src/Taskboard.Application.Contracts/Harness/IPipelineOrchestrator.cs [new]
src/Taskboard.Application.Contracts/Harness/Dtos/PipelineDtos.cs     [new]
src/Taskboard.Application/Harness/PipelineExecutionAppService.cs      [new]
src/Taskboard.Integrations/Harness/PipelineEngine.cs                  [new]
src/Taskboard.EntityFrameworkCore/Configurations/PipelineConfig.cs    [new]
tests/Taskboard.Tests.Unit/Harness/PipelineEngineTests.cs             [new]
```

---

## 4. Requirements

### RF-001: Definição de Pipeline e Papéis
- **Description:** O sistema deve suportar templates de pipeline pré-configurados:
  1. `Standard-Feature`: Architect (Plano) → Approval Gate → Builder (Código) → Verifier (Build & Test) → Reviewer (Diff Review).
  2. `Quick-Patch`: Builder (Código rápido) → Verifier (Testes).
  3. `Test-Driven`: Architect (Spec) → Tester (Testes RED) → Builder (Código GREEN) → Verifier.
- **Rules:** Cada etapa especifica o `AgentType` (ex: Claude, Codex, OpenCode), o `ModelTier` (Lite, Normal, Ultra) e o papel.

### RF-002: Resolução de Dependências (DAG Engine)
- **Description:** O motor deve avaliar periodicamente as etapas pendentes. Uma etapa só se torna elegível para execução quando todas as etapas em `DependsOn` estiverem concluídas com sucesso.
- **Rules:** Se duas etapas não tiverem dependência mútua (ex: Frontend Builder e Backend Builder), elas podem executar em paralelo se o harness suportar sub-worktrees ou execução coordenada.

### RF-003: Compartilhamento do Workspace de Run
- **Description:** Todas as etapas pertencentes ao mesmo `PipelineExecution` operam no **mesmo Git Worktree** provisionado para o run pelo `IWorkspaceIsolationService`.
- **Rules:** O `Builder` escreve o código no worktree; o `Tester` lê o código no mesmo worktree e escreve novos testes; o `Reviewer` inspeciona o `git diff` do worktree.

### RF-004: Context Handoff entre Etapas
- **Description:** Quando uma etapa conclui, seus artefatos chave (arquivos criados, resumo de decisões, notas de revisão) são consolidados e injetados no prompt da etapa seguinte como `UpstreamContext`.

### RF-005: Interrupção e Recuperação de Falhas
- **Description:** Se uma etapa falhar, o pipeline é pausado no estado `Failed` ou `AwaitingRetry`. O usuário pode editar as instruções da etapa e reexecutá-la a partir do ponto de falha sem reiniciar todo o pipeline.

---

## 5. API Contract

```http
POST /api/harness/pipelines/start
Content-Type: application/json
{
  "templateId": "standard-feature",
  "repositoryFullName": "afonsoft/agent-harness",
  "baseBranch": "main",
  "issueId": "150",
  "initialPrompt": "Implementar funcionalidade de autenticação JWT"
}
→ 201 Created
{
  "pipelineExecutionId": "pipe_987",
  "currentStage": "architect",
  "stages": [
    { "id": "stage-1", "name": "Architect", "status": "Running", "agent": "Claude" },
    { "id": "stage-2", "name": "Approval", "status": "Pending", "isGate": true },
    { "id": "stage-3", "name": "Builder", "status": "Pending", "agent": "OpenCode" },
    { "id": "stage-4", "name": "Verifier", "status": "Pending", "isAutomated": true },
    { "id": "stage-5", "name": "Reviewer", "status": "Pending", "agent": "Devin" }
  ]
}

POST /api/harness/pipelines/{id}/stages/{stageId}/approve
Content-Type: application/json
{
  "comment": "Plano aprovado, prosseguir para implementação"
}
→ 200 OK
{
  "stageId": "stage-2",
  "status": "Completed",
  "nextStageStarted": "stage-3"
}

POST /api/harness/pipelines/{id}/stages/{stageId}/retry
{
  "adjustedPrompt": "Ajuste a rota para usar POST ao invés de PUT"
}
→ 202 Accepted
```

---

## 6. Acceptance Criteria

- [x] **Given** um pipeline `Standard-Feature` iniciado, **when** o agente Architect termina de elaborar o plano, **then** o pipeline entra em estado `WaitingApproval` no gate humano.
- [x] **Given** aprovação humana enviada, **when** o Builder inicia, **then** ele recebe no prompt o plano gerado pelo Architect e o caminho do worktree isolado.
- [x] **Given** um pipeline com 2 etapas em paralelo, **when** a etapa anterior termina, **then** ambas as etapas paralelas são enfileiradas simultaneamente.
- [x] **Given** falha em uma etapa, **when** o usuário aciona `retry` com prompt corrigido, **then** apenas aquela etapa e as posteriores são re-executadas.

**Edge cases:**

| Scenario | Input | Expected behavior |
|---|---|---|
| Ciclo detectado na definição do grafo | Dependência circular A → B → A | Validação do template rejeita a inicialização com erro `400 INVALID_PIPELINE_DAG`. |
| Cancelamento durante execução paralela | Dois agentes rodando | Encerramento seguro de ambos os processos e transição do pipeline para `Cancelled`. |

---

## 7. Task Plan

- [x] **T1 — Domain & Contracts:** Definir entidades `PipelineDefinition`, `PipelineStage`, `PipelineExecution` e interfaces.
- [x] **T2 — DAG Engine Logic:** Implementar algoritmo de topological sort e checagem de ciclo no grafo de execução.
- [x] **T3 — Context Synthesizer:** Implementar gerador de handoff de contexto entre etapas sucessivas.
- [x] **T4 — Pipeline Orchestrator Service:** Integrar com o canal assíncrono de execução e com o `IWorkspaceIsolationService`.
- [x] **T5 — Unit & Integration Tests:** Cobertura de testes unitários validando resolução de dependências, gates de aprovação e cenários de falha/retry.

---

## 8. Organization Guardrails

- Não acoplar o motor de pipeline a nenhuma ferramenta proprietária externa; toda a orquestração deve rodar em C# assíncrono no host .NET 10.
- Preservar logs detalhados de cada etapa com timestamps e saída limpa de tokens/PII.

---

## 9. Definition of Done

- [x] Motor de DAG funcional e testado com xUnit.
- [x] Suporte a templates de pipeline de 3 e 5 etapas.
- [x] Handoff de contexto validado entre etapas sequenciais.
