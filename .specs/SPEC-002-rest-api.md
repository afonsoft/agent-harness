# SPEC-002: REST API

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | REST API e Servidor HTTP |
| Product / System | agent-harness |
| Module / Bounded Context | Taskboard HTTP API |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-httpapi-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O servidor atual `server/app.mjs` (3.300+ linhas) usa `node:http` raw e roteia manualmente para `health`, storage local, projetos, tarefas, eventos SSE, anexos, comentários, workflow, AI e cloud/Jira.

### Objective

Mapear todos os endpoints HTTP do Node.js para ASP.NET Core Minimal APIs, preservando rotas, métodos, query params, payloads e códigos de erro.

### Expected outcome

Aplicação `Taskboard.Server` expondo todos os endpoints com ProblemDetails e error codes customizados, autenticação baseada em cookies, SSE para eventos globais e por thread.

### Out of scope

- Implementação do client frontend (ver `SPEC-008`).
- Deploy Cloudflare (ver `SPEC-006`, `SPEC-010`).

---

## 2. Agent Role

> Senior ASP.NET Core engineer com experiência em Minimal APIs, SSE, file streaming e ProblemDetails.

### Expected behavior

- Preserve exact route contracts.
- Map errors to ProblemDetails with custom `code` fields.
- Implement SSE with correct `text/event-stream` format.
- Support cookie-based authentication and CORS.

---

## 3. Agent Autonomy Level

### Selected level

3

### Restrictions

- Não alterar rotas HTTP sem documentar breaking change.
- Não introduzir autenticação complexa além do cookie auth.

---

## 4. Product Context

### Functional context

Servidor local-first escutando em `0.0.0.0`/`127.0.0.1` na porta `TASKBOARD_PORT` (default `47823`). Serve a SPA Blazor estática e expõe API JSON.

### Technical context

- ASP.NET Core .NET 10 Minimal APIs
- Roteamento com `MapGroup`
- SSE em `/api/events` e `/api/local/ai/threads/:id/events`
- Anexos como arquivos binários (multipart/form-data)
- Erros com `{ error: { code, message, details? } }`
- Autenticação via cookies (HttpOnly, SameSite=Strict)
- CORS para desenvolvimento (`http://localhost:5173`)

### Relevant stack

- ASP.NET Core .NET 10
- Minimal APIs
- Server-Sent Events (SSE)
- ProblemDetails
- Microsoft.Data.Sqlite / EF Core

---

## 5. Task Definition

### Main task

Implementar todos os endpoints HTTP equivalentes.

### Subtasks

- Health e meta.
- Autenticação (login/logout).
- Client storage e local endpoints.
- Projetos (`/api/projects`).
- Tarefas (`/api/tasks`).
- Comentários e anexos.
- Relacionamentos e atividades.
- Workflow workspaces e device workspaces.
- AI catalog/threads/composer.
- Jira connection.
- SSE events (global e por thread).
- Static files fallback (Blazor).

### Do not do

- Não reimplementar lógica de IA sem `SPEC-005`.
- Não reimplementar Jira sem `SPEC-010`.

---

## 6. Functional Requirements

### FR-001: Health

**Endpoint:** `GET /health`  
**Response:** `200 OK` com status e metadata.

```json
{ "status": "ok", "timestamp": "2026-08-31T00:00:00Z" }
```

### FR-002: Autenticação

**Endpoints:**

```http
POST   /api/login
POST   /api/logout
```

**Regras:**

- Login via form data: `Username`, `Password`, `ReturnUrl`.
- Credenciais configuradas via `AdminUser` (variáveis de ambiente).
- Cookie HttpOnly, SameSite=Strict, expiração 8 horas.
- Rotas não-API redirecionam para `/login` se não autenticado.

### FR-003: Projetos

**Endpoints:**

```http
GET    /api/projects
POST   /api/projects
GET    /api/projects/{id}
```

**Regras:**

- GET lista todos com `issueCount` (tarefas não arquivadas).
- POST cria com `id` (opcional, gera GUID se não informado), `name`, `workspacePath`.
- Projeto `local` sempre inicializado na migration.
- Retorna `201 Created` com Location header.
- Conflict 409 se ID duplicado.

