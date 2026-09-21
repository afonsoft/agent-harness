# SPEC-011: Persistence

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Persistence and Database |
| Product / System | agent-harness |
| Module / Bounded Context | Infrastructure |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-persistence-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O sistema atual usa `node:sqlite` com schema evolutivo (ALTER TABLE, migrations manuais). É necessário mapear para EF Core 10 com SQLite e Migrations.

### Objective

Criar `Taskboard.EntityFrameworkCore` com EF Core 10, SQLite, configuração de entidades, repositórios e migrations iniciais equivalentes ao schema final do Node.js.

### Expected outcome

- `DbContext` com `Projects`, `Tasks`, `Comments`, `Attachments`, `TaskActivities`, `TaskRelations`, `WorkflowWorkspaces`, `ProjectSummaries`, `AiChatThreads`, `AiChatRuns`, `AiChatEvents`, `WorkflowNodes`, `WorkflowSequences`.
- Repositórios implementando `IRepository<T>`.
- Migrations iniciais e seeds de dados (`local`).

### Out of scope

- Sincronização D1/R2 (ver `SPEC-006`, `SPEC-010`).

---

## 2. Agent Role

> Senior EF Core/SQLite engineer.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não colocar lógica de negócio nos repositórios.
- Não expor connection strings em appsettings.

---

## 4. Product Context

### Functional context

Banco local-first em `.data/taskboard.sqlite`. Pode ser copiado/backupeado.

### Technical context

- SQLite com `Microsoft.EntityFrameworkCore.Sqlite`.
- Migrations via `dotnet ef migrations`.
- Seed do projeto `local` na inicialização.

### Relevant stack

- .NET 10
- EF Core 10
- SQLite (`Microsoft.Data.Sqlite`)
- xUnit + Shouldly

---

## 5. Task Definition

### Main task

Mapear persistência para EF Core + SQLite.

### Subtasks

- DbContext com todas as entidades.
- Configurações de entidade (Fluent API).
- Repositórios genéricos e específicos.
- Migrations (initial create).
- Seed data (projeto `local`).

### Do not do

- Não implementar lógica de domínio nos repositórios.

---

## 6. Functional Requirements

### FR-001: DbContext

**Description:**  
`TaskboardDbContext` com todos os `DbSet`.

```csharp
public sealed class TaskboardDbContext : DbContext
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Taskboard.Domain.Entities.Task> Tasks => Set<Taskboard.Domain.Entities.Task>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<TaskRelation> TaskRelations => Set<TaskRelation>();
    public DbSet<WorkflowWorkspace> WorkflowWorkspaces => Set<WorkflowWorkspace>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowSequence> WorkflowSequences => Set<WorkflowSequence>();
    public DbSet<ProjectSummary> ProjectSummaries => Set<ProjectSummary>();
    public DbSet<AiChatThread> AiChatThreads => Set<AiChatThread>();
    public DbSet<AiChatRun> AiChatRuns => Set<AiChatRun>();
    public DbSet<AiChatEvent> AiChatEvents => Set<AiChatEvent>();
}
```

### FR-002: Entity Configurations

**Description:**  
Configurações via Fluent API em `Configurations/`.

| Entity | Config |
|---|---|
| Project | PK `Id` (string, max 128), indexes em `Name` |
| Task | PK `Id` (string, max 128), FK `ProjectId`, indexes em `Status`, `AssigneeId` |
| Comment | PK `Id` (string, max 128), FK `TaskId`, index em `TaskId` |
| Attachment | PK `Id` (string, max 128), FK `TaskId`, `CommentId` opcional |
| TaskActivity | PK `Id` (string, max 128), FK `TaskId`, index em `TaskId` |
| TaskRelation | PK composto (`SourceTaskId`, `TargetTaskId`, `RelationType`), FKs |
| WorkflowWorkspace | PK `ProjectId` |
| WorkflowNode | PK `Id` (string, max 128), FK `ProjectId` |
| WorkflowSequence | PK `Id` (string, max 128), FK `ProjectId` |
| ProjectSummary | PK `ProjectId` |
| AiChatThread | PK `Id` (string, max 128), indexes em `CreatedAt` |
| AiChatRun | PK `Id` (string, max 128), FK `ThreadId` |
| AiChatEvent | PK `Id` (string, max 128), FK `ThreadId` |

### FR-003: Repositories

**Description:**  
Repositórios genéricos e específicos.

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetAsync(TId id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetListAsync(CancellationToken ct = default);
    Task<T> InsertAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(TId id, CancellationToken ct = default);
}

public interface ITaskRepository : IRepository<Task, TaskId> { }
public interface IProjectRepository : IRepository<Project, ProjectId> { }
```

**Implementação:** `EfCoreRepository<T, TId>`, `EfCoreTaskRepository`, `EfCoreProjectRepository`.

### FR-004: Migrations

**Description:**  
Migration inicial cria schema completo.

```bash
dotnet ef migrations add InitialCreate --project src/Taskboard.EntityFrameworkCore
dotnet ef database update
```

### FR-005: Seed Data

**Description:**  
Projeto `local` criado automaticamente na migration.

```csharp
migrationBuilder.InsertData(
    table: "Projects",
    columns: new[] { "Id", "Name", "WorkspacePath", "labels", "NextTaskNumber", "CreatedAt", "UpdatedAt", "Version" },
    values: new object[] { "local", "全局", null, "[]", 1L, DateTime.UtcNow, DateTime.UtcNow, 1L });
