# SPEC-001: Domain Model

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Domain Model |
| Product / System | agent-harness |
| Module / Bounded Context | Taskboard Core |
| Change type | Migration / Design |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-domain-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O domínio atual em `shared/domain.mjs` e `server/database.mjs` define statuses, prioridades, projetos, tarefas, labels, comentários, anexos, relacionamentos, atividades, workflow workspaces e chat de IA. A migração para .NET 10 deve reproduzir esse domínio com tipos fortes, validações e eventos de domínio.

### Objective

Projetar o modelo de domínio C# para `Project`, `Task`, `Comment`, `Attachment`, `TaskRelation`, `TaskActivity`, `WorkflowWorkspace` e `AiChatThread`, refletindo fielmente o schema e regras do Node.js.

### Expected outcome

Classes de domínio imutáveis/seguras, rich models, value objects para `TaskStatus`, `TaskPriority`, `Actor`, `Recurrence`, `TaskIdentifier`, `AttachmentKind`, `RelationType`, `Sandbox`, `ModelRef`, `AiChatThreadStatus`.

### Out of scope

- Implementação de controllers/handlers nesta spec (ver `SPEC-002`).
- Persistência EF Core (ver `SPEC-011`).

---

## 2. Agent Role

> You are a senior C# domain modeler using DDD, .NET 10, ABP N-Layer, and Clean Architecture.

### Expected behavior

- Model aggregates with clear invariants.
- Use value objects for type-safe enumerations.
- Publish domain events for state changes.
- Keep the domain layer free of infrastructure concerns.

---

## 3. Agent Autonomy Level

### Selected level

3

### Restrictions

- Não alterar contratos HTTP.
- Não introduzir tipos sem justificativa.
- Não referenciar EF Core no Domain project.

---

## 4. Product Context

### Functional context

O Taskboard agrupa tarefas em projetos. Cada projeto mantém contador sequencial `next_task_number`, labels próprios e path do workspace. Tarefas transitam pelos status: `backlog`, `todo`, `in_progress`, `in_review`, `blocked`, `done`, `canceled`. Comentários, anexos e relacionamentos (parent/blocks/related) enriquecem a colaboração. Workspaces de workflow e chat de IA estendem a plataforma.

### Technical context

Atualmente os estados são constantes JS em `shared/domain.mjs`. O `database.mjs` usa SQLite `CHECK` constraints.

### Relevant stack

- .NET 10, C# 14
- ABP N-Layer Domain module
- xUnit + Shouldly + NSubstitute

---

## 5. Task Definition

### Main task

Criar o modelo de domínio C# equivalente ao domínio Node.js.

### Subtasks

- Value objects: `TaskStatus`, `TaskPriority`, `Actor`, `Recurrence`, `TaskIdentifier`, `ProjectId`, `TaskId`, `CommentId`, `AttachmentId`, `AttachmentKind`, `RelationType`, `Sandbox`, `ModelRef`, `AiChatThreadStatus`, `ThreadBinding`, `TaskPatch`, `WorkflowNodeId`, `WorkflowSequenceId`, `AiChatThreadId`, `AiChatRunId`, `AiChatEventId`, `TaskActivityId`.
- Aggregates: `Project`, `Task`, `AiChatThread`.
- Entities: `Comment`, `Attachment`, `TaskRelation`, `TaskActivity`, `WorkflowWorkspace`, `ProjectSummary`, `AiChatRun`, `AiChatEvent`, `WorkflowNode`, `WorkflowSequence`.
- Domain events para mutações de tarefa e projeto.

### Do not do

- Não criar DTOs nesta camada.
- Não referenciar EF Core no Domain.
- Não usar `DateTime.Now` direto; usar `IClock` do ABP.

---

## 6. Functional Requirements

### FR-001: Status e prioridades tipadas

**Description:**  
O domínio deve restringir `TaskStatus` aos valores: `backlog`, `todo`, `in_progress`, `in_review`, `blocked`, `done`, `canceled`.  
E `TaskPriority` a: `none`, `urgent`, `high`, `medium`, `low`.

**Rules:**

- Status e prioridades são case-sensitive strings.
- Valores inválidos lançam `DomainException`.

**Inputs:**

| Field | Type | Required | Rule |
|---|---|---:|---|
| value | string | yes | dentro do enum permitido |

**Outputs:**

| Field | Type | Description |
|---|---|---|
| status/priority | value object | validado |

**Acceptance criteria:**

- [x] Testes Dado/Quando/Então para todos os valores válidos e inválidos.

### FR-002: Ciclo de vida da tarefa

**Description:**  
Uma tarefa pode ser criada, movida entre statuses, atualizada (PATCH), arquivada, restaurada e deletada permanentemente (apenas se arquivada e não Jira).

**Rules:**

