# SPEC-015: Agent Orchestration (CLI Agents via ACP)

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agent Orchestration |
| Product / System | agent-harness |
| Module / Bounded Context | Agents (Application.Contracts / Integrations / Server / Blazor) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feature/agent-orchestration` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-09-10 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O Kanban GitHub (`/github-board`) permite mover issues entre colunas, mas a execução do trabalho continua manual. Agentes CLI (Devin, Claude Code, Codex, OpenCode, OpenHands) instalados no servidor não são aproveitados.

### Objective

Ao mover uma issue para **In Progress** (ou Backlog/TODO), permitir selecionar um agente CLI instalado no servidor, delegar a execução em segundo plano via uma camada ACP (Agent Communication Protocol), transmitir `stdout`/`stderr` em tempo real para a aba "Logs do Agente" da tarefa e, em caso de sucesso, mover a issue automaticamente para **Review**.

### Expected outcome

- `IAgentDiscoveryService` varre o `PATH` e reporta `AgentInfo` (Available/Busy/Unavailable, versão).
- `IAgentAdapter` (Adapter Pattern) traduz `AgentExecutionRequest` em `AgentCommand` por CLI.
- `IAgentAcpClient` executa o processo (`System.Diagnostics.Process`) e emite `AgentLogMessage`.
- `IAgentOrchestrationService` (`BackgroundService` + `Channel<AgentExecutionRequest>`) gerencia fila, cancelamento, logs e auto-move para Review.
- `AgentLogHub` (SignalR) faz streaming por grupo `issueId`.
- Componentes Blazor `AgentSelectionModal`, `TaskLogTab`, `TaskDetailDialog` e `KanbanBoard` atualizado.

### Out of scope

- Transporte ACP JSON-RPC bidirecional completo (o adapter atual usa stdin/stdout de linha; o contrato `IAgentAcpClient` permite trocar o transporte).
- Persistência de logs em banco (logs ficam em memória por processo).
- Clone automático do repositório (`RepoPath` é informado pelo usuário/servidor).

---

## 2. Agent Role

> Senior .NET/Blazor engineer (SignalR, process management, hosted services).

---

## 3. Agent Autonomy Level

3

### Restrictions

- Nunca logar tokens.
- Remover variáveis `TASKBOARD_*` do ambiente do processo filho (`WithoutTaskboardEnv`).
- Não executar comandos arbitrários vindos do corpo da issue sem confirmação do usuário no modal.

---

## 4. Product Context

### Functional context

Fluxo: arrastar card → coluna In Progress/Backlog → `AgentSelectionModal` (agente, branch, escopo, instruções) → labels `in-progress` + `agent-<tipo>` no GitHub → enfileira execução → logs em tempo real → sucesso move para `review`.

### Technical context

- Blazor Server + MudBlazor.
- SignalR hub em `/agent-log-hub`.
- `Taskboard.Integrations.Execution` já possui `WithoutTaskboardEnv` e `IProcessTreeSignaler`.

### Relevant stack

- .NET 10, ASP.NET Core SignalR, `Microsoft.Extensions.Hosting` (BackgroundService), `Microsoft.AspNetCore.SignalR.Client`, Octokit.

---

## 5. Task Definition

### Main task

Orquestrar agentes CLI locais a partir do Kanban GitHub com streaming de logs.

### Subtasks

- Contratos e modelos em `Taskboard.Application.Contracts/Agents`.
- Discovery, adapter, ACP client e orchestrator em `Taskboard.Integrations/Agents`.
- Hub + broadcaster + DI em `Taskboard.Server`.
- UI em `Taskboard.Blazor/Components/GitHub`.

### Do not do

- Não persistir logs em SQLite nesta fase.
- Não implementar autenticação por agente.

---

## 6. Functional Requirements

### FR-001: Agent Discovery

`AgentDiscoveryService.DiscoverAsync()` procura `devin`, `claude`, `codex`, `opencode`, `openhands` no `PATH` (equivalente a `which`/`where`, com sufixo `.exe` no Windows). Para cada executável encontrado executa `--version` (timeout 2s) e retorna a primeira linha como `Version`. Executáveis ausentes retornam `AgentStatus.Unavailable`.

### FR-002: Agent Selection on Move

Ao soltar um card em `InProgress` ou `Backlog`, `KanbanBoard` abre `AgentSelectionModal`. Se o usuário cancela, nada é alterado. Se confirma: `UpdateIssueColumnAsync` (troca label da coluna), `AddLabelsToIssueAsync([agent-<tipo>])` (cria a label se não existir) e `EnqueueAsync(request)`.

### FR-003: ACP Adapter Layer

`IAgentAdapter.BuildCommand(request)` monta `AgentCommand(ExecutablePath, Arguments, WorkingDirectory)`. `KnownCliAgentAdapter` gera um único argumento de prompt com `Branch:`, `Scope:` e as instruções. `LocalCliAgentAcpClient` seleciona o adapter por `CanHandle(AgentType)`, inicia o processo com redirecionamento de saída e reporta `AgentLogMessage` via `IProgress<T>`.

### FR-004: Background Orchestration

`AgentOrchestrationService : BackgroundService, IAgentOrchestrationService` consome `Channel<AgentExecutionRequest>` e executa cada request em `Task.Run`, com `CancellationTokenSource` vinculado ao token de parada do host. Marca agentes como `Busy` enquanto houver execução do mesmo tipo. Em `ExitCode == 0` chama `UpdateIssueColumnAsync(InProgress → Review)`.

### FR-005: Real-time Logs

`SignalRAgentLogBroadcaster` envia `ReceiveLog(AgentLogMessage)` para o grupo `issueId`. `TaskLogTab` carrega `GetLogsAsync(issueId)` (histórico em memória), conecta ao hub, chama `SubscribeToIssue(issueId)` e renderiza console escuro com auto-scroll e cores por stream (`stdout`, `stderr`, `system`).

### FR-006: Cancel

`TaskLogTab` → `CancelAsync(issueId)` → `CancellationTokenSource.Cancel()` → `LocalCliAgentAcpClient` faz `Kill(entireProcessTree: true)` e registra mensagem `System` "Agent execution was cancelled.".

---

## 7. Business Rules

- Um agente do mesmo tipo em execução deixa o tipo `Busy` para novas seleções.
- Labels de agente seguem o padrão `agent-<tipo>` (`AgentTypeExtensions.ToLabel`).
- Falha ou cancelamento **não** move a issue de coluna.
- O processo filho herda o ambiente do servidor sem variáveis `TASKBOARD_*`.

---

## 8. Domain Modeling

```csharp
public enum AgentType { Devin, Claude, Codex, OpenCode, OpenHands }
public enum AgentStatus { Available, Busy, Unavailable }
public enum AgentLogStream { StdOut, StdErr, System }

