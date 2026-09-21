# SPEC-010: Integrations

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Integrations |
| Product / System | agent-harness |
| Module / Bounded Context | Integrations |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-integrations-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O sistema suporta sincronização com Jira e integração Cloudflare, além de módulos compartilhados de execução de processos (`shared/*.mjs`).

### Objective

Especificar integrações externas no .NET 10: Jira sync, Cloudflare proxy e infraestrutura de execução de processos.

### Expected outcome

- `IJiraService` e `JiraService` implementados.
- `ICloudProxyService` e `ICloudStorageService` interfaces.
- `Taskboard.Integrations` project com serviços.
- Serviços de execução de processos (`CodexExecutableResolver`, `ProcessTreeSignaler`, `ExecutableCommand`).

### Out of scope

- Implementação completa de UI de configuração Jira (ver `SPEC-008`).

---

## 2. Agent Role

> Senior integration engineer (Jira REST API, Cloudflare, OAuth, process management).

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não logar tokens.
- Não commitar credenciais.

---

## 4. Product Context

### Functional context

Permitir que o Taskboard sincronize tarefas com Jira e opção de modo cloud compartilhado.

### Technical context

- Jira project ID fixo `jira-my-tasks`.
- `server/cloud-config.mjs` e handlers de proxy.
- Cloudflare D1/R2.

### Relevant stack

- .NET 10
- HttpClient
- OAuth / Basic Auth
- Cloudflare API

---

## 5. Task Definition

### Main task

Mapear integrações Jira, Cloudflare e execução de processos.

### Subtasks

- Jira sync (serviço + endpoints).
- Cloudflare proxy (D1/R2).
- Execution helpers (Codex, ProcessTree, Executable).

### Do not do

- Não implementar UI de configuração.

---

## 6. Functional Requirements

### FR-001: Jira Connection

**Description:**  
Gerenciar conexão com Jira (URL, email, project key, token).

**Endpoints:**

```http
GET  /api/local/jira-connection
POST /api/local/jira-connection
POST /api/local/jira-connection/sync
```

**Serviço:** `JiraService` implementa `IJiraService`.

### FR-002: Jira Sync

**Description:**  
Sincronizar tarefas entre Taskboard e Jira.

**Processo:**

1. Buscar issues do Jira via REST API.
2. Criar/atualizar tarefas locais (não-Jira first).
3. Mapear status: Jira → Taskboard.
4. Mapear prioridade: Jira → Taskboard.

### FR-003: Cloudflare Proxy

**Description:**  
Proxy para D1 (banco) e R2 (anexos).

**Interfaces:**

```csharp
public interface ICloudProxyService { }
public interface ICloudStorageService { }
```

Ver `SPEC-006-cloud.md`.

### FR-004: Execution Helpers

**Description:**  
Auxiliares para execução de processos (Codex, etc).

**Classes:**

```csharp
public sealed class CodexExecutableResolver : IExecutableResolver { }
public sealed class ProcessTreeSignaler : IProcessTreeSignaler { }
public sealed record ExecutableCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IDictionary<string, string>? Environment);
public sealed class WithoutTaskboardEnv : IDisposable { }
```

---

## 7. Business Rules

- Credenciais Jira nunca logadas.
- Token armazenado em memória (não persistido).
- Sync usa Basic Auth com email:token encoded.
- Jira tasks são read-only no Taskboard.

---

## 8. Domain Modeling

Nenhum; integrações são serviços de infraestrutura.

---

## 9. Expected Architecture

```text
src/Taskboard.Integrations/
  Jira/
    IJiraService.cs
    JiraService.cs
  Cloud/
    ICloudProxyService.cs
    ICloudStorageService.cs
  Execution/
    CodexExecutableResolver.cs
    ProcessTreeSignaler.cs
    IExecutableResolver.cs
    IProcessTreeSignaler.cs
    ExecutableCommand.cs
    WithoutTaskboardEnv.cs
```

---

## 10. API Contracts

```http
GET    /api/local/jira-connection
POST   /api/local/jira-connection
POST   /api/local/jira-connection/sync
```

**JiraConnectionDto:**
```csharp
public sealed record JiraConnectionDto(
    bool Connected,
    string? Url,
    string? Email,
    string? ProjectKey
);
```

**UpdateJiraConnectionRequest:**
```csharp
public sealed record UpdateJiraConnectionRequest(
    string? Url,
    string? Email,
    string? ProjectKey,
    string? Token
);
```

---

## 11. Application Contracts

```csharp
public sealed record GetJiraConnectionQuery() : IRequest<JiraConnectionDto>;
public sealed record UpdateJiraConnectionCommand(UpdateJiraConnectionRequest Request) : IRequest<JiraConnectionDto>;
public sealed record SyncJiraCommand() : IRequest<JiraSyncResultDto>;
```

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`. Credenciais não persistidas (apenas URL, email, project key).

---

## 13. Integrations

| Service | Data sent | Data received | Security |
|---|---|---|---|
| Jira REST API | Basic Auth | issues | HTTPS |
| Cloudflare D1 | SQL queries | rows | Bearer token |
| Cloudflare R2 | files | files | Bearer token |

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Credenciais inválidas | POST /api/local/jira-connection | 401, não conecta |
| Jira offline | sync forçado | 502 JIRA_RECONCILE_FAILED |
| Token expirado | sync | 401, marca como desconectado |
| Credenciais não configuradas | sync | erro amigável |

---

## 15. Few-Shot Examples

```http
POST /api/local/jira-connection
{
  "url": "https://mycompany.atlassian.net",
  "email": "user@company.com",
  "projectKey": "MYPROJ",
  "token": "abc123"
}
```

---

## 16. Non-Functional Requirements

- Retry com exponential backoff em sync.
- Timeout de 30s para operações.
- Logs estruturados sem credenciais.

---

## 17. Mandatory Guardrails

- Nunca logar tokens.
- Nunca expor credenciais em respostas.
- Basic Auth: `email:token` em base64.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| GET /api/local/jira-connection | retorna connection status |
| POST /api/local/jira-connection | atualiza credenciais |
| POST /api/local/jira-connection/sync | sincroniza tarefas |
| JiraService.TestConnectionAsync | retorna connected: true/false |

---

## 19. Acceptance Criteria

- [x] Jira service implementado.
- [x] Cloudflare interfaces definidas.
- [x] Execution helpers implementados.
- [x] Credenciais nunca logadas.

---

## 20. Implementation Plan

1. Implementar `IJiraService` e `JiraService`.
2. Adicionar endpoints em `Taskboard.Server`.
3. Criar interfaces de Cloudflare proxy (ver `SPEC-006`).
4. Implementar execution helpers.
5. Testes de integração.

---

## 21. Rollback Strategy

- Desabilitar integração.
- Manter tarefas locais.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Credenciais expostas | Alto | Baixa | Nunca logar, validar antes de enviar |
| Jira API rate limit | Médio | Média | Backoff, cache local |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Jira service implementado.
- [x] Execution helpers implementados.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Sync é manual ou automático (cron)? (Manual por agora)
2. Quais campos de Jira mapear? (status, priority, summary, description)

## Human Approval Checklist

- [x] Jira mapeado.
- [x] Credenciais seguras.
- [x] Execution helpers definidos.