- Tarefas Jira não podem ser arquivadas/restauradas/deletadas pelo Taskboard.
- Versão incrementa a cada mutação.
- `archived_at` null indica ativa.

**Acceptance criteria:**

- [x] `Task.Move(status, sortOrder, actor)` atualiza status e ordem.
- [x] `Task.Archive(actor)` seta `ArchivedAt`.
- [x] `Task.Restore(actor)` limpa `ArchivedAt`.
- [x] `Task.Delete(actor)` só permitido se arquivada e não Jira.

### FR-003: Identificadores e numeração

**Description:**  
Projeto gera tarefas com identifier `TASK-{projectId}-{number}`. Jira usa `JIRA:{origin}:{externalKey}`.

**Rules:**

- `next_task_number` incrementa de forma atômica.
- Identificadores são únicos globalmente.

**Acceptance criteria:**

- [x] `Project.GenerateTaskIdentifier()` retorna string correta.

### FR-004: Comentários

**Description:**  
Comentário pertence a uma `Task`, possui `body`, `author` (`Actor`), opcional `thread_id`.

**Rules:**

- Body não pode ser vazio após trim.
- Deleção remove comentário e anexos vinculados (cascata).
- Edição atualiza `UpdatedAt`.

### FR-005: Anexos

**Description:**  
Upload de arquivos para tarefa ou comentário; metadados: `id`, `task_id`, `comment_id`, `kind` (`inline`/`attachment`), `filename`, `content_type`, `size`, `path`.

**Rules:**

- `size` >= 0.
- `kind` = `inline` ou `attachment`.

### FR-006: Relacionamentos

**Description:**  
Relacionamentos entre tarefas: `parent`, `blocks`, `related`.

**Rules:**

- `source_task_id` != `target_task_id`.
- `related` exige `source < target` lexicograficamente para unicidade.
- Apenas um `parent` por tarefa filha (target).
- Deleção cascata quando tarefa é removida.

### FR-007: Atividades

**Description:**  
Registro imutável de mudanças em tarefas com `actor`, `changes` JSON e timestamp.

**Rules:**

- Criado automaticamente em create/update/move/archive/restore/delete.
- `changes` é JSON com campos alterados.

### FR-008: Workflow Workspace

**Description:**  
Configuração JSON de board visual por projeto.

### FR-009: AI Chat

**Description:**  
Threads de conversa com runs e events.

**Rules:**

- Thread possui runs e events.
- Status permitidos: `idle`, `running`, `failed`.
- Sandbox: `read-only`, `workspace-write`, `danger-full-access`.

---

## 7. Business Rules

### BR-001: Status fixos

Apenas os sete status definidos são aceitos.

### BR-002: Prioridades fixas

Apenas as cinco prioridades definidas são aceitas.

### BR-003: Versão otimista

Cada mutação incrementa `version`. PATCH requer `version` e falha em conflito.

### BR-004: Tarefas arquivadas imutáveis

Tarefas arquivadas não podem ser atualizadas nem movidas. Devem ser restauradas primeiro.

### BR-005: Projeto local global

Existe sempre o projeto `local` com nome `全局` e `workspacePath` null.

### BR-006: Anexos pertencem a tarefas

Attachment sempre vinculado a uma Task; opcionalmente a um Comment.

### BR-007: Parent único

Uma tarefa target só pode ter um parent.

### BR-008: Related simétrico

`related` é não-direcional; `source < target` garante unicidade.

### BR-009: Jira tasks imutáveis

Tarefas com `external_source = jira` não podem ser arquivadas, restauradas ou deletadas.

### BR-010: ThreadBinding opcional

Task pode ter `ThreadBinding` para associar execução de agente.

### Domain invariants

- Task `project_id` não muda.
- `TaskRelation` não permite auto-relacionamento nem `related` duplicado invertido.
- Cada tarefa só pode ter um pai (`parent`).
- Um projeto não pode ser removido se houver tarefas ativas não-arquivadas.
- `next_task_number` nunca decrementa.
- Task title max 240 chars.
- Comment body não pode ser vazio.
- Attachment size não pode ser negativo.

---

## 8. Domain Modeling

### Bounded Context

Taskboard Core

### Aggregates

| Aggregate | Responsibility | Invariants |
|---|---|---|
| Project | Gerencia projetos, labels e numeração de tarefas | `next_task_number` crescente |
| Task | Ciclo de vida, atributos, versionamento | status válido, version > 0, não arquivada para mutação |
| AiChatThread | Gerencia runs e events | status válido |

### Entities

