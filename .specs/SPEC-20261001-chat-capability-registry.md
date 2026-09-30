# SPEC-20261001-chat-capability-registry: registro unificado de capabilities do chat

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Chat Capability Registry (tools / MCP / skills / delegation toggles) |
| Product / System | agent-harness |
| Module / Bounded Context | Application + Presentation (Settings → Chat) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261001-chat-ux` |
| Technical owner | afonsoft |
| Status | Draft (pending human approval) |
| Date | 2026-10-01 |
| Target agent | Devin |
| Related SPECs | SPEC-20260930-settings-tabs, SPEC-20261001-chat-mcp-client, SPEC-20261001-chat-skills-slash-commands, SPEC-20261001-chat-agent-delegation, SPEC-20261001-chat-default-mode |

---

## 1. Executive Summary

### Problem

O chat tem hoje um único toggle global (`Taskboard:Chat:Tools:Enabled`)
e 8 tools hardcoded registradas em `Program.cs`. Não existe:

- Toggle granular por tool (desligar `shell_exec` sem perder
  `web_search`).
- Categoria/origem da capability (builtin / MCP / skill / delegação).
- Descoberta dinâmica — adicionar um MCP server exige rebuild.
- Superfície de Settings que mostre o que o modelo pode invocar.

aaPanel resolve isso com um SkillManager global (scan de `SKILL.md` +
arquivo de estado enable/disable) e um registry de tools por categoria.
Open WebUI expõe toggles de tools/functions por chat. Seguimos o mesmo
modelo, sobre a infra `IChatTool` existente.

### Objective

Criar o **Chat Capability Registry**: um catálogo unificado de tudo que
o chat pode invocar — builtin tools, tools MCP, skills globais e
delegação a agentes — com:

- Enable/disable granular persistido em configuração.
- Endpoint de catálogo para a aba Chat de Settings.
- Filtro efetivo aplicado no tool loop do `ChatService`.
- Metadados por capability: `id`, `name`, `kind`, `description`,
  `enabled`, `requiresConfirmation`, `origin` (ex.: servidor MCP ou
  fonte de skill).

### Expected outcome

- Settings → Chat lista as capabilities agrupadas por `kind`, cada uma
  com toggle.
- Uma capability desabilitada nunca chega ao payload de tools do provider
  — o modelo não pode invocá-la.
- Novos MCP servers/skills aparecem no catálogo sem rebuild (discovery
  no startup + refresh manual).

### Out of scope

- Implementação do cliente MCP (SPEC-20261001-chat-mcp-client).
- Slash commands e UX do composer (SPEC-20261001-chat-skills-slash-commands).
- Delegação em si (SPEC-20261001-chat-agent-delegation).
- RBAC por usuário — toggles são globais da instância nesta iteração.

---

## 2. Agent Role

> .NET engineer — contratos, DI, configuração e testes; + Blazor para a
> seção da aba Chat.

---

## 3. Agent Autonomy Level

3

### Restrictions

- Toggles nunca sobem secrets — apenas `enabled` e metadados públicos.
- Capabilities `builtin` mantêm confinamento existente (path jail,
  risk classifier, secret redaction) — o registry não altera execução.
- Sem RBAC multi-tenant nesta iteração.

---

## 4. Product Context

### Technical context

- `src/Taskboard.Application.Contracts/Chat/IChatTool.cs` — contrato
  (`Name`, `Description`, `ParametersJson`, `ExecuteAsync`).
- `src/Taskboard.Application/Chat/ChatService.cs` — `BuildToolDefinitions`
  + tool loop (`MaxToolIterations=8`).
- `src/Taskboard.Server/Program.cs:321` — registro hardcoded das 8 tools.
- `RuntimeConfigurationService` — registry de chaves editáveis; a
  estratégia de toggles reutiliza este mecanismo.

### Relevant files

- `src/Taskboard.Application/Chat/ChatService.cs`
- `src/Taskboard.Server/Program.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` (aba `chat`)

---

## 5. Functional Requirements

### FR-001: Modelo de capability

```csharp
public enum ChatCapabilityKind { BuiltinTool, McpTool, Skill, AgentDelegation }

public sealed record ChatCapability(
    string Id,                 // "tool:shell_exec", "mcp:github/create_issue", "skill:composio-cli", "agent:run"
    ChatCapabilityKind Kind,
    string Name,
    string Description,
    bool Enabled,
    bool RequiresConfirmation, // exec|write → true na UI (informativo)
    string? Origin);           // servidor MCP / fonte de skill / null