### FR-004: Tarefas

**Endpoints:**

```http
GET    /api/tasks?projectId=&status=&archived=&q=&assigneeId=&label=
POST   /api/tasks
GET    /api/tasks/{id}
PATCH  /api/tasks/{id}
DELETE /api/tasks/{id}
POST   /api/tasks/{id}/move
POST   /api/tasks/{id}/archive
POST   /api/tasks/{id}/restore
GET    /api/tasks/{id}/comments
POST   /api/tasks/{id}/comments
PATCH  /api/tasks/{taskId}/comments/{commentId}
DELETE /api/tasks/{taskId}/comments/{commentId}
GET    /api/tasks/{id}/activities
GET    /api/tasks/{id}/relations
POST   /api/tasks/{id}/relations
DELETE /api/tasks/{id}/relations/{type}/{targetTaskId}
```

**Regras:**

- Query params para listagem: `projectId`, `status`, `q`, `assigneeId`, `label`, `archived`.
- PATCH requer `version` e `changes` (TaskPatch).
- DELETE requer `version` e a tarefa deve estar arquivada e não ser Jira.
- Move requer `status` obrigatório, `sortOrder` opcional.
- Archive/Restore validam Jira (não permitido) e estado arquivado.

### FR-005: Anexos

**Endpoints:**

```http
POST   /api/attachments
GET    /api/attachments/{id}/content
GET    /api/attachments/{id}/download
DELETE /api/attachments/{id}
```

**Regras:**

- Upload via multipart/form-data: `file`, `taskId`, `commentId` (opcional), `kind` (default `file`).
- Arquivos salvos em `.data/attachments/{attachmentId}/{filename}`.
- Content serve inline, download força download.

### FR-006: SSE Events (Global)

**Endpoint:** `GET /api/events`  
Emite eventos: `task.created`, `task.updated`, `task.moved`, `task.archived`, `task.restored`, `task.deleted`, `comment.added`, `attachment.created`, `attachment.deleted`, `project.labels_updated`.

**Formato:**

```
event: task.created
data: { "taskId": "...", "projectId": "..." }

```

### FR-007: Local endpoints

```http
GET    /api/client-storage
PUT    /api/client-storage
GET    /api/local/codex-thread-progress
GET    /api/local/host-runtime
GET/PUT /api/local/cloud-session
GET/POST /api/local/jira-connection
POST   /api/local/jira-connection/sync
GET    /api/meta
GET/POST /api/local/ai/catalog
GET    /api/local/ai/composer/candidates
POST   /api/local/ai/composer/rebind
GET/POST /api/local/ai/threads
GET    /api/local/ai/threads/{id}/events (SSE)
POST   /api/local/ai/threads/{id}/events
POST   /api/local/ai/threads/{id}/runs
PATCH  /api/local/ai/threads/{threadId}/runs/{runId}
GET/PUT /api/device-workspaces
GET/PUT /api/workflow-capabilities
```

---

## 7. Business Rules

- Rotas `/api/*` não encontradas retornam `404 NOT_FOUND`.
- Rotas estáticas fallback para Blazor `index.html`.
- Query params desconhecidos em rotas específicas retornam `400 UNKNOWN_QUERY_PARAMETER`.
- Métodos não permitidos retornam `405` com `Allow` header.
- Cookie auth obrigatório para rotas não-públicas (`/health`, `/login`, `/logout`, `/api/meta`, `/api/client-storage`, `/api/events` são públicas).
- CORS configurado para `http://localhost:5173` em dev.

---

## 8. Domain Modeling

Ver `SPEC-001-domain-model.md`.

---

## 9. Expected Architecture

### Middleware

- `CorrelationIdMiddleware` (implícito via HttpContext)
- `GlobalExceptionHandlerMiddleware` → ProblemDetails customizado
- `CookieAuthenticationMiddleware`
- `CorsMiddleware` (policy "Dev")
- Static files middleware
- Blazor antiforgery

### SSE implementation

```csharp
app.MapGet("/api/events", async (HttpResponse response, IEventStreamService events, CancellationToken ct) =>
{
    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";
    
    await foreach (var ev in events.SubscribeAsync(ct))
    {
        await response.WriteAsync($"event: {ev.Type}\n", ct);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(ev.Payload, ApiJsonOptions.Default)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
});
```

