# SPEC-20261001-chat-mcp-client: tools MCP no chat

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat MCP Client Bridge |
| Product / System | agent-harness |
| Module / Bounded Context | Integrations + Application |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-chat-ux` |
| Technical owner | afonsoft |
| Status | Done — entregue via [PR #411](https://github.com/afonsoft/agent-harness/pull/411) (merged 2026-09-30) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20261001-chat-capability-registry, SPEC-20261001-chat-ux-compact, SPEC-20260930-settings-tabs |

---

## 1. Executive Summary

### Problem

O Harness já é **servidor** MCP (`Taskboard.Mcp`) e provisiona MCP
servers **para os CLIs de agente** (`McpProvisioningService`,
`AcpMcpServerSpec` em `Program.cs:229`). Mas o provider chat não é
**cliente** MCP: tools expostas por servidores configurados (GitHub MCP,
filesystem MCP, etc.) são invisíveis ao modelo no Chat mode.

aaPanel, OpenCode e Open WebUI expõem tools MCP ao chat como tools de
função normais — descoberta no connect, namespace por servidor,
lifecycle e health visíveis.

### Objective

Adicionar um **MCP client bridge**: servidores MCP configurados são
conectados, suas tools aparecem no catálogo como `mcp:<server>/<tool>`
e são invocáveis pelo modelo dentro do confinamento existente.

### Expected outcome

- Configurar um MCP server em Settings → Chat faz suas tools aparecerem
  no catálogo (`SPEC-20261001-chat-capability-registry`) e ficarem
  utilizáveis pelo modelo.
- Cada chamada emite eventos de status "Running MCP: <server>/<tool>"
  (SPEC-20261001-chat-ux-compact).
- Servidor offline/degradado não derruba o chat — suas tools ficam
  ausentes do tool set e o erro aparece no status da capability.

### Out of scope

- MCP prompts/resources do protocolo além de `tools` nesta iteração.
- OAuth flow interativo para servidores remotos (somente
  header/static auth).
- Elicitation/sampling.

---

## 2. Agent Role

> .NET integrations engineer — ModelContextProtocol SDK, DI, SSE status
> events, testes com servidor fake.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Reutilizar o SDK `ModelContextProtocol` já referenciado (lado server);
  adicionar apenas a parte client do mesmo pacote — nenhum vendor novo.
- Credenciais de MCP servers via config/env refs — nunca serializadas
  no catálogo nem no payload do provider.
- Chamadas MCP respeitam timeout e cancellation do loop.

---

## 4. Product Context

### Technical context

- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — já define
  a especificação de servers (`AcpMcpServerSpec`: nome, transporte,
  comando/URL, args, env) usada para provisionar CLIs; a mesma fonte de
  config alimenta o client bridge — sem duplicar definições.
- `ChatToolContext` carrega workspace/provider — estendido com
  `IChatActivityReporter` (SPEC-ux-compact) para status events.
- Tool loop em `ChatService` já lida com refusal/timeout por tool.

### Relevant files

- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs`
- `src/Taskboard.Integrations/Chat/Tools/` (novo `McpToolAdapter` +
  `McpClientManager`)
- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Server/Program.cs`

---

## 5. Functional Requirements

### FR-001: Configuração de servers

Nova chave editável:

```csharp
new("Taskboard:Chat:Mcp:Enabled", "false", Editable: true, ...,
    EnvAlias: "HARNESS_CHAT_MCP_ENABLED", Validate: ValidateBoolean)
