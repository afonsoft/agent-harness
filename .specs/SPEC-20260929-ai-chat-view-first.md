# SPEC-20260929-ai-chat-view-first

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-chat-view-first` |
| Type | `Fix / UX + Config` |
| Stack | `.NET 10 / Blazor WASM / RuntimeConfigurationService` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-ai-chat-view-first` |
| Ticket | [#386](https://github.com/afonsoft/agent-harness/issues/386) |
| Status | `Done` |
| Related | SPEC-20260929-ai-chat-capabilities, SPEC-20260929-ai-chat-rail-overlay |

## 1. User Story

**As a** usuário do AI Code
**I want** escolher primeiro a View (Chat/Terminal) e só depois a CLI, com as opções sem suporte a chat estruturado desabilitadas em View=chat, e poder habilitar o "Web CLI Agent" pelo Settings sem mexer em arquivo/env
**So that** eu não erre ao selecionar uma CLI PTY-only para chat (erro 400) e o feature flag seja ligável pela UI.

**Problem context:**

- Com a capability gate (SPEC-20260929-ai-chat-capabilities), escolher `Antigravity`/`Aider` em View=chat vira erro no servidor ("has no structured chat (ACP) support").
- `Taskboard:WebCliAgent:Enabled` só é ligável via env/appsettings — a UI retorna 404 `FEATURE_DISABLED` nos endpoints de agente (`prompt`, `queue`, `retry`, `cancel`, `events`, `model`).

## 2. Scope

- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — ordem dos selects + disable por capability.
- `src/Taskboard.Server/Services/RuntimeConfigurationService.cs` — nova entrada de catálogo.
- `tests/Taskboard.Tests.Unit/Blazor/AiChatSourceGuardTests.cs` — guard da ordem.
- `tests/` — teste da entrada de catálogo (RuntimeConfigurationService tests existentes).
- `docs/features.md` (+pt-br) + `docs/installation.md` seção WebCliAgent se existir.

## 3. Technical Context

- `AgentCliMap.SupportsAcp(type)`: OpenCode/Claude/Codex/Devin.
- `CliOption.SupportsChat` já computado em `BuildCliOptions` (host e container paths).
- `RuntimeConfigurationService.Catalog` — entradas `Editable` aparecem em Settings → Configuration com Edit/Reset; valores DB sobrevivem restart e vencem env. `config.GetValue<bool>` é lido por request → sem restart.
- `OnCfgViewChanged` atual: `view == "chat" && !SelectedCliSupportsChat → "terminal"`.

## 4. Requirements

### RF-001: View antes do CLI

- **Description:** Na command bar (`_activeThread is null`), o select **View** aparece antes do select **Agent CLI**.

### RF-002: Filtro de capability

- **Description:** Com `_cfgView == "chat"`, opções do select CLI sem `SupportsChat` aparecem `disabled` (label pode sufixar "(terminal only)"). Ao trocar View→chat com CLI atual não-ACP: auto-seleciona a primeira opção `SupportsChat && Selectable`; se nenhuma, mantém View=terminal (e o option chat fica disabled quando não há nenhuma CLI ACP).

### RF-003: Toggle WebCliAgent em Settings

- **Description:** Nova entrada `CatalogEntry` em `RuntimeConfigurationService.Catalog`: `("Taskboard:WebCliAgent:Enabled", "false", Editable: true, RequiresRestart: false, Validate: ValidateBoolean, EnvAlias: "HARNESS_WEBCLIAGENT_ENABLED")`? — não há env alias documentado; usar `EnvAlias: null`. Resultado: Settings → Configuration mostra a chave editável; `PUT /api/configuration/Taskboard:WebCliAgent:Enabled` = `true` ativa os endpoints de agente imediatamente.

### RF-004: Testes + docs

- **Description:** Guard de ordem (View antes de Agent CLI) + disable condicional; teste do catálogo (chave presente, editable, bool validate); docs: como habilitar Web CLI Agent (Settings → Configuration) + nota sobre View-first em features.

## 5. API Contract

Sem mudança estrutural — nova chave passa a ser aceita pelo `PUT /api/configuration/{key}` existente.

## 6. Acceptance Criteria

- [ ] **Given** View=chat **when** abrir select CLI **then** CLIs sem ACP aparecem disabled.
- [ ] **Given** CLI PTY-only selecionada **when** trocar para View=chat **then** troca para uma CLI ACP ou volta a terminal se nenhuma existir.
- [ ] **Given** Settings → Configuration **when** editar `Taskboard:WebCliAgent:Enabled` para `true` **then** endpoints `/api/local/ai/threads/*/prompt` deixam de retornar 404 FEATURE_DISABLED.
- [ ] **Given** build/test **then** verde.

## 7. Task Plan

- [ ] **T1 — UI:** reordenar selects + disable + auto-switch no OnCfgViewChanged.
- [ ] **T2 — Config:** catalog entry WebCliAgent.
- [ ] **T3 — Tests/docs:** guards + catálogo + features/installation.
- [ ] **T4 — Done + PR.**

## 8. Organization Guardrails

- Sem mudança de rotas; entry de catálogo respeita validadores existentes.
- Não alterar `SupportsChat` para container (já correto desde #375).
