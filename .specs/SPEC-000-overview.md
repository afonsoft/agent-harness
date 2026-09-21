# SPEC-000: Visão Geral / Overview

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Visão Geral da Migração / Migration Overview |
| Product / System | agent-harness (clone de `dashi-taskboard` / Codex Taskboard) |
| Module / Bounded Context | Taskboard Platform |
| Change type | Migration / Reescrita |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/specs-harness-docs` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O repositório `afonsoft/agent-harness` é uma aplicação local-first de gestão de tarefas (issue board) implementada originalmente em Node.js 22.5+, React 19, Vite, TypeScript e SQLite nativo (`node:sqlite`). A aplicação possui servidor HTTP raw `node:http`, CLI `taskctl`, servidor MCP, Skill para agentes de IA e interface web. O objetivo é produzir um clone funcional em **C# 14 / .NET 10**, preservando comportamento, contratos HTTP e integrações com agentes via MCP e Skill.

### Objective

Criar uma aplicação C# .NET 10 (ASP.NET Core Minimal APIs + ABP N-Layer) que replique a funcionalidade atual: projetos, tarefas, comentários, anexos, relacionamentos, workspaces de workflow, chat de IA, sincronização Jira, CLI e servidor MCP.

### Expected outcome

Após a migração, a aplicação .NET 10 deve:

- Atender às mesmas rotas HTTP documentadas nos specs.
- Persistir os mesmos dados em SQLite/EF Core.
- Oferecer CLI equivalente ao `taskctl` (usando Spectre.Console.Cli).
- Expor servidor MCP com as mesmas ferramentas.
- Disponibilizar Skill para agentes no padrão Agent Skills.
- Implementar autenticação baseada em cookies com usuário admin.
- Servir frontend Blazor (fase 2) e preparar .NET MAUI (fase futura).

### Out of scope

- Reescrita da UI web em outra tecnologia pode ser escopo futuro; nesta fase a UI Blazor está implementada e servida estaticamente.
- Deploy Cloudflare Workers/D1/R2 detalhado em `SPEC-006` e `SPEC-010`.
- Modificação do código-fonte Node.js original (não reescrever o projeto legado).
- Implementação real de C# (concluída): os módulos em `src/` seguem estas specs e compilam.

---

## 2. Agent Role

> You are a senior software engineer specialized in C# 14, .NET 10, Clean Architecture, Domain-Driven Design, ABP N-Layer, automated testing, security, observability, and clean code.  
> Your responsibility is to implement the migration according to each SPEC without inventing requirements, expanding the scope, or making undocumented architectural decisions.

### Expected behavior

- Be conservative with architectural changes.
- Preserve backward compatibility whenever possible.
- Prioritize readability, testability, and maintainability.
- Make uncertainty explicit before implementing.
- Do not introduce external dependencies without justification.
- Do not remove existing tests without a documented technical reason.
- Do not change public contracts without declaring the impact.

---

## 3. Agent Autonomy Level

### Selected level

3

### Restrictions associated with this level

- Do not push directly to `main`.
- Do not change database schemas without migration and rollback plan.
- Do not publish packages or deploy automatically.
- Do not alter existing Node.js source code.
- Do not expand the C# implementation beyond what these specs define without updating the SPEC first.

---

## 4. Product Context

### Functional context

O Taskboard é um quadro de tarefas local-first, compatível com múltiplos agentes de IA, com sincronização opcional Jira, CLI `taskctl` e servidor MCP.

### Technical context

**Atual (Node.js):**

- Node.js 22.5+ com `node:sqlite`.
- Servidor raw `node:http` (não Fastify/Express).
- React 19 + Vite frontend.
- Tauri para desktop.
- CLI `taskctl` em `cli/taskctl.mjs`.
- MCP server em `mcp/index.mjs`.
- Skill em `skills/manage-taskboard/SKILL.md`.
- Cloud: Cloudflare/D1/R2 via `wrangler`.

**Alvo (.NET 10) - IMPLEMENTADO:**

- C# 14.
- ASP.NET Core Minimal APIs.
- ABP N-Layer / Clean Architecture.
- EF Core 10 com SQLite (local-first); PostgreSQL/SQL Server opcional.
- xUnit + Shouldly + NSubstitute.
- SSE (`text/event-stream`) para eventos globais e por thread.
- MCP server com `ModelContextProtocol` SDK .NET, transporte Stdio/SSE.
- CLI `taskctl` como **Spectre.Console.Cli** console app.
- Blazor Server / .NET MAUI Blazor Hybrid para frontend.
- Autenticação baseada em cookies com usuário admin configurável.

### Relevant files or directories

```text
/.specs/                 # Specs unificados
/.claude/                # Harness Claude
/.devin/                 # Configuração Devin
/.agent/                 # Agent Skills
/docs/                   # Documentação en-us/pt-br
/.github/workflows/      # GitHub Actions
/src/
  Taskboard.Domain/
  Taskboard.Domain.Shared/
  Taskboard.Application.Contracts/
  Taskboard.Application/
  Taskboard.EntityFrameworkCore/
  Taskboard.Server/
  Taskboard.Cli/
  Taskboard.Mcp/
  Taskboard.AiChat/
  Taskboard.Cloud/
  Taskboard.Workflow/
  Taskboard.Integrations/
  Taskboard.Blazor/
  Taskboard.Maui/
