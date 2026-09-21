# SPEC-20260911-persist-agent-logs

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Persist Agent Logs |
| Product / System | agent-harness |
| Module / Bounded Context | Agents (Persistence) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/persist-agent-logs` |
| Technical owner | afonsoft |
| Status | `Done` (status corrigido — implementado e issues fechadas; reconciliado em 2026-09-17) |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem
`AgentLogMessage` do SPEC-015 ficou em memória por processo; ao reiniciar o servidor os logs se perdem.

### Objective
Persistir `AgentLogMessage` em SQLite/EF Core sem alterar o contrato de streaming em tempo real.

### Expected outcome
- Entidade/mapeamento EF Core para `AgentLogMessage`.
- Repositório `IAgentLogRepository` com `AppendAsync` e `GetByIssueIdAsync`.
- `AgentOrchestrationService` persiste logs ao mesmo tempo que emite `IAgentLogBroadcaster`.
- `TaskLogTab` carrega histórico persistido ao conectar.

### Out of scope
- Mudanças na UI do console (salvaguardar aparência).
- Alteração do transporte ACP (ainda stdin/stdout).

---

## 2. Agent Role

> Senior .NET engineer (EF Core, repositories, ABP patterns).

---

## 3. Agent Autonomy Level

3

### Restrictions
- Nunca logar tokens.
- Não alterar `AgentLogMessage` de forma a quebrar o `SignalR` existente.

---

## 4. Product Context

### Functional context
Logs de agente são emitidos em tempo real e, a partir desta feature, ficam disponíveis após restart.

### Technical context
- EF Core SQLite (`TaskboardDbContext`).
- Repositórios ABP (`IRepository<T>` ou custom).

### Relevant stack
- .NET 10, EF Core 10, ABP N-Layer.

---

## 5. Task Definition

### Main task
Tornar os logs de agente persistentes.

### Subtasks
- Mapear entidade `AgentLog` ou adaptar `AgentLogMessage` para persistência.
- Criar `IAgentLogRepository` e implementação `EfCoreAgentLogRepository`.
- Atualizar `AgentOrchestrationService` para `AppendAsync` após emitir log.
- Atualizar `TaskLogTab` para `GetByIssueIdAsync` antes de conectar ao hub.

### Do not do
- Não substituir o streaming em memória (ainda usado para realtime).
- Não persistir tokens ou comandos sensíveis.

---

## 6. Functional Requirements

### FR-001: Mapeamento EF Core
Criar entidade/mapeamento para `AgentLogMessage` com `Id`, `Timestamp`, `IssueId`, `Stream`, `Content`.

### FR-002: Repositório
`IAgentLogRepository.AppendAsync(AgentLogMessage log)` e `IAgentLogRepository.GetByIssueIdAsync(string issueId)`.

### FR-003: Persistência no serviço
`AgentOrchestrationService` chama `AppendAsync` após notificar `IAgentLogBroadcaster`.

### FR-004: Carregamento de histórico
`TaskLogTab` carrega histórico persistido e depois se conecta ao `SignalR` para novos logs.

---

## 7. Business Rules

- Logs persistidos mantêm a ordem cronológica (`Timestamp`).
- Apenas logs de `stdout`/`stderr`/`system` persistem; comandos internos nunca persistem.

---

## 8. Domain Modeling

```csharp
public sealed class AgentLog : Entity<long>
{
    public DateTimeOffset Timestamp { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public AgentLogStream Stream { get; set; }
    public string Content { get; set; } = string.Empty;
}
```

---

## 9. Expected Architecture

```text
src/Taskboard.Domain/Agents/
  AgentLog.cs
src/Taskboard.Application.Contracts/Agents/
  IAgentLogRepository.cs
src/Taskboard.EntityFrameworkCore/Agents/
  AgentLogConfiguration.cs
  EfCoreAgentLogRepository.cs
src/Taskboard.Server/Agents/
  (DI registration)
```

---

## 10. API Contracts

`IAgentLogRepository`:

```csharp
Task AppendAsync(AgentLogMessage log, CancellationToken cancellationToken = default);
Task<IReadOnlyList<AgentLogMessage>> GetByIssueIdAsync(string issueId, CancellationToken cancellationToken = default);
```

---

## 11. Testing

- `AgentLogRepositoryTests` — inserir e recuperar logs por `IssueId`.
- `AgentOrchestrationServiceTests` — verifica chamada a `AppendAsync` após log.

---

## 12. Acceptance Criteria

- [ ] `dotnet build Taskboard.sln` sem warnings (`TreatWarningsAsErrors`).
- [ ] Logs inseridos podem ser lidos após restart do servidor.
- [ ] `GetByIssueIdAsync` retorna logs na ordem cronológica.
- [ ] Testes `Dado_Quando_Entao` passam.
