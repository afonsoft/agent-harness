# SPEC-004: MCP Server

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | MCP Server |
| Product / System | agent-harness |
| Module / Bounded Context | MCP |
| Change type | Migration |
| Repository | afonsoft/agent-harness |
| Suggested branch | `devin/spec-mcp-net10` |
| Technical owner | afonsoft |
| Status | Implemented |
| Date | 2026-08-31 |
| Target agent | Devin |

---

## 1. Executive Summary

### Problem

O MCP server atual `mcp/index.mjs` expõe 13 tools via STDIO usando `@modelcontextprotocol/sdk`. Precisa de equivalente em .NET 10.

### Objective

Criar servidor MCP em C# expondo as mesmas tools, mapeando cada `CallTool` para a API HTTP interna (não spawnar CLI).

### Expected outcome

Aplicação `Taskboard.Mcp` compatível com protocolo MCP, transporte STDIO, usando SDK oficial `ModelContextProtocol`.

### Out of scope

- Ferramentas de IA generativa (ver `SPEC-005`).
- Transporte SSE (apenas STDIO implementado).

---

## 2. Agent Role

> Senior .NET engineer com experiência em protocolos e servidores MCP.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Não duplicar lógica de negócio (usar HTTP API client).
- `CallTool` deve chamar HTTP API diretamente, não spawnar CLI.

---

## 4. Product Context

### Functional context

MCP permite que IDEs (Claude, Cursor, VS Code Copilot, Devin, etc.) interajam com o Taskboard via tools padronizadas.

### Technical context

- `mcp/index.mjs` inicia servidor e delega para `taskctl` via `spawnTaskctl`.
- Tools schemas definidos em `toolSchemas`.
- Respostas JSON.

### Relevant stack

- .NET 10
- `ModelContextProtocol` SDK .NET (v0.1.0-preview+)
- `System.Text.Json`
- `Taskboard.Requests` / `Taskboard.ValueObjects`

---

## 5. Task Definition

### Main task

Implementar servidor MCP .NET com as tools existentes.

### Subtasks

- Implementar transporte STDIO.
- Mapear cada tool para HTTP API client.
- Validar inputs com schemas JSON (atributos Description).
- Retornar erros formatados como `CallToolResult` com `IsError = true`.

### Do not do

- Não duplicar lógica de negócio.

---

## 6. Functional Requirements

### FR-001: list_projects

**Description:** Lista projetos.  
**Input:** `{}` (opcional filtros).  
**Output:** `[{id,name,issueCount}]`.

### FR-002: get_project

**Input:** `{ "project_id": "local" }`.  
**Output:** `{ project }`.

### FR-003: create_project

**Input:** `{ "name", "id?", "workspace_path?" }`.  
**Output:** `{ project }`.

### FR-004: list_issues

**Input:** `{ "project_id", "status?", "limit?" }`.  
**Output:** `{ project, tasks }`.

### FR-005: get_issue

**Input:** `{ "issue_id" }`.  
**Output:** `{ task }`.

### FR-006: create_issue

**Input:** `{ "project_id", "title", "description?", "status?", "priority?", "labels?", "start_date?", "due_date?" }`.  
**Output:** `{ task }`.

### FR-007: update_issue

**Input:** `{ "issue_id", "version?", "changes" (JsonElement) }`.  
**Output:** `{ task }`.

### FR-008: move_issue

**Input:** `{ "issue_id", "status", "sort_order?" }`.  
**Output:** `{ task }`.

### FR-009: archive_issue

**Input:** `{ "issue_id" }`.  
**Output:** `{ task }`.

### FR-010: restore_issue

**Input:** `{ "issue_id" }`.  
**Output:** `{ task }`.

### FR-011: add_comment

**Input:** `{ "issue_id", "body" }`.  
**Output:** `{ comment }`.

### FR-012: upload_attachment

**Input:** `{ "issue_id", "file_path" }` (caminho local lido pelo servidor).  
**Output:** `{ attachment }`.

### FR-013: cloud_status

**Input:** `{}`.  
**Output:** `{ connected, cloudUrl? }`.

---

## 7. Business Rules

- O MCP apenas orquestra; lógica em HTTP API.
- Schemas devem ser compatíveis com MCP 2024-11-05.
- Erros retornam `CallToolResult` com `IsError = true` e `Content` com texto descritivo.
- `CallTool` chama HTTP API interna via `ITaskboardApiClient`, não spawna CLI.
- Resolução de `issue_id` aceita tanto GUID quanto identifier `TASK-{project}-{number}`.

