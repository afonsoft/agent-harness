# SPEC-005: AI Chat

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | AI Chat |
| Product / System | agent-harness |
| Module / Bounded Context | AI / Workflow |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-ai-workflow-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O Taskboard possui chat com agentes (`ai_chat_threads`, `ai_chat_runs`, `ai_chat_events`), catalog de modelos, composer candidates e rebind.

### Objective

Especificar módulo de IA chat em .NET 10: threads, runs, events, catalog e composer.

### Expected outcome

- `AiChatThread`, `AiChatRun`, `AiChatEvent` implementados.
- Catalog de modelos e composer candidates (stubs).
- SSE por thread (`/api/local/ai/threads/:id/events`).
- Abstração `ILLMProvider` com implementação `MockLLMProvider` para testes.
- Streaming de respostas via `IAsyncEnumerable<LLMStreamChunk>`.

### Out of scope

- Integração real com modelos LLM (OpenAI, Anthropic, Azure) - apenas abstração definida.
- UI de configuração de providers.

---

## 2. Agent Role

> Senior AI/Backend engineer.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não implementar streaming real de LLM sem provider definido.
- Não expor API keys.

---

## 4. Product Context

### Functional context

Agentes de IA podem conversar, executar ações e registrar eventos.

### Technical context

- Tabelas `ai_chat_*` (threads, runs, events).
- Eventos SSE por thread.
- Catalog `/api/local/ai/catalog` (em memória).
- `MockLLMProvider` para desenvolvimento/testes.
- Abstração `ILLMProvider` para futuros providers reais.

### Relevant stack

- .NET 10
- HttpClient para LLM providers (futuro)
- SSE (`text/event-stream`)
- `System.Text.Json`

---

## 5. Task Definition

### Main task

Mapear AI chat, catalog e composer.

### Subtasks

- CRUD de threads/runs/events.
- Catalog de modelos (em memória).
- Composer candidates/rebind (stubs).
- SSE por thread.
- Abstração `ILLMProvider`.

### Do not do

- Não implementar streaming real de LLM sem provider definido.

---

## 6. Functional Requirements

### FR-001: AI Chat Threads

**Description:**  
Criar, listar, obter threads de IA.

**Endpoints:**

```http
GET    /api/local/ai/threads
POST   /api/local/ai/threads
```

**Request POST:**

```json
{
  "title": "Refactor plan",
  "originProjectId": "local",
  "model": "gpt-4o",
  "reasoningEffort": "medium",
  "sandbox": "read-only"
}
```

### FR-002: AI Chat Runs

**Description:**  
Iniciar, finalizar runs com status e exit_code.

**Endpoints:**

```http
POST   /api/local/ai/threads/{id}/runs
PATCH  /api/local/ai/threads/{threadId}/runs/{runId}
```

**PATCH Request:**

```json
{
  "status": "completed",
  "exitCode": 0
}
```

### FR-003: AI Chat Events

**Description:**  
Registrar eventos de `user`, `assistant`, `activity`, `error`.

**Endpoints:**

```http
GET    /api/local/ai/threads/{id}/events
POST   /api/local/ai/threads/{id}/events
```

**SSE Endpoint:** `GET /api/local/ai/threads/{id}/events` (text/event-stream)

### FR-004: AI Catalog

**Description:**  
Listar modelos disponíveis e `reasoning_effort`.

**Endpoints:**

```http
GET    /api/local/ai/catalog
POST   /api/local/ai/catalog
```

**POST Request:**

```json
{
  "id": "gpt-4o",
  "name": "GPT-4o",
  "provider": "openai",
  "reasoningEffort": "medium"
}
```

### FR-005: Composer

**Description:**  
Retornar candidates e rebind de thread (stubs).

**Endpoints:**

```http
GET    /api/local/ai/composer/candidates
POST   /api/local/ai/composer/rebind
```

### FR-006: SSE per thread

**Description:**  
`GET /api/local/ai/threads/:id/events` emite eventos por thread com historical replay.

---

## 7. Business Rules

- Thread possui runs e events.
- Status permitidos: `idle`, `running`, `failed`.
- Sandbox: `read-only`, `workspace-write`, `danger-full-access`.
- Events imutáveis.
- Run execução assíncrona com streaming.
- Historical replay no SSE: envia eventos existentes antes de subscrever.

---

## 8. Domain Modeling

### Aggregates

| Aggregate | Responsibility | Invariants |
|---|---|---|
| AiChatThread | Gerencia runs e events | status válido |

### Entities

| Entity | Identity | Responsibility |
|---|---|---|
| AiChatThread | AiChatThreadId | Conversa |
| AiChatRun | AiChatRunId | Execução |
| AiChatEvent | AiChatEventId | Evento |

### Value Objects

| Value Object | Fields | Validations |
|---|---|---|
| Sandbox | string | `read-only`, `workspace-write`, `danger-full-access` |
| ModelRef | string Value | in catalog |
| AiChatEventRole | string | `user`, `assistant`, `activity`, `error` |
| AiChatThreadStatus | string | `idle`, `running`, `failed` |
| AiChatThreadId | string Value | <=128 chars |
| AiChatRunId | string Value | <=128 chars |
| AiChatEventId | string Value | <=128 chars |