/tests/
  Taskboard.Tests.Unit/
  Taskboard.Tests.Integration/
/skills/
  manage-taskboard/
```

### Context files the agent must read before implementation

- `README.md`
- `.specs/CAPABILITY-MAP.md`
- `.specs/SPEC-001-domain-model.md`
- `.specs/SPEC-002-rest-api.md`
- `.specs/SPEC-011-persistence.md`

---

## 5. Task Definition

### Main task

Migrar a aplicação `agent-harness` do stack Node.js/React/SQLite para C# .NET 10, mantendo funcionalidade equivalente, contratos HTTP, CLI, MCP server e Skill.

### Subtasks

- Mapear domínio e entidades (`SPEC-001`).
- Mapear persistência e schema SQLite (`SPEC-011`).
- Mapear API REST e server HTTP (`SPEC-002`).
- Mapear CLI `taskctl` (`SPEC-003` - migrado para Spectre.Console.Cli).
- Mapear MCP server (`SPEC-004`).
- Mapear IA / chat / workflow workspaces (`SPEC-005`, `SPEC-007`).
- Mapear cloud e Jira (`SPEC-006`, `SPEC-010`).
- Mapear Web UI (`SPEC-008` - Blazor/MAUI).
- Mapear Skill de agente (`SPEC-009`).
- Definir arquitetura, testes, CI/CD e plano de implantação (este SPEC).

### Do not do

- Não modificar o código-fonte Node.js original.
- Não expandir a implementação C# além do definido nestas specs sem atualizar a spec primeiro.
- Não alterar frontend sem spec dedicada.

---

## 6. Functional Requirements

### FR-001: Compatibilidade de rotas

Todas as rotas HTTP do sistema legado devem ser replicadas no .NET 10 (ver `SPEC-002`).

### FR-002: Compatibilidade de dados

O schema SQLite final do Node.js deve ser reproduzido em EF Core 10 (ver `SPEC-011`).

### FR-003: Compatibilidade de CLI

O CLI `taskctl` .NET deve oferecer os mesmos subcomandos e saída JSON (ver `SPEC-003`).

### FR-004: Compatibilidade MCP

O servidor MCP .NET deve expor as mesmas tools e schemas JSON (ver `SPEC-004`).

### FR-005: Compatibilidade Skill

A skill `manage-taskboard` deve funcionar com o novo `taskctl` .NET e MCP (ver `SPEC-009`).

### FR-006: Autenticação e Autorização

Sistema de autenticação baseada em cookies com usuário admin configurável via variáveis de ambiente.

---

## 7. Business Rules

### BR-001: Local-first

O banco padrão é SQLite local; sincronização cloud e Jira são opcionais.

### BR-002: Não quebrar contratos

Rotas, payloads e códigos de erro HTTP devem ser mantidos ou versionados.

### BR-003: Identificadores estáveis

`TASK-{projectId}-{number}` e `JIRA:{origin}:{key}` permanecem válidos.

### BR-004: Segurança

Senhas e tokens nunca logados; credenciais armazenadas de forma segura.

### Domain invariants

- O projeto `local` (`全局`) sempre existe após inicialização.
- Identificadores de tarefas são únicos globalmente.
- Apenas tarefas arquivadas podem ser deletadas.
- Tarefas Jira não podem ser modificadas no Taskboard.

---

## 8. Domain Modeling

Ver `SPEC-001-domain-model.md` para entidades, value objects, agregados e eventos de domínio.

Resumo dos agregados:

| Aggregate | Responsibility |
|---|---|
| Project | Gerencia projetos, labels e numeração de tarefas |
| Task | Ciclo de vida, status, prioridade, versionamento |
| AiChatThread | Conversa com agente (runs e events) |

---

## 9. Expected Architecture

### Architectural style

ABP N-Layer / Clean Architecture / Modular Monolith.

### Layers

```text
Domain
  Taskboard.Domain.Shared      # Value Objects, Domain Events, Exceptions, Interfaces
  Taskboard.Domain             # Aggregates, Entities, Domain Services
