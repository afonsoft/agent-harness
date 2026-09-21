# SPEC-006: Cloud

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Cloud Mode |
| Product / System | agent-harness |
| Module / Bounded Context | Cloud |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-cloud-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O Taskboard suporta modo cloud: companion loopback, proxy Cloudflare D1/R2, Basic Auth e polling de revisão.

### Objective

Especificar módulo cloud em .NET 10: companion device-local, Cloudflare proxy, sync e autenticação.

### Expected outcome

- `Taskboard.Cloud` com `ICloudflareProxyService` (D1/R2).
- `CloudSessionService` em memória para sessão cloud.
- Endpoints `/api/local/cloud-session`, `/api/meta`.
- Interfaces para storage e proxy Cloudflare.

### Out of scope

- Deploy real de Cloudflare Workers/D1/R2 (configuração only).
- Implementação completa de sync (apenas interfaces).

---

## 2. Agent Role

> Senior backend engineer com experiência em proxy, sync e autenticação.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não persistir credenciais em texto plano.
- Não exponer tokens de Cloudflare.

---

## 4. Product Context

### Functional context

Modo nuvem permite compartilhamento de boards entre devices e colaboração remota.

### Technical context

- Cloudflare D1 (banco) e R2 (anexos).
- Basic Auth no companion.
- Polling de revisão a cada 2s (config em `/api/meta`).

### Relevant stack

- .NET 10
- HttpClient
- Cloudflare API (D1, R2)

---

## 5. Task Definition

### Main task

Mapear cloud companion, proxy e autenticação.

### Subtasks

- Cloud Session (em memória).
- Proxy Cloudflare D1/R2.
- Meta endpoint com config de realtime.

### Do not do

- Não implementar deploy automático.
- Não implementar sync completo (apenas interfaces).

---

## 6. Functional Requirements

### FR-001: Cloud Session

**Endpoints:**

```http
GET  /api/local/cloud-session
PUT  /api/local/cloud-session
```

**Regras:**

- GET retorna sessão ativa (`CloudSessionDto`).
- PUT atualiza sessão com `UpdateCloudSessionRequest`.
- Campos: `connected`, `companionUrl`, `username`, `projectId`.
- Armazenado em memória (`CloudSessionService`).

### FR-002: Meta

**Endpoint:** `GET /api/meta`  
**Response:** metadados do servidor incluindo `realtime:{transport:'poll',intervalMs:2000}`.

```json
{
  "name": "taskboard",
  "version": "1.0.0",
  "realtime": { "transport": "poll", "intervalMs": 2000 }
}
```

### FR-003: Cloudflare Proxy (Interfaces)

**Interfaces definidas:**

```csharp
public interface ICloudflareProxyService
{
    Task<CloudflareD1Result> ExecuteD1QueryAsync(string sql, CancellationToken ct = default);
    Task<CloudflareR2Result> UploadToR2Async(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadFromR2Async(string key, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
```

**Implementação:** `CloudflareProxyService` usa HttpClient com Bearer token.

### FR-004: Review Polling

**Descrição:**  
Configuração de polling via `/api/meta` retorna `intervalMs: 2000`. Cliente faz polling a cada 2s.

---

## 7. Business Rules

- Credenciais Cloudflare via configuração (não hardcoded).
- Basic Auth no companion (fora do escopo deste spec).
- Sync não sobrescreve local sem version conflict handling.
- Tokens nunca logados.

---

## 8. Domain Modeling

Nenhum novo; reutiliza `Project`, `Task`, `Attachment` via DTOs.

---

## 9. Expected Architecture

```text
src/Taskboard.Cloud/
  Services/
    CloudflareProxyService.cs
    ICloudflareProxyService.cs
    CloudflareD1Result.cs
    CloudflareR2Result.cs

src/Taskboard.Server/Services/
  CloudSessionService.cs
```

---

## 10. API Contracts

```http
GET  /api/local/cloud-session
PUT  /api/local/cloud-session
GET  /api/meta
```

**CloudSessionDto:**
```csharp
public sealed record CloudSessionDto(
    bool Connected,
    string? CompanionUrl,
    string? Username,
    string? ProjectId
);
```

**UpdateCloudSessionRequest:**
```csharp
public sealed record UpdateCloudSessionRequest(
    bool? Connected,
    string? CompanionUrl,
    string? Username,
    string? ProjectId
);
```

---

## 11. Application Contracts

```csharp
public sealed record UpdateCloudSessionCommand(
    string? CompanionUrl, 
    string? Username, 
    string? Password, 
    string? ProjectId
) : IRequest;

public sealed record GetCloudSessionQuery() : IRequest<CloudSessionDto>;
```

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`. Credenciais em configuração (não no banco).

---

## 13. Integrations

| Service | Data sent | Data received | Security |
|---|---|---|---|
| Cloudflare D1 | queries | rows | API token (Bearer) |
| Cloudflare R2 | files | files | API token (Bearer) |

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Cloudflare offline | timeout | `TestConnectionAsync` retorna false |
| Credenciais inválidas | 401 do Cloudflare | `ExecuteD1QueryAsync` retorna `Success=false`, `Error` |
| Proxy não configurado | chamar serviço | exception ou configuração obrigatória |

---

## 15. Few-Shot Examples

```http
PUT /api/local/cloud-session
{
  "connected": true,
  "companionUrl": "https://companion.example.com",
  "username": "user",
  "projectId": "my-project"
}
```

---

## 16. Non-Functional Requirements

- Proxy D1/R2: retry com exponential backoff.
- `TestConnectionAsync` para validação de config.
- Logs sem expor tokens.

---

## 17. Mandatory Guardrails

- Nunca logar tokens.
- Nunca commitar credenciais.
- HttpClient com `Authorization: Bearer` header.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| GET /api/meta | retorna realtime config |
| GET/PUT /api/local/cloud-session | persiste config em memória |
| CloudflareProxyService.TestConnectionAsync | true/false |
| CloudflareProxyService.ExecuteD1QueryAsync | D1Result |

---

## 19. Acceptance Criteria

- [x] Cloud session endpoints.
- [x] Meta endpoint com realtime config.
- [x] Proxy D1/R2 interfaces + implementação.
- [x] Tokens via HttpClient Authorization header.

---

## 20. Implementation Plan

1. Criar `Taskboard.Cloud` project.
2. Implementar `ICloudflareProxyService` e `CloudflareProxyService`.
3. Implementar `CloudSessionService` em `Taskboard.Server`.
4. Integrar endpoints em `Taskboard.Server`.
5. Configurar HttpClient para Cloudflare no DI.

---

## 21. Rollback Strategy

- Desabilitar cloud mode.
- Restaurar backup local.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Latência Cloudflare | Médio | Média | Cache local, sync incremental |
| Segurança de credenciais | Alto | Média | Configuração externa, não hardcoded |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Contratos claros.
- [x] Estratégia de segurança.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Cloudflare é obrigatório ou opcional? (Opcional - interfaces definidas)
2. Companion deve rodar como serviço separado ou no mesmo processo? (Fora de escopo - apenas config)

## Human Approval Checklist

- [x] Cloud session mapeado.
- [x] Proxy e interfaces claras.
- [x] Segurança de credenciais.