```

---

## 7. Business Rules

- Strings com max length para evitar SQLite performance issues.
- Timestamps em UTC (`DateTime.UtcNow`).
- Version para optimistic concurrency.
- Soft delete via `ArchivedAt` (não hard delete de tarefas).

---

## 8. Domain Modeling

Ver `SPEC-001-domain-model.md`.

---

## 9. Expected Architecture

```text
src/Taskboard.EntityFrameworkCore/
  Data/
    TaskboardDbContext.cs
    TaskboardDbContextFactory.cs
  Configurations/
    ProjectConfiguration.cs
    TaskConfiguration.cs
    CommentConfiguration.cs
    AttachmentConfiguration.cs
    TaskActivityConfiguration.cs
    TaskRelationConfiguration.cs
    WorkflowWorkspaceConfiguration.cs
    ProjectSummaryConfiguration.cs
    AiChatThreadConfiguration.cs
    AiChatRunConfiguration.cs
    AiChatEventConfiguration.cs
  Repositories/
    EfCoreRepository.cs
    EfCoreTaskRepository.cs
    EfCoreProjectRepository.cs
  Migrations/
    20260824031300_InitialCreate.cs
    20260824031300_InitialCreate.Designer.cs
    TaskboardDbContextModelSnapshot.cs
  ValueConverters/
    StringIdValueConverter.cs
    JsonValueConverter.cs
    ListStringJsonValueConverter.cs
    NullableJsonValueConverter.cs
    StringValueObjectConverter.cs
  ServiceCollectionExtensions.cs
```

---

## 10. API Contracts

Ver `SPEC-002-rest-api.md`.

---

## 11. Application Contracts

```csharp
public interface IRepository<T, TId> where T : class
{
    Task<T?> GetAsync(TId id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetListAsync(CancellationToken ct = default);
    Task<T> InsertAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(TId id, CancellationToken ct = default);
}
```

---

## 12. Persistence and Data

### Database

- SQLite em `.data/taskboard.sqlite`.
- Migrations em `src/Taskboard.EntityFrameworkCore/Migrations/`.

### Tables

| Table | PK | FKs | Indexes |
|---|---|---|---|
| Projects | Id (string) | - | Name |
| Tasks | Id (string) | ProjectId | Status, AssigneeId, ProjectId |
| Comments | Id (string) | TaskId | TaskId |
| Attachments | Id (string) | TaskId, CommentId | TaskId |
| TaskActivities | Id (string) | TaskId | TaskId |
| TaskRelations | composite | SourceTaskId, TargetTaskId | - |
| WorkflowWorkspaces | ProjectId (string) | - | - |
| WorkflowNodes | Id (string) | ProjectId | - |
| WorkflowSequences | Id (string) | ProjectId | - |
| ProjectSummaries | ProjectId (string) | - | - |
| AiChatThreads | Id (string) | - | CreatedAt |
| AiChatRuns | Id (string) | ThreadId | - |
| AiChatEvents | Id (string) | ThreadId | - |

---

## 13. Integrations

Nenhuma.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Banco não existe | app inicia | EF Core cria automaticamente |
| Migration pendente | app inicia | erro amigável |
| Concurrent writes | duas threads | optimistic concurrency (Version) |
| String muito longa | >maxLength | validation error |

---

## 15. Few-Shot Examples

```csharp
// DbContext registration
services.AddDbContext<TaskboardDbContext>(options =>
    options.UseSqlite(connectionString));

// Repository usage
var project = await _projectRepository.GetAsync(ProjectId.From("local"), ct);
var tasks = await _taskRepository.GetListAsync(ct);

// Seed verification
var dbContext = scope.ServiceProvider.GetRequiredService<TaskboardDbContext>();
var localProject = await dbContext.Projects.FindAsync("local");
```

---

## 16. Non-Functional Requirements

- Connection string fora do código (appsettings ou env).
- Migrations idempotentes.
- Soft delete (ArchiveAt) para tarefas.

---

## 17. Mandatory Guardrails

- Não hard delete de tarefas (use archive).
- Não expor connection strings.
- Usar UTC para timestamps.
- Version para optimistic concurrency.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| DbContext cria banco | arquivo .sqlite existe |
| Seed projeto local | Projects contém "local" |
| Migration idempotente | running twice não falha |
| Soft delete | Task.ArchivedAt setado |

---

## 19. Acceptance Criteria

- [x] DbContext com todos os DbSet.
- [x] Entity configurations via Fluent API.
- [x] Repositories implementados.
- [x] Migration inicial criada.
- [x] Seed do projeto local.

---

## 20. Implementation Plan

1. Criar `Taskboard.EntityFrameworkCore` project.
2. Adicionar pacotes NuGet (EF Core, SQLite).
3. Criar `TaskboardDbContext`.
4. Adicionar configurações de entidade.
5. Implementar repositories.
6. Criar migrations.
7. Seed projeto local.
8. Testes de integração.

---

## 21. Rollback Strategy

- `dotnet ef database update <previous-migration>`
- Restaurar backup do SQLite.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Concorrência em SQLite | Médio | Média | Optimistic concurrency (Version) |
| Diferenças SQLite vs Node | Médio | Média | Mapear tipos explicitamente |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] DbContext implementado.
- [x] Repositories funcionais.
- [x] Migrations aplicadas.
- [x] Seed working.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Usar `long` ou `Guid` para IDs? (Resolvido: string para compatibilidade)
2. Transaction isolation level? (Resolvido: serializable para SQLite)

## Human Approval Checklist

- [x] Schema claro.
- [x] Repositories definidos.
- [x] Migrations definidas.
- [x] Seed working.