# SPEC-20260921-ai-code-chat-ux

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-code-chat-ux` |
| Type | `Feature` |
| Stack | `Blazor / .NET 10 / ACP v1 / SignalR` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | implementation branch per issue |
| Ticket | [#289](https://github.com/afonsoft/agent-harness/issues/289) |
| Status | `Done` (merged via [PR #293](https://github.com/afonsoft/agent-harness/pull/293); issue #289 closed 2026-09-22) |
| Depends on | SPEC-20260921-ai-code-thread-config (RF-005 catálogo ACP) |

## 1. User Story

**As a** usuário do AI Code
**I want** que a conversa mostre tool calls como blocos estruturados (diff inline, output de comando, cards de permissão), permita enfileirar prompts enquanto o agente trabalha, exiba o consumo de contexto/tokens e suporte fork/retry de respostas
**So that** a experiência se aproxima de um coding agent moderno (ZCode, Claude Code) em vez de um chat de texto puro — e eu mantenho controle sobre o que o agente faz.

**Análise do ZCode (o que o web/desktop dele tem e nós não):**

| Área ZCode | Arquivo(s) | Nosso estado | Adotar? |
|---|---|---|---|
| Tool-call renderers por tipo | `packages/ui/src/ToolCallBlocks/renderers/` — `EditInlineDiffContent`, `ExecuteOutput`, `ask-question`, `escalate`, `changes-group`, `agent` | `ToolCallCard` genérico (título + output bruto) | **Sim** (RF-001) |
| Fila de prompts durante o turno | `zcode-protocol-v4/input-intent.ts`, `CommandInbox` — input enfileirado com admission serial | Send bloqueado/descartado durante run | **Sim** (RF-002) |
| Medidor de contexto/custo | `chat-input-toolbar/contextUsage.tsx`, `CodingPlanContextUsage` | `usage_update` → `TokenUsage` existe, mas não renderizado no chat | **Sim** (RF-003) |
| Fork de sessão / retry | `zcode-protocol-v4/fork.ts`, `sessions-index.ts` | Sem fork; thread é linear | **Sim** (RF-004) |
| Model/mode quick-switch no composer | `modelSelection.ts`, `ThoughtLevelCycleControl` | Modelo fixo por thread | **Sim** (RF-005) |
| Plan/permission cards | `cua-permission/`, `ask-question.tsx`, `escalate.tsx` | `PermissionPromptCard` existe (Allow/Deny) | Parcial — estender com `always` e diff preview (RF-001) |
| Plugin store, CUA, mobile remote, whiteboard | `pluginStore`, `zcode-cua`, `browser-use` | — | **Não** (fora do escopo do Harness) |

## 2. Scope

**In scope:**
- RF-001: `ToolCallCard` evolui para renderers por tipo de tool: `edit`/`write` → diff inline (old/new side-paint), `execute`/`terminal` → output colapsável com exit code, `fs/read` → preview truncado, `ask-question`/`permission` → card com opções do ACP (`allow`, `allow_always`, `reject`), grupo `changes` → lista de arquivos tocados com +/- counts.
- RF-002: Composer enfileira prompt durante turno ativo (`queued` badge, FIFO, cancelável antes do admission); backend envia como próximo `session/prompt` ao fim do turno corrente.
- RF-003: Medidor de contexto/tokens no composer — `usage_update` (`used`/`size`/`cost`) do ACP → barra percentual + tooltip com números; warning ≥80%.
- RF-004: Fork/retry — ação por evento: "fork from here" cria thread nova copiando eventos até o ponto; "retry" reenvia último prompt (cancelando turno corrente se ativo).
- RF-005: Toolbar do composer com quick-switch de modelo/modo usando `configOptions`/`modes` negociados (ACP) — troca chama `session/set_config_option`/`set_mode` sem recriar o thread.
- RF-006: Output grande nunca trava o render: payloads >N chars colapsam com "show more"; markdown continua sanitizado.

**Out of scope:**
- Remote control mobile, CUA/computer-use, plugin store, whiteboard do ZCode.
- Streaming diff em tempo real (renderizamos o resultado final do tool call).
- Multi-pane workbench/git graph.

## 3. Technical Context

**Referências externas (analisadas):**
- `zai-org/ZCode` `packages/ui/src/ToolCallBlocks/` — `ToolLayout` (summary row + body colapsável), `resolveRenderer.ts` (dispatch por tool kind), `fileSummaries.ts` (agregação de arquivos).
- `zai-org/ZCode` `packages/shared/src/zcode-protocol-v4/` — `input-intent` (queue), `fork`, `delta` (streaming), `sessions-index` (retomada).
- `zai-org/ZCode` `packages/ui/src/chat-input-toolbar/` — `contextUsage`, `ThoughtLevelCycleControl`, `modelSelection`.
- ACP v1: `session/prompt`, `session/update` (`tool_call`, `tool_call_update`, `usage_update`, `plan`, `available_commands`), `session/set_config_option`, `session/set_mode`, `session/request_permission` — todos já implementados no PR #284.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — loop de mensagens/composer atual
- `src/Taskboard.Blazor/Components/AiChat/ToolCallCard.razor` + `PermissionPromptCard.razor` — base dos renderers
- `src/Taskboard.Application.Contracts/Agents/AgentEventNormalizer.cs` — envelopes normalizados (`tool_call`, `tool_output`, `usage`, `permission`)
- `src/Taskboard.Integrations/Agents/AcpSessionClient.cs` — eventos ACP → normalizados
- `src/Taskboard.Server/Services/AgentSessionManager.cs` — ciclo de vida da sessão do thread
- `src/Taskboard.Server/Services/AgentControlService.cs` — `set_config`/`set_mode`/permission reply
- `src/Taskboard.Client/wwwroot/css/site.css` — estilos `.ai-chat-*`

## 4. Functional Requirements

### RF-001 — Renderers de tool call

`ToolCallCard` ganha dispatch por `ToolKind` (do envelope normalizado): `edit`/`write` renderiza diff inline (linhas `-`/`+` coloridas, path no header); `execute`/`terminal` renderiza saída em `<pre>` colapsável com badge de exit code; `read` mostra preview de N linhas; tool desconhecido → card genérico atual (fallback). Agrupamento `changes`: tool calls consecutivos de escrita no mesmo turno agregam num card "N files changed" com lista `path (+a/-d)`.

### RF-002 — Fila de prompts

Composer nunca desabilita o input: durante turno ativo, Enviar enfileira (`queued` badge na mensagem, FIFO por thread). Ao receber `turn_complete`/`idle`, o backend despacha o próximo item como `session/prompt`. Usuário pode cancelar item enfileirado antes do dispatch. Persistência: `AiChatEvent` com `Role="queued"` para sobreviver a reload.

### RF-003 — Medidor de contexto

`usage_update` ACP (`{used, size, cost?}`) → barra no composer (`used/size %`), tooltip com valores absolutos e custo quando presente. ≥80% → warning amarelo; ≥95% → vermelho. Sem dados → barra oculta.

### RF-004 — Fork e retry

Ação "fork" em qualquer evento cria thread novo (`source: fork` no título) copiando eventos até o ponto escolhido — backend duplica `AiChatEvent` rows com novo `ThreadId`. "Retry" no último turno reenvia o último prompt do usuário; se turno ativo, `session/cancel` antes de reenviar.

### RF-005 — Quick-switch de modelo/modo

Toolbar do composer lista `configOptions` (`category:"model"`) e `modes` negociados do peer ACP; mudança chama `set_config_option`/`set_mode` via `AgentControlService` existente. Indisponível (peer não anuncia) → controles ocultos, sem erro.

### RF-006 — Boundaries de render

Payloads de tool >8k chars colapsam ("show more"); regex/markdown sanitization mantida; render de lista virtualizado não é exigido (paginação existente basta).

## 5. Non-Functional Requirements

- **NFR-001**: nenhum evento ACP novo — a SPEC consome envelopes já normalizados; se faltar `Kind` específico, estender `AgentEventNormalizer` com teste.
- **NFR-002**: fila FIFO por thread é serializada no backend — sem corrida entre `session/prompt` concorrentes.
- **NFR-003**: renderers degradam graciosamente: tool kind desconhecido → card genérico.

## 6. Acceptance Criteria

1. Tool call de edição mostra diff inline; de comando mostra output + exit code; permissão mostra card com `allow`/`always`/`reject`.
2. Enviar durante turno ativo enfileira e marca `queued`; item dispara ao fim do turno; cancelar remove da fila.
3. Barra de contexto reflete `usage_update` e muda de cor nos thresholds.
4. Fork cria thread com prefixo de eventos; retry reenvia último prompt.
5. Quick-switch aparece só quando o peer anuncia `configOptions`/`modes` e aplica sem recriar thread.
6. `dotnet build` zero warnings; testes cobrem dispatch de renderers, fila e fork.

## 7. Testing Strategy

- Unit: dispatch de renderer por `ToolKind`; agregação `changes`; parser de `usage_update` → meter; lógica FIFO da fila; fork copia prefixo correto.
- Integration: endpoints de queue/fork/retry; `set_config_option` propagado ao `AgentControlService`.
- BDD em português (`Dado_Quando_Entao`).