```

A lista de servers reutiliza a config já existente de provisioning
(`Taskboard:Mcp:Servers` — a fonte hoje consumida por
`McpProvisioningService`); o bridge usa somente servers com
`enabled=true`. Toggle por server individual é o `Disabled` do
capability registry (`mcp:<server>/*` não é granular por server —
o registry trata cada tool).

### FR-002: `IMcpClientManager` (Singleton)

- `ConnectAsync(ct)` — para cada server habilitado: cria transport
  (stdio `McpClientFactory.CreateAsync` ou HTTP/SSE conforme spec),
  `ListToolsAsync`, normaliza nomes → `mcp_{server}_{tool}`
  (sanitizado para `[a-z0-9_-]`, colisões recebem sufixo).
- `IReadOnlyList<McpToolInfo> Tools` — catálogo consumido pelo registry.
- `Task<ChatToolResult> CallAsync(string toolId, JsonElement args,
  ChatToolContext ctx, CancellationToken ct)`.
- Health: server que falhou no connect fica `Unhealthy` com
  `lastError`; tools ausentes do set até refresh. Retry/backoff manual
  via botão Refresh da aba.

### FR-003: `McpToolAdapter` — `IChatTool`

Cada tool MCP vira um adapter:

```csharp
Name = "mcp_github_create_issue",
Description = "[mcp:github] " + tool.Description,
ParametersJson = tool.InputSchema,
ExecuteAsync → IMcpClientManager.CallAsync
```

Timeout padrão 30s (`Taskboard:Chat:Mcp:CallTimeoutSeconds`, 5–300);
resultado truncado em 32KB como as demais tools. Erros MCP viram
`ChatToolResult` com `refused=false` + payload `{ "error": ... }` —
não exceção não tratada.

### FR-004: Status events

`CallAsync` reporta via `IChatActivityReporter`:
`running_mcp` (server, tool) no início → `completed|failed|timeout`.
O mini-terminal/chip de status renderiza "Running MCP github/get_file…"
(SPEC-ux-compact).

### FR-005: Segurança

- Tools MCP passam pelo mesmo redaction de saída (`ISecretRedactor`).
- Transporte stdio executa comandos — **somente servers já presentes na
  config de provisioning** (administrador já os definiu); o bridge não
  aceita specs arbitrários vindos do chat.
- `Env` do spec pode referenciar `env:VAR` — resolvida server-side,
  nunca exposta.

---

## 6. Business Rules

- `Taskboard:Chat:Mcp:Enabled=false` (default) → bridge inerte, zero
  conexões.
- Toggle granular por tool via registry (`mcp:<server>/<tool>` em
  `Disabled`).
- Skills/agents não herdam tools MCP automaticamente — sub-agents
  recebem toolset explícito (SPEC-delegation).

---

## 7. Expected Architecture

```
config Taskboard:Mcp:Servers ──► IMcpClientManager (connect+list)
                                      │ mcp_* tools
                                      ▼
                        IChatCapabilityRegistry ──► ChatService tool loop
                                      │
                        McpToolAdapter.CallAsync ──► MCP server
                                      │
                        IChatActivityReporter ──► SSE "running_mcp"
```

---

## 8. Edge Cases

- Server stdio morre no meio → `CallAsync` retorna erro; manager marca
  `Unhealthy` e um reconnect é tentado na próxima chamada (single retry).
- Nome de tool colide com builtin → prefixo `mcp_` torna colisão
  impossível; colisão entre servers → sufixo `_2`.
- Tool com schema inválido para OpenAI (ex.: sem `type:object`) →
  normalizada para `{"type":"object","properties":{…}}`.
- Server desabilitado no meio da sessão → chamada retorna refused
  `"capability disabled"`.

---

## 9. Non-Functional Requirements

- Connect de todos os servers ≤5s no primeiro uso (lazy: primeiro
  resolve do tool set após Enabled), paralelizado.
- Sem vazamento de processos stdio: `Dispose` no shutdown da aplicação.

---

## 10. Expected Tests

- `Dado_ServerHabilitado_Quando_Lista_Entao_ToolsComPrefixoMcp`
- `Dado_ServerOffline_Quando_Calla_Entao_ResultErroSemExcecao`
- `Dado_ToolDesabilitada_Quando_Calla_Entao_Refused`
- `Dado_McpDisabled_Quando_Resolve_Entao_ZeroToolsMcp`
- Fake stdio MCP server em testes de integração (responder
  `tools/list`/`tools/call` JSON-RPC).

---

## 11. Acceptance Criteria

1. `HARNESS_CHAT_MCP_ENABLED=true` + server configurado → tools `mcp_*`
   no catálogo e invocáveis.
2. Status "Running MCP <server>/<tool>" emitido durante a chamada.
3. Server offline não quebra o chat.
4. Credenciais de servers nunca aparecem em catálogo/payload/logs.

---

## 12. Implementation Plan

1. RED: testes do manager/adapter com fake server.
2. `IMcpClientManager` + transports + normalização.
3. `McpToolAdapter` + wiring no registry.
4. Chaves de config + seção na aba Chat (lista de servers + health).
5. GREEN + build.

---

## 13. Rollback Strategy

- `Taskboard:Chat:Mcp:Enabled=false` desliga sem deploy; revert do
  commit remove o bridge.

---

## Pending Questions

1. Servers MCP do chat = mesma lista do provisioning (reuso) ou lista
   própria `Taskboard:Chat:Mcp:Servers`?
2. HTTP remoto com auth header fixa nesta iteração, ou só stdio?

---

## Human Approval Checklist

- [ ] Reuso do spec de servers aprovado.
- [ ] Prefixo `mcp_` + granularidade de toggle aceitos.
- [ ] SDK ModelContextProtocol client aceito (mesmo pacote).