| Entity | Identity | Responsibility |
|---|---|---|
| Comment | CommentId | Texto e metadados |
| Attachment | AttachmentId | Metadados de arquivo |
| TaskRelation | Guid (composto) | Relacionamento |
| TaskActivity | TaskActivityId | Log de mudanças |
| WorkflowWorkspace | ProjectId | Config JSON de board visual |
| ProjectSummary | ProjectId | Resumo gerado |
| AiChatRun | AiChatRunId | Execução |
| AiChatEvent | AiChatEventId | Evento |
| WorkflowNode | WorkflowNodeId | Nó do grafo |
| WorkflowSequence | WorkflowSequenceId | Sequência de execução |

### Value Objects

| Value Object | Fields | Validations |
|---|---|---|
| TaskStatus | string Value | in TASK_STATUSES |
| TaskPriority | string Value | in TASK_PRIORITIES |
| Actor | Type, Id, Name, AvatarUrl | Type in ('user','agent') |
| Recurrence | Interval, Unit | Unit in ('day','week','month','year'), Interval > 0 |
| TaskIdentifier | string Value | formato correto, único |
| ProjectId | string Value | <=128 chars |
| TaskId | string Value | <=128 chars |
| CommentId | string Value | <=128 chars |
| AttachmentId | string Value | <=128 chars |
| AttachmentKind | string | 'inline' ou 'attachment' |
| RelationType | string | 'parent','blocks','related' |
| Sandbox | string | 'read-only','workspace-write','danger-full-access' |
| ModelRef | string Value | in catalog |
| AiChatThreadStatus | string Value | 'idle','running','failed' |
| ThreadBinding | ThreadId, RunId | ambos opcionais |
| TaskPatch | 14 campos opcionais | PATCH parcial |
| WorkflowNodeId | string Value | <=128 chars |
| WorkflowSequenceId | string Value | <=128 chars |
| AiChatThreadId | string Value | <=128 chars |
| AiChatRunId | string Value | <=128 chars |
| AiChatEventId | string Value | <=128 chars |
| TaskActivityId | string Value | <=128 chars |

### Domain Events

| Event | When it occurs | Payload |
|---|---|---|
| TaskCreatedDomainEvent | Após criação | TaskId, ProjectId |
| TaskMovedDomainEvent | Após move | TaskId, OldStatus, NewStatus |
| TaskUpdatedDomainEvent | Após patch | TaskId, ChangedFields |
| TaskArchivedDomainEvent | Após archive | TaskId |
| TaskRestoredDomainEvent | Após restore | TaskId |
| TaskDeletedDomainEvent | Após delete | TaskId |
| ProjectLabelsUpdatedDomainEvent | Após novo label | ProjectId |
| CommentAddedDomainEvent | Após novo comentário | TaskId, CommentId |

### Expected C# style

```csharp
public sealed class Project : AggregateRoot<ProjectId>
{
    private readonly List<string> _labels = new();
    public string Name { get; private set; }
    public string? WorkspacePath { get; private set; }
    public IReadOnlyCollection<string> Labels => _labels.AsReadOnly();
    public long NextTaskNumber { get; private set; } = 1;

    public static Project Create(ProjectId id, string name, string? workspacePath)
        => new(id, name, workspacePath);

    public TaskIdentifier GenerateTaskIdentifier()
    {
        var number = NextTaskNumber++;
        return TaskIdentifier.From($"TASK-{Id.Value}-{number}");
    }

    public void AddLabel(string label) => _labels.Add(label);
}

public sealed class Task : AggregateRoot<TaskId>
{
    private readonly List<string> _labels = new();
    public TaskIdentifier Identifier { get; private set; }
    public ProjectId ProjectId { get; private set; }
    public string Title { get; private set; }
    public TaskStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTime? ArchivedAt { get; private set; }

    public void Move(TaskStatus newStatus, double? sortOrder, Actor actor)
    {
        if (ArchivedAt.HasValue) throw new DomainException("Archived tasks cannot be moved.");
        var old = Status.Value;
        Status = newStatus;
        SortOrder = sortOrder;
        Version++;
        AddDomainEvent(new TaskMovedDomainEvent(Id, old, Status.Value));
    }
}
```

---

## 9. Expected Architecture

ABP Domain layer: `Taskboard.Domain` project.

- `AggregateRoot<T>` base from ABP.
- `DomainService` only for cross-aggregate logic.
- `DomainException` for invariant violations.

---

## 10. API Contracts

Ver `SPEC-002-rest-api.md`.

---

## 11. Application Contracts