Application
  Taskboard.Application.Contracts  # DTOs, Requests, Repository Interfaces, Service Interfaces
  Taskboard.Application            # Application Services, Handlers, Mapping
Infrastructure
  Taskboard.EntityFrameworkCore    # DbContext, Configurations, Repositories, Migrations
  Taskboard.Mcp                    # MCP Server
  Taskboard.Cli                    # CLI taskctl
  Taskboard.Integrations           # Jira, Cloudflare, Execution helpers
  Taskboard.Cloud                  # Cloud services
  Taskboard.AiChat                 # AI Chat services
  Taskboard.Workflow               # Workflow engine
Presentation/API
  Taskboard.Server                 # ASP.NET Core Minimal APIs, Static Files, Auth
  Taskboard.Blazor                 # Blazor Server Components
  Taskboard.Maui                   # .NET MAUI Blazor Hybrid (preparado)
Tests
  Taskboard.Tests.Unit
  Taskboard.Tests.Integration
```

### Allowed dependencies

- `Domain` não depende de nenhuma outra camada.
- `Domain.Shared` não depende de nenhuma outra camada.
- `Application.Contracts` depende de `Domain.Shared`.
- `Application` depende de `Domain` e `Application.Contracts`.
- `Infrastructure` implementa contratos do `Application` e `Application.Contracts`.
- `Server` orquestra entrada/saída.

### Forbidden dependencies

- Domain acessando banco, HTTP, filas, cache.
- Controller contendo regras de negócio.
- Handler contendo regras complexas de domínio.
- Repository validando regras de negócio.
- DTOs vazando para o domínio.

---

## 10. API Contracts

Ver `SPEC-002-rest-api.md`.

Resumo das rotas implementadas:

```http
GET    /health
POST   /api/login
POST   /api/logout
GET    /api/meta
GET    /api/client-storage
PUT    /api/client-storage
GET    /api/local/codex-thread-progress
GET    /api/local/host-runtime
GET/PUT /api/local/cloud-session
GET/POST /api/local/jira-connection
POST   /api/local/jira-connection/sync
GET/POST /api/local/ai/catalog
GET    /api/local/ai/composer/candidates
POST   /api/local/ai/composer/rebind
GET/POST /api/local/ai/threads
GET    /api/local/ai/threads/:id/events
POST   /api/local/ai/threads/:id/events
POST   /api/local/ai/threads/:id/runs
PATCH  /api/local/ai/threads/:threadId/runs/:runId
GET/PUT /api/device-workspaces
GET/PUT /api/workflow-capabilities
GET/POST /api/projects
GET    /api/projects/{id}
GET/POST /api/tasks
GET/PATCH/DELETE /api/tasks/:id
POST   /api/tasks/:id/move
POST   /api/tasks/:id/archive
POST   /api/tasks/:id/restore
GET/POST /api/tasks/:id/comments
PATCH/DELETE /api/tasks/:taskId/comments/:commentId
GET    /api/tasks/:id/activities
GET/POST /api/tasks/:id/relations
DELETE /api/tasks/:id/relations/{type}/{targetTaskId}
POST   /api/attachments
GET    /api/attachments/:id/content
GET    /api/attachments/:id/download
DELETE /api/attachments/:id
GET    /api/events (SSE)
GET    /api/local/ai/threads/:id/events (SSE)
```

---

## 11. Application Contracts

Exemplos de commands/queries:

```csharp
public sealed record CreateProjectCommand(string Id, string Name, string? WorkspacePath) : IRequest<ProjectDto>;
public sealed record ListProjectsQuery() : IRequest<IReadOnlyList<ProjectDto>>;
public sealed record CreateTaskCommand(
    string ProjectId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    IReadOnlyList<string> Labels
) : IRequest<TaskDto>;
public sealed record UpdateTaskCommand(
    string TaskId,
    long Version,
    TaskPatch Changes,
    string? ThreadId,
    ThreadBinding? ThreadBinding,
    Actor Actor
) : IRequest<TaskDto>;
public sealed record MoveTaskCommand(
    string TaskId,
    long Version,
    string Status,
    double? SortOrder,
    string? ThreadId,
    ThreadBinding? ThreadBinding,
    Actor Actor
) : IRequest<TaskDto>;
```

Handlers encapsulam regras de domínio e chamam repositórios.

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`.