public sealed record AgentInfo(string Name, string ExecutablePath, AgentType Type, AgentStatus Status, string? Version);
public sealed record AgentExecutionRequest(string IssueId, int IssueNumber, string RepositoryFullName, string RepoPath, string? Branch, string? Scope, string Instructions, AgentType AgentType);
public sealed record AgentLogMessage(DateTimeOffset Timestamp, string IssueId, AgentLogStream Stream, string Content);
public sealed record AgentExecutionResult(int ExitCode, bool IsSuccess);
public sealed record AgentCommand(string ExecutablePath, IReadOnlyList<string> Arguments, string WorkingDirectory);
```

---

## 9. Expected Architecture

```text
src/Taskboard.Application.Contracts/Agents/
  AgentType.cs, AgentStatus.cs, AgentLogStream.cs
  AgentInfo.cs, AgentExecutionRequest.cs, AgentLogMessage.cs, AgentExecutionResult.cs, AgentCommand.cs
  IAgentDiscoveryService.cs, IAgentAdapter.cs, IAgentAcpClient.cs
  IAgentLogBroadcaster.cs, IAgentOrchestrationService.cs, AgentTypeExtensions.cs
src/Taskboard.Integrations/Agents/
  PathSearch.cs, AgentDiscoveryService.cs, KnownCliAgentAdapter.cs
  LocalCliAgentAcpClient.cs, AgentOrchestrationService.cs
src/Taskboard.Server/Hubs/
  AgentLogHub.cs, SignalRAgentLogBroadcaster.cs
src/Taskboard.Blazor/Components/GitHub/
  AgentSelectionModal.razor, TaskLogTab.razor, TaskDetailDialog.razor, KanbanBoard.razor
```

Dependências: `Integrations → Application.Contracts`; `Server → Integrations, Blazor`; `Blazor → Application.Contracts` (nunca `Integrations → Server`; por isso `IAgentLogBroadcaster` vive em Contracts e é implementado no Server).

---

## 10. API Contracts

SignalR hub `/agent-log-hub`:

| Direction | Method | Payload |
|---|---|---|
| client → server | `SubscribeToIssue(string issueId)` | — |
| client → server | `UnsubscribeFromIssue(string issueId)` | — |
| server → client | `ReceiveLog(AgentLogMessage)` | `{ timestamp, issueId, stream, content }` |

`IGitHubService.AddLabelsToIssueAsync(repositoryFullName, issueNumber, labels)` — novo método; cria labels ausentes (cor `ededed`).

---

## 11. Testing

Testes unitários em português (`Dado_Quando_Entao`), xUnit + Shouldly + NSubstitute:

- `KnownCliAgentAdapterTests` — prompt e working directory; executável ausente lança `FileNotFoundException`.
- `AgentDiscoveryServiceTests` — `PATH` isolado com executável falso; status/versão.
- `AgentOrchestrationServiceTests` — enqueue registra log; sucesso move para Review; falha não move; cancelamento encerra com log.

---

## 12. Acceptance Criteria

- [x] `dotnet build Taskboard.sln` sem warnings (`TreatWarningsAsErrors`).
- [x] Agentes detectados no `PATH` aparecem no modal com status.
- [x] Mover para In Progress abre modal; cancelar não altera a issue.
- [x] Logs aparecem em tempo real na aba "Logs do Agente".
- [x] Cancelar encerra o processo e registra no log.
- [x] Exit code 0 move a issue para `review`.