```csharp
public sealed record CreateTaskCommand(
    ProjectId ProjectId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    IReadOnlyList<string> Labels,
    string? ThreadId,
    ThreadBinding? ThreadBinding,
    Actor Creator
) : IRequest<TaskDto>;

public sealed record AddCommentCommand(
    TaskId TaskId,
    string Body,
    Actor Author,
    ThreadBinding? Thread
) : IRequest<CommentDto>;

public sealed record CreateRelationCommand(
    TaskId SourceTaskId,
    TaskId TargetTaskId,
    RelationType Type
) : IRequest;
```

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`.

Tabelas/entidades:

- `projects`
- `tasks`
- `comments`
- `task_activities`
- `attachments`
- `workflow_workspaces`
- `project_summaries`
- `ai_chat_threads`
- `ai_chat_runs`
- `ai_chat_events`
- `task_relations`
- `workflow_nodes`
- `workflow_sequences`

---

## 13. Integrations

Nenhuma externa no domínio.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Status inválido | "invalid" | DomainException |
| Mover tarefa arquivada | task.ArchivedAt != null | DomainException |
| Versão conflito | version != current | VersionConflictException |
| Comentário vazio | "" | DomainException |
| Anexo tamanho negativo | size=-1 | DomainException |
| Relacionamento consigo | source==target | DomainException |
| Segundo parent | parent já existe para target | DomainException |
| Título task > 240 chars | "x" * 241 | DomainException |

---

## 15. Few-Shot Examples

### Exemplo: criação de tarefa

```csharp
var project = Project.Create(ProjectId.From("my-project"), "My Project");
var task = Task.Create(
    TaskId.NewGuid(),
    project.NextTaskIdentifier(),
    project.Id,
    "Nova feature",
    TaskStatus.Todo,
    TaskPriority.High,
    new[] { "特性" },
    Actor.LocalUser()
);
task.Identifier.Value.ShouldBe("TASK-my-project-1");
```

### Exemplo: upload de anexo

```csharp
var attachment = Attachment.Create(
    AttachmentId.NewGuid(),
    TaskId.From("task-1"),
    AttachmentKind.Attachment,
    "diagrama.png",
    "image/png",
    1024,
    "/path/to/diagrama.png"
);
```

---

## 16. Non-Functional Requirements

- Value objects imutáveis (records).
- Domain events testáveis.
- Sem `DateTime.Now` direto; usar `IClock` do ABP.
- StringValueObject base para value objects baseados em string.

---

## 17. Mandatory Guardrails

- Domain não acessa infraestrutura.
- Sem `DateTime.Now` direto; usar `IClock` do ABP.
- Não criar DTOs no Domain project.
- Não referenciar EF Core no Domain project.

---

## 18. Expected Tests

| Class | Scenarios |
|---|---|
| TaskStatus | valores válidos/inválidos |
| TaskPriority | valores válidos/inválidos |
| Actor | tipo user/agent |
| Recurrence | interval/unit válidos/inválidos |
| TaskIdentifier | formatos local e JIRA |
| Project | numeração, labels, workspace |
| Task | criação, move, archive, restore, delete, versionamento, patch |
| TaskRelation | parent único, related simétrico, self-relation |
| Comment | body vazio, thread_id, edit |
| Attachment | kind, size, filename |
| AiChatThread | runs, events, status |
| WorkflowWorkspace | JSON config |

---

## 19. Acceptance Criteria

- [x] Todos os value objects testados.
- [x] Regras de tarefas Jira testadas.
- [x] Eventos de domínio publicados corretamente.
- [x] Invariants de relacionamentos testados.
- [x] Domain tests passam (`dotnet test tests/Taskboard.Tests.Unit`).

---

## 20. Implementation Plan

1. Criar value objects em `Taskboard.Domain.Shared/ValueObjects/`.
2. Criar aggregates Project, Task e AiChatThread em `Taskboard.Domain/Entities/`.
3. Criar entities Comment, Attachment, TaskRelation, TaskActivity, WorkflowWorkspace, ProjectSummary, AiChatRun, AiChatEvent, WorkflowNode, WorkflowSequence.
4. Criar domain events.
5. Escrever domain tests Dado/Quando/Então em `tests/Taskboard.Tests.Unit/Domain/`.

---

## 21. Rollback Strategy

- Reverter para modelos anteriores se quebrar contrato.
- Restaurar backup do SQLite em caso de inconsistência.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Diferença de semântica entre JS dinâmico e C# tipado | Médio | Média | Mapear tipos explicitamente |
| `next_task_number` concorrência em SQLite | Médio | Média | Usar transação serializável ou row lock |

---

## 23. Definition of Done

- [x] Domain model testado e aprovado.
- [x] SPEC revisado.
- [x] Nenhuma dependência de infraestrutura no Domain.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Os identificadores devem permanecer como string ou usar UUIDs internamente? (Resolvido: string IDs)
2. `sort_order` usa double ou decimal para evitar imprecisão? (Resolvido: double)
3. `ProjectId` e `TaskId` devem permitir slugs customizados ou sempre GUID? (Resolvido: string IDs, pode ser slug ou GUID)

## Human Approval Checklist

- [x] Modelo de domínio está alinhado ao bounded context.
- [x] Agregados e entidades identificados.
- [x] Invariants documentados.
- [x] Value objects testáveis.
- [x] Eventos de domínio mapeados.