Entidades persistidas:

| Table | Purpose |
|---|---|
| Projects | Projetos |
| Tasks | Tarefas |
| Comments | Comentários |
| TaskActivities | Log de mudanças |
| Attachments | Anexos |
| WorkflowWorkspaces | Config JSON do board |
| ProjectSummaries | Resumos gerados |
| AiChatThreads | Threads de IA |
| AiChatRuns | Execuções de IA |
| AiChatEvents | Eventos de IA |
| TaskRelations | Relacionamentos |

---

## 13. Integrations

Ver `SPEC-006-cloud.md`, `SPEC-010-integrations.md`.

| Service | Data sent | Data received | Security |
|---|---|---|---|
| Jira API | updates de issues | issues, comentários | OAuth/basic token armazenado em secrets |
| Cloudflare D1/R2 | dados sincronizados | dados remotos | Token de API Cloudflare |
| AI Models (OpenAI/Claude) | prompts, contexto | respostas, ações | API key, sandbox read-only/write |

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Projeto duplicado | POST /api/projects com id existente | 409 PROJECT_EXISTS |
| Tarefa não encontrada | GET /api/tasks/invalid | 404 TASK_NOT_FOUND |
| Conflito de versão | PATCH com version desatualizado | 409 VERSION_CONFLICT |
| Jira offline | sync forçado | 502 JIRA_RECONCILE_FAILED |
| Arquivo inexistente | GET /api/attachments/invalid | 404 ATTACHMENT_NOT_FOUND |
| Não autenticado | Acesso a rota protegida | 302 redirect para /login |

---

## 15. Few-Shot Examples

### Example 1: Criar projeto

**Input:**

```json
POST /api/projects
{
  "id": "my-project",
  "name": "My Project",
  "workspacePath": "/home/user/my-project"
}
```

**Expected output:**

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

### Example 2: Build and run

```bash
dotnet build src/Taskboard.Server
dotnet run --project src/Taskboard.Server
dotnet run --project src/Taskboard.Cli -- project create --id my-project --name "My" --workspace-path /abs/path
dotnet test
dotnet build /warnaserror
```

---

## 16. Non-Functional Requirements

### Performance

- P95 < 300ms para operações de leitura.
- Sync Jira < 30s para boards medianos.

### Security

- Não logar tokens, senhas ou dados pessoais.
- Validar autorização antes de acessar recursos.
- Sanitizar descrições e comentários (DOMPurify equivalente).
- Autenticação via cookies HttpOnly, SameSite=Strict.

### Observability

- Structured logs com `ILogger`.
- Métricas de sucesso, erro e latência.
- Tracing para chamadas externas.
- CorrelationId no fluxo de requisições.

### Reliability

- Cancellation tokens respeitados.
- Timeouts para chamadas externas.
- Transações para persistência.
- Idempotência em comandos de sync.

---

## 17. Mandatory Guardrails

- Do not invent requirements.
- Do not create a new architecture without justification.
- Do not modify a public contract without documenting the breaking change.
- Do not remove or ignore existing tests.
- Do not add a library without an explicit need.
- Do not place business rules in controllers.
- Do not access infrastructure directly from the domain layer.
- Do not expose secrets, tokens, personal data, or regulated data in logs.
- Do not deploy, push, or merge automatically.
- Do not modify CI/CD pipelines unless this SPEC has a dedicated section for it.
- Do not expand the scope with opportunistic improvements.
- Stop and request human review when there is critical ambiguity.

---

## 18. Expected Tests

### Unit tests

| Class | Scenarios |
|---|---|
| Project | criação, numeração, labels |
| Task | mudança de status, versionamento, prioridade |
| TaskRelation | parent, blocks, related |
| Value Objects | TaskStatus, TaskPriority, Actor, Recurrence, TaskIdentifier |

### Integration tests

| Flow | Validation |
|---|---|
| POST /api/projects | 201 Created |
| GET /api/projects | retorna lista |
| POST /api/tasks | cria com identifier correto |
| PATCH /api/tasks/:id | version conflict 409 |
| GET /api/tasks/:id/comments | retorna comentários |
| POST /api/tasks/:id/comments | cria comentário |
| GET /api/events | SSE stream funciona |