### Error handling

```csharp
app.UseExceptionHandler();
app.AddExceptionHandler<GlobalExceptionHandler>();
```

GlobalExceptionHandler mapeia `DomainException` para `ProblemDetails` com `code` customizado.

---

## 10. API Contracts

### Listar tarefas

```http
GET /api/tasks?projectId=local&status=todo&archived=false
```

Response:

```json
{
  "tasks": [ { "id": "...", "identifier": "TASK-local-1", ... } ],
  "project": { "id": "local", "name": "全局", "issueCount": 5 }
}
```

### Criar tarefa

```http
POST /api/tasks
{
  "projectId": "local",
  "title": "Implementar login",
  "description": "...",
  "status": "todo",
  "priority": "high",
  "labels": ["特性"],
  "creator": { "type": "user", "id": "local", "name": "Local User" },
  "sortOrder": 1.0,
  "startDate": "2026-08-31T00:00:00Z",
  "dueDate": "2026-09-07T00:00:00Z"
}
```

### PATCH tarefa

```http
PATCH /api/tasks/task-123
{
  "version": 3,
  "changes": { "title": "Novo título", "priority": "urgent", "labels": ["bug"] }
}
```

### Error responses

| Status | Code | Quando |
|---|---|---|
| 400 | INVALID_PATH | path malformado |
| 400 | UNKNOWN_QUERY_PARAMETER | query inesperada |
| 400 | INVALID_ATTACHMENT | file ou taskId faltando |
| 401 | UNAUTHORIZED | credenciais inválidas |
| 404 | TASK_NOT_FOUND | tarefa inexistente |
| 404 | PROJECT_NOT_FOUND | projeto inexistente |
| 404 | COMMENT_NOT_FOUND | comentário inexistente |
| 404 | ATTACHMENT_NOT_FOUND | anexo inexistente |
| 404 | THREAD_NOT_FOUND | thread IA inexistente |
| 404 | RUN_NOT_FOUND | run IA inexistente |
| 409 | VERSION_CONFLICT | version obsoleto |
| 409 | PROJECT_EXISTS | projeto duplicado |
| 409 | MODEL_EXISTS | modelo IA duplicado |
| 409 | JIRA_ARCHIVE_UNAVAILABLE | operações proibidas em Jira |
| 409 | TASK_ARCHIVED | tarefa arquivada não modificável |
| 409 | TASK_IS_JIRA | tarefa Jira não modificável |
| 409 | SELF_RELATION | auto-relacionamento |
| 500 | INTERNAL_ERROR | erro interno |
| 502 | JIRA_RECONCILE_FAILED | sync falhou |

---

## 11. Application Contracts