---

## 8. Domain Modeling

Ver `SPEC-001-domain-model.md`.

---

## 9. Expected Architecture

`Taskboard.Mcp` console app. `McpServerBuilder` com `WithToolsFromAssembly()`. Injeta `ITaskboardApiClient`.

```text
src/Taskboard.Mcp/
  Program.cs
  Tools/
    TaskboardTools.cs
  Services/
    TaskboardApiClient.cs
    ITaskboardApiClient.cs
```

---

## 10. API Contracts

Tools schemas JSON compatíveis com MCP.

```json
// Request
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": { "name": "create_issue", "arguments": { "project_id": "local", "title": "Bug", "status": "todo" } }
}
```

---

## 11. Application Contracts

Reutiliza requests de `Taskboard.Requests` (`CreateTaskRequest`, `UpdateTaskRequest`, `MoveTaskRequest`, `CreateCommentRequest`, `CreateProjectRequest`).

---

## 12. Persistence and Data

Ver `SPEC-011-persistence.md`.

---

## 13. Integrations

Taskboard HTTP API.

---

## 14. Edge Cases and Error Scenarios

| Scenario | Input | Expected behavior |
|---|---|---|
| Tool inexistente | name inválido | erro protocolo MCP |
| Parâmetro faltando | create sem title | erro de validação (`IsError=true`) |
| Versão conflito | update com version antiga | retornar erro amigável |
| Arquivo não existe | upload_attachment com path inválido | 404 via API → erro formatado |
| Issue não encontrada | get_issue com id inválido | erro "Issue não encontrada" |

---

## 15. Few-Shot Examples

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "create_issue",
    "arguments": {
      "project_id": "local",
      "title": "Bug",
      "status": "todo",
      "priority": "high"
    }
  }
}
```

---

## 16. Non-Functional Requirements

- Latência de tool call < 500ms.
- Transporte STDIO padrão.
- Logs estruturados sem expor dados sensíveis (LogLevel.Trace para stderr).

---

## 17. Mandatory Guardrails

- Não duplicar lógica de negócio.
- Não expor tokens/secrets.
- Usar `CancellationToken`.
- Configurar `TASKBOARD_URL` via env var ou config.

---

## 18. Expected Tests

| Flow | Validation |
|---|---|
| list_projects | retorna local |
| create_issue via MCP | payload mapeado |
| update_issue version conflict | erro formatado |
| Transporte STDIO | mensagens JSON-RPC |
| upload_attachment | arquivo lido e enviado |

---

## 19. Acceptance Criteria

- [x] 13 tools expostas.
- [x] Schemas compatíveis MCP (atributos Description).
- [x] Erros retornam `IsError=true` com conteúdo descritivo.
- [x] CallTool chama HTTP API via `ITaskboardApiClient`.

---

## 20. Implementation Plan

1. Criar `Taskboard.Mcp` console app.
2. Adicionar `ModelContextProtocol` SDK.
3. Configurar `McpServerBuilder` com transporte STDIO (`WithStdioServerTransport()`).
4. Registrar tools com `[McpServerToolType]` e `[McpServerTool]`.
5. Implementar handlers injetando `ITaskboardApiClient`.
6. Testes de integração.

---

## 21. Rollback Strategy

- Reverter para MCP manual/STDIO.
- Manter schemas compatíveis.

---

## 22. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|---|---|---:|---|
| MCP SDK .NET instável | Médio | Média | Pin versão ou implementar STDIO manual |
| Divergência de schemas | Alto | Média | Validar contra Node.js mcp/index.mjs |

---

## 23. Definition of Done

- [x] MCP server funcional.
- [x] Tools validadas.
- [x] Tests passam.
- [x] Build compila sem warnings.

---

## 24. Key Reminder

> The SPEC is the contract.

## Pending Questions

1. Usar SDK oficial `ModelContextProtocol` .NET ou implementação manual? (Resolvido: SDK oficial)
2. Transporte padrão é STDIO ou SSE? (Resolvido: STDIO)
3. `upload_attachment` lê arquivo local do servidor MCP ou recebe base64? (Resolvido: lê arquivo local do servidor)

## Human Approval Checklist

- [x] 13 tools mapeadas.
- [x] Schemas compatíveis.
- [x] Erros tratados.