# SPEC-008: Frontend

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Frontend |
| Product / System | agent-harness |
| Module / Bounded Context | Presentation |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-webui-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O frontend atual usa React 19 + Vite + TypeScript com componentes para board, gantt, workflow, filtros, chat AI, comentários, anexos, etc. A decisão de curto prazo é servir a SPA React existente; a longo prazo reescrever em Blazor/.NET MAUI.

### Objective

- Fase 1: configurar `Taskboard.Server` para servir a SPA Blazor Server e estática (fallback).
- Fase 2 (futura): reescrever UI em Blazor Server / .NET MAUI Blazor Hybrid, consumindo REST API + SSE.

### Expected outcome

- Fase 1: Blazor Server app configurado e servido, fallback SPA para rotas não-API.
- Fase 2: componentes Blazor para board, cards, chat, workflow.

### Out of scope

- Reescrita completa para Blazor nesta fase (apenas infraestrutura básica).

---

## 2. Agent Role

> Frontend/ASP.NET engineer.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não modificar componentes React sem decisão explícita.
- Preservar endpoints `/api` e `/api/events`.

---

## 4. Product Context

### Functional context

A UI oferece board Kanban, filtros, gantt, editor de tarefas, chat AI, workflow visual, comentários e anexos.

### Technical context

- Blazor Server (Razor Components) em `Taskboard.Blazor`.
- Static files middleware para SPA fallback.
- Autenticação via cookies (ASP.NET Core Identity ou custom).
- SSE para eventos em tempo real.

### Relevant stack

- .NET 10
- Blazor Server (.NET 8+)
- Microsoft.AspNetCore.Authentication.*
- Static files

---

## 5. Task Definition

### Main task

Configurar Blazor Server e static files fallback.

### Subtasks

- Criar `Taskboard.Blazor` project (Razor class library).
- Configurar `Taskboard.Server` para servir Blazor e static files.
- Implementar antiforgery.
- Configurar fallback SPA para rotas não-API.
- Adicionar cliente HTTP para API.

### Do not do

- Não reescrever todos os componentes React agora.

---

## 6. Functional Requirements

### FR-001: Blazor Server Integration

**Description:**  
`Taskboard.Server` registra componentes Blazor e serve via middleware.

**Configuração:**

```csharp
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

### FR-002: Static Files + SPA Fallback

**Description:**  
Rotas não-API fazem fallback para `index.html`.

**Configuração:**

```csharp
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
```

### FR-003: Anti-Forgery

**Description:**  
Proteção CSRF para state-changing operations.

**Configuração:**

```csharp
services.AddAntiforgery();
app.UseAntiforgery();
```

### FR-004: HTTP Client

**Description:**  
Blazor app consome API REST + SSE.

**Serviço:** `TaskboardClient` em `Taskboard.Blazor/Services/TaskboardClient.cs`.

---

## 7. Business Rules

- Servidor deve suportar múltiplas requisições simultâneas (Blazor SignalR).
- Static files served de `wwwroot/`.
- Fallback apenas para rotas que não começam com `/api`.

---

## 8. Domain Modeling

Nenhum; UI não tem domínio próprio.

---

## 9. Expected Architecture

```text
src/Taskboard.Blazor/
  Services/
    TaskboardClient.cs
  Pages/
    (placeholder para futuras páginas)
  _Host.cshtml / App.razor
  (outros componentes)
```

`Taskboard.Server` referencia `Taskboard.Blazor` e configura middleware.

---

## 10. API Contracts

Blazor consome API REST + SSE de `Taskboard.Server`:

- `GET /api/projects`
- `GET /api/tasks`
- `GET /api/events` (SSE)
- etc.

---

## 11. Application Contracts

```csharp
public class TaskboardClient
{
    // HttpClient wrapper para API
    Task<List<ProjectDto>> GetProjectsAsync();
    Task<List<TaskDto>> GetTasksAsync(string? projectId);
    // ...
}
```

---

## 12. Persistence and Data

Nenhum; UI consome API.

---

## 13. Integrations

- Taskboard HTTP API
- SSE events

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| API offline | SSE connection | reconnect automático |
| Token expirado | requisição autenticada | redirect para login |
| Fallback loop | /api/* request | não faz fallback |

---

## 15. Few-Shot Examples

```csharp
// TaskboardClient usage
var tasks = await _client.GetTasksAsync("local");
// render em Blazor
@foreach (var task in tasks) {
    <div>@task.Title</div>
}
```

---

## 16. Non-Functional Requirements

- Latência de UI < 300ms (sem rede).
- SignalR connection para Blazor interactivity.
- Suporte a SSR (Server-Side Rendering) com hydration.

---

## 17. Mandatory Guardrails

- Não expor credenciais no client.
- Usar antiforgery tokens.
- Não fazer requests direto para banco.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Blazor app carrega | página inicial renderiza |
| Static files servidos | /robots.txt retorna arquivo |
| Fallback SPA | /unknown-route retorna index.html |
| Antiforgery | tokens em forms |

---

## 19. Acceptance Criteria

- [x] Blazor Server configurado.
- [x] Static files + SPA fallback.
- [x] Antiforgery configurado.
- [x] TaskboardClient implementado.

---

## 20. Implementation Plan

1. Criar `Taskboard.Blazor` Razor class library.
2. Configurar `Program.cs` do Server com Blazor middleware.
3. Configurar static files e SPA fallback.
4. Adicionar antiforgery.
5. Implementar `TaskboardClient`.
6. Criar App.razor básica.

---

## 21. Rollback Strategy

- Remover Blazor middleware.
- Servir apenas static files.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| SignalR scaling | Médio | Média | Redis backplane em produção |
| Fallback loop | Médio | Baixa | whitelist /api/* |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Blazor configurado.
- [x] SPA fallback funcional.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Quais componentes Blazor priorizar na Fase 2? (Board view, task cards, chat)
2. Usar Fluent UI ou componente custom? (Ainda não definido)

## Human Approval Checklist

- [x] Blazor infrastructure clara.
- [x] SPA fallback especificado.
- [x] Antiforgery considerado.