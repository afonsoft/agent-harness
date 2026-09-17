# SPEC-20260911-acp-json-rpc

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | ACP JSON-RPC Transport |
| Product / System | taskboard-ai |
| Module / Bounded Context | Agents (Integrations) |
| Change type | Feature |
| Repository | afonsoft/taskboard-ai |
| Suggested branch | `feature/acp-json-rpc` |
| Technical owner | afonsoft |
| Status | `Done` (status corrigido — implementado e issues fechadas; reconciliado em 2026-09-17) |
| Date | 2026-09-11 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem
O adapter ACP atual (SPEC-015) usa stdin/stdout de linha; agentes que expõem JSON-RPC não podem se comunicar com mensagens estruturadas.

### Objective
Implementar um transporte ACP JSON-RPC bidirecional sobre stdin/stdout, mantendo o adapter existente.

### Expected outcome
- `JsonRpcAcpClient : IAgentAcpClient`.
- Serialização/deserialização de `JsonRpcRequest`, `JsonRpcResponse`, `JsonRpcNotification`.
- Contratos `IAgentAcpClient` continuam funcionando para linha simples.

### Out of scope
- Substituir o `LocalCliAgentAcpClient` existente.
- Autenticação por agente.

---

## 2. Agent Role

> Senior .NET engineer (process management, JSON-RPC, System.Text.Json).

---

## 3. Agent Autonomy Level

3

### Restrictions
- Nunca logar tokens.
- Não executar comandos arbitrários sem confirmação.

---

## 4. Product Context

### Functional context
Agentes compatíveis com JSON-RPC podem ser selecionados no `AgentSelectionModal` e trocam mensagens estruturadas com o servidor.

### Technical context
- .NET `System.Text.Json`.
- `System.Diagnostics.Process` redirecionado.
- Contratos de `IAgentAcpClient` em `Application.Contracts`.

### Relevant stack
- .NET 10, System.Text.Json, xUnit.

---

## 5. Task Definition

### Main task
Criar um adapter ACP que fale JSON-RPC.

### Subtasks
- Definir modelo `JsonRpcMessage`, `JsonRpcRequest`, `JsonRpcResponse`, `JsonRpcNotification`.
- Implementar `JsonRpcAcpClient` usando `IAgentAdapter` e `IAgentLogBroadcaster`.
- Mapear métodos `Initialize`, `Execute`, `Cancel` via JSON-RPC.
- Adicionar testes de serialização e integração.

### Do not do
- Não remover `LocalCliAgentAcpClient` (adapter por cli conhecido).
- Não alterar o `AgentDiscoveryService`.

---

## 6. Functional Requirements

### FR-001: Mensagens JSON-RPC
Suporte a `request` (id, method, params), `response` (id, result, error) e `notification` (method, params sem id).

### FR-002: Transporte bidirecional
`JsonRpcAcpClient` envia mensagens via `stdin` e lê respostas/notificações via `stdout`/`stderr`.

### FR-003: Adapter selection
`KnownCliAgentAdapter` (ou novo `JsonRpcAgentAdapter`) gera `AgentCommand` com `Content-Type` JSON-RPC quando o agente declarar suporte.

### FR-004: Streaming de logs
Notificações e resultados geram `AgentLogMessage` com `Stream.System`.

---

## 7. Business Rules

- Timeout de resposta para `request` configurável (padrão 30s).
- Erros JSON-RPC (`{ "error": ... }`) propagam como `AgentExecutionResult` falho.
- Cancelamento envia `$/cancelRequest`.

---

## 8. Domain Modeling

```csharp
public abstract record JsonRpcMessage(string JsonRpc = "2.0");
public sealed record JsonRpcRequest(string Id, string Method, JsonElement? Params) : JsonRpcMessage;
public sealed record JsonRpcResponse(string Id, JsonElement? Result, JsonRpcError? Error) : JsonRpcMessage;
public sealed record JsonRpcNotification(string Method, JsonElement? Params) : JsonRpcMessage;
public sealed record JsonRpcError(int Code, string Message, JsonElement? Data);
```

---

## 9. Expected Architecture

```text
src/Taskboard.Application.Contracts/Agents/
  IAgentAcpClient.cs (já existe)
src/Taskboard.Integrations/Agents/
  JsonRpcMessage.cs
  JsonRpcAcpClient.cs
  JsonRpcAgentAdapter.cs (opcional)
```

---

## 10. API Contracts

`IAgentAcpClient`:

```csharp
Task<AgentExecutionResult> ExecuteAsync(AgentCommand command, IProgress<AgentLogMessage> progress, CancellationToken cancellationToken = default);
```

Mantido igual; implementação interna decide linha ou JSON-RPC.

---

## 11. Testing

- `JsonRpcMessageTests` — serialização/deserialização round-trip.
- `JsonRpcAcpClientTests` — resposta correta, erro, cancelamento.

---

## 12. Acceptance Criteria

- [ ] `dotnet build Taskboard.sln` sem warnings (`TreatWarningsAsErrors`).
- [ ] `JsonRpcAcpClient` processa request/response sem perder mensagens.
- [ ] Erro JSON-RPC gera `AgentExecutionResult` com `IsSuccess = false`.
- [ ] Testes `Dado_Quando_Entao` passam.