### LLM Provider Abstraction

| Type | Purpose |
|---|---|
| ILLMProvider | Interface para providers LLM |
| LLMMessage | Mensagem (role, content, name) |
| LLMOptions | Temperature, MaxTokens, TopP, StopSequences |
| LLMResponse | Content, Usage, FinishReason |
| LLMStreamChunk | ContentDelta, IsComplete, Usage |
| LLMUsage | PromptTokens, CompletionTokens, TotalTokens |

---

## 9. Expected Architecture

`Taskboard.AiChat` module com serviços de abstração para LLM providers.

```text
src/Taskboard.AiChat/
  (projeto placeholder - lógica em Taskboard.Application/AiChat/)

src/Taskboard.Application/AiChat/
  AiChatService.cs
  MockLLMProvider.cs
  ILLMProvider.cs (em Application.Contracts)
```

---

## 10. API Contracts

```http
GET/POST /api/local/ai/threads
GET     /api/local/ai/threads/{id}/events (SSE + historical replay)
POST    /api/local/ai/threads/{id}/events
POST    /api/local/ai/threads/{id}/runs
PATCH   /api/local/ai/threads/{threadId}/runs/{runId}
GET/POST /api/local/ai/catalog
GET     /api/local/ai/composer/candidates
POST    /api/local/ai/composer/rebind
```

---

## 11. Application Contracts

```csharp
public sealed record CreateAiChatThreadRequest(
    string Title,
    string? OriginProjectId,
    string Model,
    string ReasoningEffort,
    string Sandbox
);

public sealed record AddAiChatEventRequest(
    string Role,
    string Content
);

public sealed record UpdateAiChatRunRequest(
    string Status,
    int? ExitCode
);

public sealed record AiChatModelDto(
    string Id,
    string Name,
    string Provider,
    string ReasoningEffort
);
```

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`.

Tabelas:
- `ai_chat_threads`
- `ai_chat_runs`
- `ai_chat_events`

---

## 13. Integrations

| Service | Data sent | Data received | Security |
|---|---|---|---|
| OpenAI/Claude API | prompts | completions | API key |
| MockLLMProvider | prompts | mock responses | none |

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Modelo inválido | catalog não contém | 400 |
| Thread não encontrada | GET /api/local/ai/threads/invalid | 404 |
| Run status inválido | `status` fora do enum | 400 |
| Role inválido | event com role desconhecido | 400 |
| SSE reconnect | nova conexão | historical replay + live |

---

## 15. Few-Shot Examples

```http
POST /api/local/ai/threads
{
  "title": "Refactor plan",
  "originProjectId": "local",
  "model": "gpt-4o",
  "reasoningEffort": "medium",
  "sandbox": "read-only"
}
```

SSE Response:
```
event: ai_chat.event
data: {"id":"...","threadId":"...","role":"assistant","content":"Hello","createdAt":"..."}

event: ai_chat.event
data: {"id":"...","threadId":"...","role":"assistant","content":" World","createdAt":"..."}
```

---

## 16. Non-Functional Requirements

- Latência de catalog < 100ms (em memória).
- SSE por thread com reconnect e historical replay.
- MockLLMProvider para testes sem API keys.

---

## 17. Mandatory Guardrails

- Não logar API keys.
- Não persistir prompts sensíveis sem consentimento.
- `CancellationToken` respeitado em streaming.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| POST /api/local/ai/threads | 201 |
| GET /api/local/ai/catalog | retorna modelos |
| SSE thread events | historical replay + live stream |
| MockLLMProvider.StreamAsync | yield chunks + complete |

---

## 19. Acceptance Criteria

- [x] Threads/runs/events mapeados.
- [x] Catalog e composer funcional (stubs).
- [x] SSE por thread com historical replay.
- [x] `ILLMProvider` abstração definida.
- [x] `MockLLMProvider` implementado.

---

## 20. Implementation Plan

1. Criar domain `AiChatThread`, `AiChatRun`, `AiChatEvent`.
2. Criar value objects `Sandbox`, `ModelRef`, `AiChatEventRole`, `AiChatThreadStatus`.
3. Criar `ILLMProvider` e `MockLLMProvider`.
4. Criar `AiChatService` com execução assíncrona de runs.
5. Mapear endpoints em `Taskboard.Server`.
6. Implementar SSE por thread com `IThreadEventStreamService`.
7. Configurar `MockLLMProvider` como default no DI.

---

## 21. Rollback Strategy

- Desabilitar feature flag de AI chat.
- Reverter endpoints.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| Provider não definido | Alto | Alta | Deixar abstração; mock inicial |
| Streaming SSE complexo | Médio | Média | `IAsyncEnumerable` + in-memory channel |

---

## 23. Definition of Done

- [x] SPEC revisado.
- [x] Contratos claros.
- [x] Abstração de provider.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Qual provider de LLM será suportado (OpenAI, Anthropic, Azure OpenAI)?
2. O streaming de respostas é obrigatório? (Sim, via IAsyncEnumerable)

## Human Approval Checklist

- [x] Threads/runs/events definidos.
- [x] Catalog e composer claros.
- [x] SSE por thread especificado.
- [x] Abstração LLM definida.