```

`Id` é estável e usado na persistência do toggle.

### FR-002: Persistência dos toggles

- `Taskboard:Chat:Tools:Enabled` (existe) — master switch dos kinds
  `BuiltinTool|McpTool`.
- `Taskboard:Chat:Skills:Enabled` — master de `Skill`.
- `Taskboard:Chat:AgentDelegation:Enabled` — master de `AgentDelegation`.
- `Taskboard:Chat:Capabilities:Disabled` — JSON array de `Id`s
  desabilitados individualmente (ex.: `["tool:shell_exec","mcp:fs/write"]`).
  Editável via Settings; validado como JSON array de strings.

Regra efetiva: `enabled = master(kind) AND id ∉ Disabled`.

### FR-003: Registry service

`IChatCapabilityRegistry` (Scoped):

- `Task<IReadOnlyList<ChatCapability>> ListAsync(ct)` — builtin tools do
  `IReadOnlyDictionary<string,IChatTool>` + MCP tools do manager
  (SPEC-mcp-client) + skills do `ISkillDiscoveryService` + capabilities
  fixas de delegação.
- `Task<ChatToolEffectiveSet> ResolveToolSetAsync(ct)` — aplica FR-002 e
  retorna os `IChatTool` efetivos para o loop (MCP tools viram adapters
  `IChatTool`; skill/delegation aparecem como tools `use_skill`,
  `run_agent`, `task` quando habilitadas).

`ChatService` consome `ResolveToolSetAsync` no lugar do dictionary cru.

### FR-004: Endpoint de catálogo

- `GET /api/chat/capabilities` → `ChatCapability[]` (ordenado por kind,
  name). Autenticado.
- A aba Chat de Settings renderiza grupos com toggles que gravam
  `Taskboard:Chat:Capabilities:Disabled` + masters via config endpoints
  existentes.

### FR-005: Refresh

Discovery de MCP/skills roda no startup e pode ser re-executado via
botão "Refresh" na aba (re-lista sem restart — caches curtos de ≤60s).

---

## 6. Business Rules

- Capability desconhecida em `Disabled` é ignorada (stale de skill
  removida não quebra).
- `RequiresConfirmation` é informativo na aba; a confirmação real segue
  o gateway de segurança existente.
- Provider sem suporte a tools → tool set vazio, chat funciona em texto
  puro (comportamento atual preservado).

---

## 7. Expected Architecture

```
Settings → Chat            GET /api/chat/capabilities
     │                             │
     ▼                             ▼
Config endpoints ──► IChatCapabilityRegistry
                         ├─ IReadOnlyDictionary<string,IChatTool> (builtin)
                         ├─ IMcpToolCatalog (SPEC-mcp-client)
                         ├─ ISkillDiscoveryService (existente)
                         └─ delegation descriptors (SPEC-delegation)
                                   │
                                   ▼
                        ChatService.ResolveToolSetAsync → tool loop
```

---

## 8. Edge Cases

- MCP server offline → capabilities `mcp:*` listadas com `Enabled=false`
  implícito (não entram no tool set) — detalhe no SPEC-mcp-client.
- Skills desabilitadas não aparecem como tools nem no slash palette.
- `Disabled` com JSON inválido → validação rejeita no save.
- Todas as capabilities off → provider recebe `tools: null`.

---

## 9. Non-Functional Requirements

- `ListAsync` ≤200ms pós-warmup (cache de discovery 60s).
- Toggle granular sem restart (`RequiresRestart: false`).

---

## 10. Expected Tests

- `Dado_ToolDesabilitada_Quando_Resolve_Entao_AusenteDoToolSet`
- `Dado_MasterToolsOff_Quando_Resolve_Entao_SemTools`
- `Dado_DisabledJsonInvalido_Quando_Valida_Entao_Rejeita`
- `Dado_Catalogo_Quando_Get_Entao_AgrupaPorKindComOrigem`

---

## 11. Acceptance Criteria

1. Settings → Chat mostra capabilities por kind com toggles funcionais.
2. `tool:shell_exec` desabilitada some do payload de tools.
3. `Capabilities:Disabled` aceita/valida JSON e sobrevive a refresh.
4. Masters `Tools/Skills/AgentDelegation` gateiam os kinds.

---

## 12. Implementation Plan

1. RED: testes de `ResolveToolSetAsync` e validação.
2. `ChatCapability` + `ChatCapabilityRegistry` + chaves de config.
3. Endpoint `/api/chat/capabilities` + UI da aba Chat.
4. Rewire `ChatService` → registry. GREEN + build.

---

## 13. Rollback Strategy

- Reverter commit; `ChatService` volta ao dictionary direto.

---

## Pending Questions

1. `Disabled` como JSON array único ok, ou prefere uma chave por item
   (`Taskboard:Chat:Tools:shell_exec:Enabled`)?
2. Skills desabilitadas devem sumir do catálogo ou aparecer cinzas?

---

## Human Approval Checklist

- [ ] Modelo `ChatCapability`/kinds aprovado.
- [ ] Persistência via `Disabled` JSON aceita.
- [ ] Endpoint de catálogo aceito.