```csharp
public sealed record ListTasksQuery(
    string? ProjectId,
    string? Status,
    bool? Archived,
    string? Q,
    string? AssigneeId,
    string? Label
) : IRequest<TaskListDto>;

public sealed record GetTaskQuery(TaskId Id) : IRequest<TaskDto>;
public sealed record CreateTaskCommand(
    string ProjectId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    IReadOnlyList<string> Labels,
    Actor? Creator = null,
    double? SortOrder = null,
    DateTime? StartDate = null,
    DateTime? DueDate = null
) : IRequest<TaskDto>;
public sealed record UpdateTaskCommand(
    TaskId Id,
    long Version,
    TaskPatch Changes
) : IRequest<TaskDto>;
public sealed record MoveTaskCommand(
    TaskId Id,
    string Status,
    double? SortOrder
) : IRequest<TaskDto>;
public sealed record ArchiveTaskCommand(TaskId Id) : IRequest<TaskDto>;
public sealed record RestoreTaskCommand(TaskId Id) : IRequest<TaskDto>;
public sealed record DeleteTaskCommand(TaskId Id, long Version) : IRequest;
```

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`.

---

## 13. Integrations

Ver `SPEC-006-cloud.md`, `SPEC-010-integrations.md`.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| ID muito longo | >128 chars | 400 INVALID_PATH |
| Query param não esperado | `?foo=bar` em GET /api/tasks/:id | 400 UNKNOWN_QUERY_PARAMETER |
| Versão errada | version=1 quando current=2 | 409 VERSION_CONFLICT |
| Tarefa Jira arquivada | source=jira + archive | 409 JIRA_ARCHIVE_UNAVAILABLE |
| Servidor offline | — | health falha |
| Não autenticado | Acesso a /api/tasks | 302 redirect /login |
| Upload sem file | POST /api/attachments sem file | 400 INVALID_ATTACHMENT |
| Comment body vazio | POST /comments com body="" | 400 EmptyCommentBody |

---

## 15. Few-Shot Examples

### Exemplo: criação de projeto

```http
POST /api/projects
{
  "id": "my-project",
  "name": "My Project",
  "workspacePath": "/home/user/my-project"
}
```

Response:

```json
{
  "project": {
    "id": "my-project",
    "name": "My Project",
    "workspacePath": "/home/user/my-project",
    "labels": ["缺陷", "特性", "for-claude", "hold", "改进", "phase-1", "phase-2", "phase-3", "phase-4", "phase-5", "phase-6"],
    "issueCount": 0,
    "createdAt": "2026-08-24T01:53:00Z",
    "updatedAt": "2026-08-24T01:53:00Z"
  }
}
```

---

## 16. Non-Functional Requirements

- P95 < 300ms para operações de leitura.
- SSE reconnect faz full refresh no cliente.
- CORS configurado para `http://localhost:5173` em dev.
- Static files e fallback SPA funcionam (Blazor).

---

## 17. Mandatory Guardrails

- Não alterar contratos sem versionamento/documentação.
- Não colocar regras de negócio em endpoints.
- Não expor dados sensíveis em ProblemDetails.
- Respeitar cancellation tokens.
- Validar anti-forgery para state-changing operations.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| GET /health | 200 |
| POST /api/login | 200/302 |
| POST /api/projects | 201 Created |
| GET /api/projects | retorna lista com local |
| POST /api/tasks | cria com identifier correto |
| PATCH /api/tasks/:id | version conflict 409 |
| GET /api/events | SSE stream funciona |
| POST /api/attachments | multipart upload |
| GET /api/local/ai/threads/:id/events | SSE thread events |
| Static files | fallback para index.html (Blazor) |

---

## 19. Acceptance Criteria

- [x] Todos os endpoints mapeados.
- [x] ProblemDetails customizado via GlobalExceptionHandler.
- [x] SSE funcional (global e por thread).
- [x] CORS e cookie auth.
- [x] Static files fallback (Blazor).

---

## 20. Implementation Plan

1. Criar `Taskboard.Server` project (Web).
2. Configurar Minimal APIs e middleware.
3. Configurar autenticação cookie + AdminUser.
4. Mapear health, meta, client-storage.
5. Mapear `/api/projects`.
6. Mapear `/api/tasks` e sub-recursos.
7. Mapear `/api/attachments`.
8. Mapear `/api/events` SSE (global).
9. Mapear local endpoints (AI, cloud, Jira, workflow).
10. Mapear `/api/local/ai/threads/:id/events` SSE (por thread).
11. Configurar static files e Blazor fallback.
12. Escrever integration tests com `WebApplicationFactory`.

---

## 21. Rollback Strategy

- Reverter branch.
- Restaurar backup do SQLite.
- Desabilitar feature flag.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Diferença de roteamento raw vs Minimal APIs | Médio | Média | Testar rotas 1:1 |
| SSE scaling | Médio | Baixa | In-memory channel para MVP |
| Cookie auth vs token | Médio | Baixa | Configurar SameSite/HttpOnly |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Todos os endpoints mapeados.
- [x] Tests automatizados.
- [x] Build validado.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Usar Minimal APIs ou controllers MVC? (Resolvido: Minimal APIs)
2. Como implementar SSE em produção (`IAsyncEnumerable`, canal, SignalR)? (Resolvido: In-memory channel)
3. Static files: servir build Blazor existente? (Resolvido: Blazor Server com static assets)

## Human Approval Checklist

- [x] Contratos API explícitos.
- [x] Códigos de erro mapeados.
- [x] SSE e CORS considerados.
- [x] Static files e fallback definidos.
- [x] Autenticação/autorização documentada.