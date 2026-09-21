# SPEC-013: Authentication & Authorization

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Authentication & Authorization |
| Product / System | agent-harness |
| Module / Bounded Context | Security |
| Change type | Implementation |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-auth-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O sistema precisa de autenticação e autorização para proteger endpoints e permitir uso multi-usuário.

### Objective

Implementar sistema de autenticação baseada em cookies com usuário admin configurável.

### Expected outcome

- Cookie-based authentication (HttpOnly, SameSite=Strict).
- Admin user configurável via variáveis de ambiente.
- Proteção CSRF com antiforgery tokens.
- Autorização baseada em roles/claims.

### Out of scope

- Multi-user completo com Identity (futuro).
- OAuth2/OpenID Connect (futuro).

---

## 2. Agent Role

> Senior ASP.NET Core security engineer.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não expor senhas em logs.
- Não commitar credenciais.

---

## 4. Product Context

### Functional context

Usuário admin local acessa via navegador. Agentes usam CLI/MCP com API key (via `Authorization` header).

### Technical context

- ASP.NET Core Authentication + Cookie authentication.
- Antiforgery tokens para Blazor.
- Claims para autorização.

### Relevant stack

- .NET 10
- Microsoft.AspNetCore.Authentication.*
- Microsoft.AspNetCore.Authorization.*

---

## 5. Task Definition

### Main task

Implementar autenticação e autorização.

### Subtasks

- Cookie authentication middleware.
- Admin user service.
- Antiforgery configuration.
- Authorization policies.

### Do not do

- Não implementar Identity completo.

---

## 6. Functional Requirements

### FR-001: Cookie Authentication

**Description:**  
Login via form com username/senha. Cookie HttpOnly, SameSite=Strict, 8h expiração.

**Endpoints:**

```http
POST /api/login
POST /api/logout
GET  /login
```

**Configuração:**

- Username: `AdminUser__Username` (default: "admin")
- Password: `AdminUser__Password` (default: "admin")
- Cookie: `TaskboardAuth`

### FR-002: Antiforgery

**Description:**  
Proteção CSRF para Blazor e state-changing operations.

**Configuração:**

```csharp
services.AddAntiforgery(options => 
{
    options.Cookie.Name = "TaskboardAntiforgery";
    options.Cookie.SameSite = SameSiteMode.Strict;
});
app.UseAntiforgery();
```

### FR-003: Authorization Policies

**Description:**  
Políticas de autorização baseadas em roles.

**Policies:**

- `RequireAuthenticatedUser`: usuário logado
- `RequireAdmin`: role admin

---

## 7. Business Rules

- Credenciais via variáveis de ambiente (não hardcoded).
- Cookie com `HttpOnly`, `SameSite=Strict`, `Secure` em produção.
- Tempo de expiração: 8 horas.
- Rotas `/health`, `/api/meta`, `/api/login`, `/api/logout`, `/api/events` são públicas.

---

## 8. Domain Modeling

Nenhum; segurança é infraestrutura.

---

## 9. Expected Architecture

```text
src/Taskboard.Server/
  Services/
    AdminUser.cs          # Admin user configuration
  Middleware/
    GlobalExceptionHandler.cs
  Program.cs             # Authentication configuration
```

---

## 10. API Contracts

### Login

```http
POST /api/login
Content-Type: application/x-www-form-urlencoded

username=admin&password=admin&returnUrl=/
```

Response: redirect para returnUrl ou 200 OK.

### Logout

```http
POST /api/logout
```

Response: redirect para /login.

### Protected routes

```http
GET /api/projects
```

Se não autenticado: 302 redirect para /login.

---

## 11. Application Contracts

```csharp
public class AdminUserOptions
{
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin";
}

public sealed class AdminUser
{
    public bool Validate(string username, string password);
}
```

---

## 12. Persistence and Data

Nenhum; credenciais em memória/variáveis de ambiente.

---

## 13. Integrations

Nenhuma.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Credenciais inválidas | POST /api/login com senha errada | 401 Unauthorized |
| Cookie expirado | requests após 8h | 302 redirect /login |
| CSRF attack | POST /api/tasks sem antiforgery | 400 BadRequest |
| Rotas públicas | GET /health | 200 OK |

---

## 15. Few-Shot Examples

```bash
# Configurar admin via variáveis de ambiente
export AdminUser__Username=admin
export AdminUser__Password=secretpassword
```

```csharp
// Authorization policy
app.MapGet("/api/projects", [Authorize(Policy = "RequireAuthenticatedUser")] () => { ... });
```

---

## 16. Non-Functional Requirements

- Cookie com `Secure` em produção.
- Expiração de 8 horas.
- Antiforgery tokens em todos os forms.

---

## 17. Mandatory Guardrails

- Nunca logar senhas.
- Nunca expor credenciais em respostas.
- `HttpOnly`, `SameSite=Strict`.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| Login válido | cookie setado |
| Login inválido | 401 |
| Logout | cookie removido |
| Rotas protegidas sem auth | 302 redirect |
| Antiforgery | token válido |

---

## 19. Acceptance Criteria

- [x] Cookie authentication configurado.
- [x] Admin user via config.
- [x] Antiforgery working.
- [x] Rotas públicas/públicas separation.

---

## 20. Implementation Plan

1. Configurar cookie authentication em `Program.cs`.
2. Implementar `AdminUser` service.
3. Configurar antiforgery.
4. Adicionar authorization policies.
5. Mapear endpoints login/logout.
6. Testes.

---

## 21. Rollback Strategy

- Desabilitar authentication.
- Todas as rotas ficam públicas.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Credenciais default expostas | Alto | Baixa | Exigir mudança no primeiro acesso |
| CSRF | Alto | Média | Antiforgery mandatory |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Authentication working.
- [x] Authorization working.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Usar Identity completo ou custom? (Resolvido: custom com cookie auth)
2. Token JWT para API? (Resolvido: apenas cookie para browser, API key futura)

## Human Approval Checklist

- [x] Credenciais seguras.
- [x] Antiforgery configurado.
- [x] Políticas de autorização claras.