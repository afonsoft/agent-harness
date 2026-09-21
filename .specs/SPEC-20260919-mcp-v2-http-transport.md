# SPEC-20260919 — MCP v2: auditoria do SDK 2.2.0 + transporte HTTP stateless no Taskboard.Server

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `mcp-v2-http-transport` |
| Type | `Feature` |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / ModelContextProtocol C# SDK 2.2.0` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260919-mcp-v2-http-transport` |
| Ticket | [#136](https://github.com/afonsoft/agent-harness/issues/136) |
| Status | `Done` — aprovada pelo usuário (2026-09-19); entregue no PR #137 e validada em produção |

Origin: pedido do usuário — analisar o uso do MCP no repo frente à
[rev. 2026-07-28 do protocolo / SDK C# v2.0](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/)
e aos [conceitos v2](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/index.html).
Auditoria confirmou que já usamos o pacote mais recente (`2.2.0`) num
servidor **stdio** — o ganho real é expor o mesmo conjunto de tools via
**HTTP stateless** dentro do `Taskboard.Server`, no mesmo modelo
URL+headers que o provisioning de MCP já escreve nos CLIs.

## 1. User Story

**As a** Harness operator running agent CLIs,
**I want** the taskboard MCP tools reachable over an authenticated,
stateless HTTP endpoint hosted by `Taskboard.Server`,
**so that** agents can connect via `url + headers` (the same provisioning
model used for the RAG "knowledge" MCP) without spawning the stdio exe,
and the server can scale like any other ASP.NET Core endpoint.

## 2. Scope

### In scope

1. **Auditoria v2** — confirmar pacote `ModelContextProtocol` na versão
   mais recente (hoje `2.2.0`, topo da série 2.x no NuGet), build livre
   de diagnósticos `MCP9004/9005/9006`, e revisar `TaskboardApiClient` /
   `TaskboardTools` quanto a APIs deprecadas.
2. **HTTP transport** — adicionar `ModelContextProtocol.AspNetCore`
   (centralizado em `Directory.Packages.props`), registrar
   `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` (assembly
   de `Taskboard.Mcp`) e `MapMcp` sob o grupo autenticado `/api`
   (path `/api/mcp`), aproveitando o default **stateless** do v2.
3. **Tools compartilhadas** — `Taskboard.Server` referencia o projeto
   `Taskboard.Mcp` (ou extrai as tools para assembly compartilhado,
   conforme menor atrito) e injeta `ITaskboardApiClient` apontando para o
   próprio host (loopback `TASKBOARD_URL`), sem duplicar implementação.
4. **Auth** — o endpoint herda a autorização do grupo `api` (cookie ou
   `X-Api-Key`), mesmo modelo das demais rotas.
5. **Docs** — `docs/api.md` + `api.pt-br.md` (endpoint MCP HTTP),
   `docs/installation.md` + `.pt-br.md` (como registrar a URL num CLI).

### Out of scope

- **MRTR** (`InputRequiredException`, `InputRequest.ForElicitation`) —
  decidido na entrevista: sem ganho claro hoje; fica como follow-up
  documentado.
- **`ModelContextProtocol.Extensions.Tasks`** (tools long-running) e
  **`Extensions.Apps`** (experimental `MCPEXP003`) — fora de escopo.
- Mudar o provisioning (`AgentMcpConfigMap`/`McpProvisioningService`)
  para registrar o próprio endpoint HTTP nos CLIs — follow-up; o
  endpoint é aditivo e pode ser registrado manualmente.
- Alterar o server stdio (`src/Taskboard.Mcp/Program.cs`) — ele
  continua existindo e funcionando como está.
- `[McpHeader]` param promotion — irrelevante sem necessidade de
  roteamento por header; stdio também não o usa.
- `.github/workflows/**`.

## 3. Technical Context

### AS-IS (evidências do repositório)

- `Directory.Packages.props:17` — `ModelContextProtocol` **2.2.0**
  (latest no NuGet: 2.0.0/2.1.0/2.2.0).
- `src/Taskboard.Mcp/Program.cs` — `Host.CreateApplicationBuilder` +
  `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`;
  `ITaskboardApiClient` singleton com `TASKBOARD_URL` (default
  `http://127.0.0.1:47823`).
- `src/Taskboard.Mcp/Tools/TaskboardTools.cs` — `[McpServerToolType]`,
  4 tools (`get_issue_history`, `list_github_issue_comments`,
  `add_github_issue_comment`, `cloud_status`), `CallToolResult` +
  helper `Error(...)`.
- `src/Taskboard.Mcp/Services/TaskboardApiClient.cs` — cliente HTTP para
  a API local.
- `src/Taskboard.Server/Program.cs` — grupo `api` autenticado; padrão
  para novos endpoints.
- `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs` — os estilos de
  entry (`Devin`/`Claude`/`OpenCode`/`Codex`/`Antigravity`/`Kimi`/
  `Qwen`/`Copilot`) são todos **url+headers** — compatível com um
  endpoint HTTP próprio.
- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` —
  provisiona o RAG "knowledge" MCP (não usa o SDK; não muda).

### TO-BE (docs oficiais v2)

- `WithHttpTransport()` + `MapMcp(path)` no ASP.NET Core;
  `HttpServerTransportOptions.Stateless` default `true` — sem
  `initialize` handshake, sem `Mcp-Session-Id`; fallback automático
  para clientes down-level.
- Pacote `ModelContextProtocol.AspNetCore` para o transporte HTTP;
  `ModelContextProtocol` (já referenciado) cobre hosting/DI/discovery.

## 4. Functional Requirements

| ID | Requirement |
|---|---|
| RF-001 | `Directory.Packages.props` adiciona `ModelContextProtocol.AspNetCore` na versão mais recente estável compatível com `ModelContextProtocol` 2.2.0 (mesma linha 2.x). |
| RF-002 | `Taskboard.Server` registra o MCP server HTTP: `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` descobrindo `TaskboardTools` (via `ProjectReference` a `Taskboard.Mcp` ou assembly compartilhado extraído — o que gerar menos churn). |
| RF-003 | `MapMcp` mapeado em `/api/mcp` dentro do grupo autenticado — anônimo → `401`; autenticado (cookie ou `X-Api-Key`) → protocolo MCP normal. |
| RF-004 | Transporte HTTP roda **stateless** (default v2 — não configurar `Stateless = false`); servidor responde tanto clientes 2026-07-28 quanto down-level via handshake legado. |
| RF-005 | `ITaskboardApiClient` é resolvível no DI do Server com BaseUrl loopback (`TASKBOARD_URL` env ou `Taskboard:BaseUrl`, default `http://127.0.0.1:47823`), reutilizando as tools sem duplicação. |
| RF-006 | O exe stdio `Taskboard.Mcp` continua compilando e funcionando inalterado (transportes coexistem). |
| RF-007 | Build sem warnings `MCP9004`/`MCP9005`/`MCP9006`; qualquer diagnóstico introduzido é tratado no PR. |
| RF-008 | Testes de integração: anônimo → 401; autenticado → `tools/list` retorna as 4 tools; `tools/call cloud_status` retorna resultado válido. |
| RF-009 | `docs/api.md`/`api.pt-br.md` documentam o endpoint; `docs/installation.md`/`installation.pt-br.md` mostram registro por URL nos CLIs (ex.: claude `mcp-config`/`agy mcp add` com `X-Api-Key`). |

## 5. API Contract

| Aspecto | Valor |
|---|---|
| Endpoint | `POST /api/mcp` (Streamable HTTP, stateless) |
| Auth | mesma do grupo `api`: cookie de sessão ou header `X-Api-Key` |
| Protocolo | MCP rev. 2026-07-28; fallback automático para clientes legados |
| Métodos | `tools/list`, `tools/call` (mínimo; demais métodos conforme SDK) |
| Erros | `401` sem auth; erros MCP conforme SDK |

Tools expostas (mesmas do stdio): `get_issue_history`,
`list_github_issue_comments`, `add_github_issue_comment`,
`cloud_status`.

## 6. Acceptance Criteria

- **AC-1** *Given* request anônima a `POST /api/mcp`, *when* recebida,
  *then* retorna `401` (não HTML/fallback).
- **AC-2** *Given* request autenticada (`X-Api-Key`), *when* chama
  `tools/list`, *then* retorna as 4 tools registradas.
- **AC-3** *Given* request autenticada, *when* chama
  `tools/call cloud_status`, *then* retorna `CallToolResult` válido via
  JSON-RPC.
- **AC-4** *Given* `dotnet build`, *when* compilado, *then* zero
  warnings `MCP9xxx` e zero erros.
- **AC-5** *Given* o exe `Taskboard.Mcp`, *when* executado via stdio,
  *then* continua servindo as mesmas tools.
- **AC-6** *Given* docs, *when* inspecionados, *then* `api.md` +
  `installation.md` (en + pt-br) cobrem o endpoint e o registro por URL.

## 7. Task Plan

| # | Task | Validação |
|---|---|---|
| T1 | `Directory.Packages.props` + `PackageReference` AspNetCore no Server | `dotnet restore` ok |
| T2 | Compartilhar tools com o Server (ProjectReference a `Taskboard.Mcp` ou extrair `Taskboard.Mcp.Tools` — escolher menor churn) | build limpo |
| T3 | DI: `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` + `ITaskboardApiClient` loopback; `MapMcp` no grupo `api` | build limpo |
| T4 | Integration tests (AC-1..AC-3) | verdes |
| T5 | Docs en + pt-br | revisão |
| T6 | Suites completas + PR + merge + deploy + validação em produção (`tools/list` autenticado) | verde |

## 8. Organization Guardrails

- Branch `feature/devin-20260919-mcp-v2-http-transport`; PR → merge →
  deploy `taskboard-server`.
- Sem mudanças em `/.github/workflows/**`.
- Endpoint herda auth existente — nunca expor MCP anônimo.
- `X-Api-Key` nunca em logs/docs reais (usar `<redacted>`).
- Escopo fechado: sem MRTR, Tasks, Apps, ou provisioning do próprio
  endpoint nesta entrega.

## 9. Definition of Done

- [x] RF-001..009 implementados.
- [x] `dotnet build` limpo; suites unit + integration verdes.
- [x] `POST /api/mcp` validado em produção (401 anônimo; `tools/list` autenticado).
- [x] Exe stdio `Taskboard.Mcp` inalterado e funcional.
- [x] Docs en + pt-br atualizados.
- [x] SPEC → `Status: Done`; PR merged; `taskboard-server` redeployed.
