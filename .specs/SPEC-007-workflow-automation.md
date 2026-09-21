# SPEC-007: Workflow Automation

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Workflow Automation |
| Product / System | agent-harness |
| Module / Bounded Context | Workflow |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-workflow-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O Taskboard possui workflow workspaces (JSON de board visual), engine de grafo de workflow e auto-claim de tarefas `todo` por agentes Codex.

### Objective

Especificar módulo de workflow e automação em .NET 10.

### Expected outcome

- `WorkflowWorkspace` persistido por projeto.
- Interfaces para control-flow engine (stubs).
- Endpoints `/api/device-workspaces`, `/api/workflow-capabilities`.
- Entidades `WorkflowNode` e `WorkflowSequence` definidas no domínio.

### Out of scope

- UI visual do workflow (ver `SPEC-008`).
- Engine de automação completo (apenas interfaces/stubs).

---

## 2. Agent Role

> Senior backend engineer com experiência em grafos, automação e agendamento.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não executar ações destrutivas sem confirmação.
- Não logar credenciais de Codex.

---

## 4. Product Context

### Functional context

Workflow permite customizar colunas, estados e automações por projeto. Auto-claim permite que agentes assumam tarefas `todo` automaticamente.

### Technical context

- Tabelas `workflow_workspaces`.
- `workflow-control-flow.mjs` / `workflow-sequence.mjs` (legado).
- `taskboard-automation*.mjs` (legado).

### Relevant stack

- .NET 10
- JSON schema validation
- Cron/scheduler (Hangfire ou HostedService) - stub

---

## 5. Task Definition

### Main task

Mapear workflow e automação.

### Subtasks

- Workflow workspace (JSON config por projeto).
- Workflow nodes e sequences (entidades de domínio).
- Endpoints de device workspaces e workflow capabilities.
- Interfaces para automation engine (stubs).

### Do not do

- Não implementar engine completo (apenas interfaces).

---

## 6. Functional Requirements

### FR-001: Workflow Workspace

**Description:**  
JSON config de board visual por projeto.

**Endpoints:**

```http
GET    /api/workflow-workspaces/{projectId}
PUT    /api/workflow-workspaces/{projectId}
```

**Regras:**

- Configuração JSON de colunas, estados, automações.
- Persistido por projeto em `workflow_workspaces`.

### FR-002: Device Workspaces

**Description:**  
Workspaces de dispositivo para workflow visual.

**Endpoints:**

```http
GET    /api/device-workspaces
PUT    /api/device-workspaces
```

### FR-003: Workflow Capabilities

**Description:**  
Capabilidades de workflow disponíveis.

**Endpoints:**

```http
GET    /api/workflow-capabilities
PUT    /api/workflow-capabilities
```

### FR-004: Workflow Nodes

**Description:**  
Nós do grafo de workflow.

**Entidade:** `WorkflowNode` (domínio) com campos:

- `id`: identificador
- `project_id`: projeto
- `type`: tipo do nó
- `config`: JSON config

### FR-005: Workflow Sequences

**Description:**  
Sequências de execução.

**Entidade:** `WorkflowSequence` (domínio) com campos:

- `id`: identificador
- `project_id`: projeto
- `nodes`: array de node IDs
- `config`: JSON config

---

## 7. Business Rules

- Workflow workspace é opcional por projeto.
- Device workspaces e capabilities são singletons globais.
- Automation engine não implementado; interfaces apenas.
- JSON validation para config de workspace.

---

## 8. Domain Modeling

### Entities

| Entity | Identity | Responsibility |
|---|---|---|
| WorkflowWorkspace | ProjectId | Config JSON de board visual |
| WorkflowNode | WorkflowNodeId | Nó do grafo |
| WorkflowSequence | WorkflowSequenceId | Sequência de execução |
| ProjectSummary | ProjectId | Resumo gerado (projetos que não são Jira) |

### Value Objects

| Value Object | Fields |
|---|---|
| WorkflowNodeId | string Value |
| WorkflowSequenceId | string Value |

---

## 9. Expected Architecture

```text
src/Taskboard.Workflow/
  (projeto placeholder para interfaces/stubs)

src/Taskboard.Domain/Entities/
  WorkflowWorkspace.cs
  WorkflowNode.cs
  WorkflowSequence.cs
  ProjectSummary.cs
```

---

## 10. API Contracts

```http
GET/PUT /api/workflow-workspaces/{projectId}
GET/PUT /api/device-workspaces
GET/PUT /api/workflow-capabilities
```

---

## 11. Application Contracts

```csharp
public sealed record GetWorkflowWorkspaceQuery(string ProjectId) : IRequest<WorkflowWorkspaceDto>;
public sealed record UpdateWorkflowWorkspaceCommand(string ProjectId, JsonDocument Config) : IRequest;
```

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`.

Tabelas:
- `workflow_workspaces`
- `project_summaries`
- `workflow_nodes`
- `workflow_sequences`

---

## 13. Integrations

Nenhuma no momento.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Workflow workspace não existe | GET /api/workflow-workspaces/invalid | 404 |
| JSON inválido | PUT com config inválido | 400 INVALID_JSON |
| Device workspace vazio | GET sem config | retorna default |

---

## 15. Few-Shot Examples

```http
PUT /api/workflow-workspaces/local
{
  "columns": [
    { "id": "todo", "title": "To Do" },
    { "id": "in_progress", "title": "In Progress" },
    { "id": "done", "title": "Done" }
  ],
  "automations": []
}
```

---

## 16. Non-Functional Requirements

- JSON config validado com schema.
- Persistência em SQLite.

---

## 17. Mandatory Guardrails

- Não executar automação real sem validação humana.
- Não logar credenciais de agentes.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| GET/PUT workflow workspace | persiste JSON |
| Device workspaces | singleton |
| Workflow capabilities | retorna defaults |

---

## 19. Acceptance Criteria

- [x] Workflow workspace endpoints.
- [x] Device workspaces e capabilities endpoints.
- [x] Entidades de domínio (WorkflowNode, WorkflowSequence, ProjectSummary).
- [x] Interfaces de automação (stubs).

---

## 20. Implementation Plan

1. Criar entidades de domínio (`WorkflowWorkspace`, `WorkflowNode`, `WorkflowSequence`, `ProjectSummary`).
2. Mapear endpoints em `Taskboard.Server`.
3. Implementar stubs de automation engine (interfaces).

---

## 21. Rollback Strategy

- Remover automation endpoints.
- Manter workspace config.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Automation engine complexo | Alto | Alta | Stub, não implementar completo |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Endpoints claros.
- [x] Entidades de domínio definidas.
- [x] Interfaces de automação stubs.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Automation engine deve ser implementado ou apenas stubs? (Stubs - interfaces)
2. Quais tipos de automação são necessários? (Ainda não definido - deferir)

## Human Approval Checklist

- [x] Workspace mapeado.
- [x] Nodes e sequences definidos.
- [x] Interfaces stubs claras.