---

## 19. Acceptance Criteria

- [x] Todos os specs de migração foram criados/atualizados.
- [x] Arquitetura .NET 10 ABP N-Layer foi definida.
- [x] Contratos HTTP mapeados sem ambiguidade.
- [x] Domínio e persistência mapeados.
- [x] CLI, MCP e Skill mapeados.
- [x] Plano de testes definido.
- [x] Riscos e rollback documentados.
- [x] Build compila sem warnings.
- [x] Testes passam.

---

## 20. Implementation Plan

### Step 1: Discovery

- [x] Ler arquivos contexto.
- [x] Identificar arquitetura atual.
- [x] Extrair schema, rotas, CLI, MCP.

### Step 2: Technical design

- [x] Definir classes de domínio.
- [x] Definir commands/queries.
- [x] Definir contratos API.
- [x] Definir repositórios.
- [x] Definir migrations.

### Step 3: Implementation

- [x] Implementar domínio.
- [x] Implementar aplicação.
- [x] Implementar infraestrutura EF Core + SQLite.
- [x] Implementar HttpApi (Minimal APIs).
- [x] Implementar CLI (Spectre.Console.Cli).
- [x] Implementar MCP server.
- [x] Implementar Skill.
- [x] Implementar Autenticação/Autorização.
- [x] Implementar Blazor Server.

### Step 4: Tests

- [x] Domain tests.
- [ ] Application tests.
- [x] Integration tests.
- [ ] Contract tests.

### Step 5: Final validation

- [x] Build.
- [x] Tests.
- [ ] Review arquitetura.
- [ ] Documentação.

---

## 21. Rollback Strategy

### When to trigger rollback

- Erro 5xx aumentado.
- Quebra de contrato.
- Inconsistência de dados.

### How to revert

- Reverter branch.
- Restaurar backup do SQLite.
- Reduzir feature flag.

### Expected evidence

- Logs.
- Métricas.
- Smoke tests.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Diferença semântica entre `node:sqlite` e EF Core | Médio | Média | Mapear tipos SQL, testar migrations |
| MCP SDK .NET ainda em evolução | Médio | Baixa | Usar versão estável ou implementar server STDIO/SSE manual |
| Perda de funcionalidade de AI chat | Alto | Média | Especificar detalhadamente (`SPEC-005`) |
| CLI `taskctl` complexo | Médio | Média | Decompor em subcomandos (`SPEC-003`) |

---

## 23. Definition of Done

- [ ] SPEC revisado.
- [ ] Implementação segue o SPEC.
- [ ] Testes automatizados criados.
- [ ] Build validado.
- [ ] Contratos preservados ou versionados.
- [ ] Observabilidade implementada.
- [ ] Documentação atualizada.
- [ ] PR descreve mudanças, riscos e evidências.
- [ ] Nenhum TODO crítico no código.
- [ ] Nenhuma decisão arquitetural implícita.

---

## 24. Key Reminder

> The SPEC is the contract.  
> The agent must not optimize, expand, or reinterpret the scope.  
> In case of ambiguity, the agent must stop, make the uncertainty explicit, and propose technical options with impact, risk, and recommendation.

---

## Pending Questions and Ambiguities

1. Frontend: Blazor Server implementado; .NET MAUI preparado. Confirmar se MAUI deve ser completado.
2. Banco padrão em produção: SQLite, PostgreSQL ou SQL Server?
3. Servidor MCP: transporte padrão STDIO, SSE ou ambos?
4. Deploy automático via GitHub Actions - quando habilitar?

## Human Approval Checklist

- [ ] O problema de negócio está claro.
- [ ] O resultado esperado é mensurável.
- [ ] O escopo e fora de escopo são explícitos.
- [ ] Requisitos funcionais são testáveis.
- [ ] Regras de negócio estão completas o suficiente.
- [ ] Modelo de domínio está alinhado ao bounded context.
- [ ] Contratos API são explícitos.
- [ ] Casos de borda listados.
- [ ] Requisitos não-funcionais definidos.
- [ ] Guardrails claros.
- [ ] Testes necessários claros.
- [ ] Estratégia de rollback definida.
- [ ] Riscos e mitigações documentados.
- [ ] Ambiguidades resolvidas ou aceitas.