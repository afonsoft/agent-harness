# SPEC-20261003-ai-code-agent-chat: Agent mode como chat com delegação de CLI

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Agent-chat — modo Agent como provider chat com delegação de CLI |
| Product / System | agent-harness |
| Module / Bounded Context | Blazor + Application + Integrations + EF |
| Change type | Feature + Bugfix |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Status | In progress |
| Date | 2026-10-03 |
| Target agent | Devin |
| Related SPECs | SPEC-20261001-chat-agent-delegation, SPEC-20260929-ai-code-provider-chat, SPEC-20261001-chat-default-mode, SPEC-20261003-ai-code-devin-layout |

---

## 1. Executive Summary

### Problem

1. **Agent mode quebrado**: `BuildSessionArguments` spawnava `devin --acp` /
   `codex --acp` — mas ACP é **subcomando** nesses CLIs (`devin acp`,
   `codex acp`). O processo morria com `unexpected argument '--acp'` antes do
   handshake (`ProcessDied`). O mesmo valia para `claude --acp` — que não
   existe; ACP no Claude Code exige o binário separado `claude-code-acp`.
2. **Chat sem scroll automático**: `ScrollToEndAsync` do ProviderChat só
   chamava o highlight de sintaxe — nunca scrollava.
3. **Arquitetura**: o usuário não quer ACP — o modo Agent deve ser o próprio
   provider chat onde o assistant delega execuções aos CLIs como tools
   (`run_agent`/`run_cli` — exec argv não-interativo, output no card da tool
   como mini-terminal), não uma sessão interativa.

### Objective

- Remover o modo Assistant da UI. Dois modos: **Chat** (provider chat) e
  **Agent** (provider chat com barra de contexto de agente).
- A conversa persiste `AgentCli`/`RepositoryFullName`/`WorkspacePath`/
  `AgentModel`; `ChatToolContext` carrega `DefaultAgentCli`/`DefaultAgentModel`;
  `run_agent` e `run_cli` usam esses defaults quando o modelo omite os args.
- Corrigir argv ACP (legado) e scroll.

---

## 2. Requirements

### RF-001 — ACP argv por CLI

`BuildSessionArguments`: `devin`/`codex`/`opencode` → subcomando `acp`;
demais tipos mantêm a forma anterior. `AgentCliMap.SupportsAcp(Claude)`
passa a `false` (sem ACP nativo — só via bridge `claude-code-acp`).

### RF-002 — Agent-bar na conversa

`ChatAgentContext { AgentCli, RepositoryFullName, WorkspacePath, AgentModel }`
persiste em `ChatConversations` (migration `AddChatConversationAgentContext`,
4 colunas TEXT nullable). `Create`/`PATCH /api/local/chat/conversations`
aceitam `agent`; PATCH substitui o bloco inteiro (all-null limpa).

### RF-003 — Defaults delegados

`ExecuteToolCallAsync` monta `ChatToolContext` com
`WorkspacePath` = conversa.WorkspacePath ?? `ResolveCardWorkdir(RepositoryFullName)`,
`DefaultAgentCli`/`DefaultAgentModel` da conversa. `run_agent` sem `agent`
usa `DefaultAgentCli`; `ResolvedModelName`/`OmitModelFlag` derivam de
`DefaultAgentModel`. `run_cli` sem `cli` mapeia `DefaultAgentCli`
(AgentType → binário via `AgentCliMap`).

### RF-004 — UI: dois modos

`AiChat.razor`: botões Chat | Agent. Com `_activeThread` nulo e modo agent,
renderiza `ProviderChat` + footer com os pickers (view/CLI/docker/repo/model).
Mudança nos pickers → PATCH do `agent` da conversa aberta
(`OnParametersSetAsync` com comparação por valor do record).
`/agent` no provider chat carrega a conversa aberta para o modo Agent
(`SeedConversationId` + `OnSwitchToAgent(string?)`). "+" em modo Agent
remonta o ProviderChat (`@key`) — nenhuma thread legada é criada. Threads
`assistant`/`agent` antigas continuam abrindo pelo rail (pane legado
inalterado). `DefaultModePolicy` não resolve mais `"assistant"` → `"agent"`.

### RF-005 — Scroll stick-to-bottom

`taskboardChat.highlight` faz bind de um listener por container: segue o fim
enquanto o usuário está no fim (tolerância 24px) e solta quando ele sobe.

### RF-006 — Mini-terminal

O output das tools já renderiza em `chat-mini-terminal` (ProviderChat tool
card) — requisito cumprido pelo renderer existente.

## 3. Out of scope

- Sandbox/docker para runs delegados (argv templates já carregam suas flags;
  `ContainerContext` continua só nas threads legadas).
- Tool `question`/`plan`/`lsp`/`apply_patch` do opencode — análise entregue,
  implementação em SPEC futura.
