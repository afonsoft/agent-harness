# SPEC-20260921-ai-code-thread-config

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-code-thread-config` |
| Type | `Feature` |
| Stack | `Blazor / .NET 10 / ACP v1` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feat/ai-code-rename` → implementation branch per issue |
| Ticket | [#288](https://github.com/afonsoft/agent-harness/issues/288) |
| Status | `Done` (merged via [PR #292](https://github.com/afonsoft/agent-harness/pull/292); issue #288 closed 2026-09-22) |

## 1. User Story

**As a** usuário do Harness
**I want** que a página "AI Chat" se chame **AI Code** e que, ao criar uma conversa, eu escolha o repositório (dropdown do GitHub), o agent CLI e o modelo — por tier Lite/Normal/Ultra ou pelo catálogo real que o agente expõe via ACP
**So that** a conversa nasce já vinculada ao workspace certo e ao modelo certo, sem configurar nenhum "provider OpenAI-compatible" — a autenticação e o catálogo de modelos pertencem ao CLI do agente.

**Decisão de arquitetura (pergunta original do usuário):**

> Precisamos de um "Provider OpenAI-compatible" em Settings, ou podemos usar o opencode/outro CLI como agente via ACP?

**Resposta: não precisamos — usamos os CLIs via ACP.** O ZCode (`zai-org/ZCode`) mantém uma camada de providers (`packages/provider/src/config/provider-data-schema.ts`: `apiType` ∈ `openai-chat-completions | openai-responses | anthropic-messages` + `baseUrl` + `apiKey`) porque **o runtime dele é o próprio agente** — o `zcode-cli` fala direto com os endpoints de modelo. No Harness a responsabilidade é invertida: cada agent CLI (opencode, claude, codex, copilot, agy…) já encapsula provider, auth e catálogo de modelos. Após o PR #284 (ACP v1), o handshake `initialize` retorna `agentCapabilities.configOptions` — incluindo `category:"model"` com o catálogo **real e autenticado** do agente — e `session/set_config_option` troca o modelo em sessão ativa. Um provider OpenAI-compatible próprio criaria uma segunda cadeia de config/chaves divergente dos CLIs e duplicaria `MockLLMProvider` (que já cobre dev/test). Fica registrado como alternativa rejeitada; revisitar apenas se surgir requisito de chat sem nenhum CLI instalado.

## 2. Scope

**In scope:**
- RF-001: Rename UI "AI Chat" → "AI Code" (menu `NavMenu`, topbar `MainLayout`, `PageTitle`/h1 da página). Route `/ai-chat`, namespaces `AiChat.*` e DTOs **permanecem** — rename é só de label.
- RF-002: `NewThreadDialog` ganha dropdown de repositório do GitHub (`RepositoryCombobox`, mesmo componente do `RunAgentDialog`), populando `CreateAiChatThreadRequest.RepositoryFullName` (o campo já existe end-to-end: request → `AiChatThread` entity → DTO).
- RF-003: Resolução automática de workspace: quando `Mode=agent`, `RepositoryFullName` setado e `WorkspacePath` vazio → backend resolve `~/repos/<repo-name>` via `WorkspaceService.ResolveCardWorkdir`; path manual continua tendo prioridade quando informado.
- RF-004: Seletor de modelo duplo no dialog: **tier** (Lite/Normal/Ultra — resolve via `AgentCliModels.ModelFor`) **ou** modelo explícito do catálogo; opção vazia = default do CLI. Persistir a escolha no thread.
- RF-005: Catálogo de modelos em ordem de prioridade: (a) `configOptions` `category:"model"` negociados via ACP na sessão ativa; (b) probe do CLI (`IAgentModelCatalogService`); (c) tabela curada `AgentCliModels`; (d) entries custom `POST /api/local/ai/catalog`. UI marca a fonte ("reported by agent" vs "curated").
- RF-006: `AiChatThread` persiste `ModelTier` e `ModelSource` para auditoria (qual catálogo serviu a escolha).

**Out of scope:**
- Provider OpenAI-compatible em Settings (alternativa rejeitada — ver §1).
- Renomear rota `/ai-chat` ou namespaces `AiChat.*` (breaking change sem ganho).
- Mobile remote, multi-window, plugin store do ZCode.
- Mudanças no protocolo ACP (coberto pela SPEC-20260921-acp-v1-conformance).

## 3. Technical Context

**Referências externas (analisadas):**
- `zai-org/ZCode` `packages/provider/src/config/provider-data-schema.ts` — provider layer que **rejeitamos** copiar (ver §1).
- `zai-org/ZCode` `packages/ui/src/chat-input-toolbar/modelSelection.ts` — grouped model select com placeholder para modelo indisponível.
- ACP v1 `initialize` → `agentCapabilities.configOptions` / `session/set_config_option` — já implementado: `AcpPeerInfo.ConfigOptions`, `IAgentSessionClient.SetConfigOptionAsync`, `AgentControlService` action `set_config`.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/Pages/NewThreadDialog.razor` — dialog a estender (agent + model + sandbox já existem)
- `src/Taskboard.Blazor/Components/Pages/RunAgentDialog.razor` — `RepositoryCombobox` + tier picker como padrão
- `src/Taskboard.Application.Contracts/Requests/CreateAiChatThreadRequest.cs` — `RepositoryFullName` já existe
- `src/Taskboard.Domain/Entities/AiChatThread.cs` — persistência do thread
- `src/Taskboard.Application/AiChat/AiChatCatalogService.cs` — agregação do catálogo (adicionar fonte ACP)
- `src/Taskboard.Server/Program.cs` — endpoint `POST /api/ai-chat/threads` (~L690) e `ResolveCardWorkdir` (~L801)
- `src/Taskboard.Integrations/Agents/AcpPeerInfo.cs` — `ConfigOptions` negociados

**Files to create or modify:**
```text
src/Taskboard.Blazor/Layout/NavMenu.razor              [mod — RF-001]
src/Taskboard.Blazor/Layout/MainLayout.razor           [mod — RF-001]
src/Taskboard.Blazor/Components/Pages/AiChat.razor     [mod — RF-001]
src/Taskboard.Blazor/Components/Pages/NewThreadDialog.razor  [mod — RF-002/004]
src/Taskboard.Application.Contracts/Requests/CreateAiChatThreadRequest.cs [mod — RF-004 tier]
src/Taskboard.Application.Contracts/Dtos/AiChatThreadDto.cs              [mod — RF-006]
src/Taskboard.Domain/Entities/AiChatThread.cs          [mod — RF-006]
src/Taskboard.Application/AiChat/AiChatCatalogService.cs [mod — RF-005]
src/Taskboard.Server/Program.cs                       [mod — RF-003/005]
src/Taskboard.EntityFrameworkCore/Configurations/AiChatThreadConfiguration.cs [mod — RF-006]
tests/Taskboard.Tests.Unit/**                          [novos testes]
tests/Taskboard.Tests.Integration/**                   [novos testes]
docs/features.md / features.pt-br.md                   [mod]
```

## 4. Functional Requirements

### RF-001 — Rename "AI Chat" → "AI Code" ✅ (entregue no PR da SPEC)

Labels visíveis: menu lateral (`NavMenu.razor` — `aria-label`, `data-label`, `nav-label`), título da topbar (`MainLayout.razor` PageTitles), `PageTitle` e `h1` da página. Rota `/ai-chat`, ícone `ChatDots`, namespaces e tabela `AiChatThreads` inalterados. Links/bookmarks existentes continuam funcionando.

### RF-002 — Dropdown de repositório no New Thread

`NewThreadDialog` usa `RepositoryCombobox` (padrão `RunAgentDialog`) para escolher o repositório GitHub. Seleção popula `RepositoryFullName` no request. Repositório é **opcional** em `Mode=assistant`; sugerido quando `Mode=agent`.

### RF-003 — Workspace automático a partir do repo

`POST /api/ai-chat/threads`: se `Mode=agent` ∧ `WorkspacePath` vazio ∧ `RepositoryFullName` presente → `WorkspaceService.ResolveCardWorkdir(RepositoryFullName)` resolve `~/repos/<name>`. `WorkspacePath` explícito sempre vence. Falha de resolução → `400` com mensagem clara, não fallback silencioso.

### RF-004 — Tier ou modelo explícito

Dialog oferece `Model` (catálogo) e `ModelTier` (`Lite|Normal|Ultra`, default Normal). Regra: modelo explícito vence; vazio + tier → `AgentCliModels.ModelFor(agentType, tier)`; ambos vazios → CLI default. `CreateAiChatThreadRequest` ganha `ModelTier`; backend grava o nome efetivo no thread.

### RF-005 — Catálogo ACP-first

Quando a sessão ACP do thread está ativa e o peer anunciou `configOptions` com `category:"model"`, essa lista é o catálogo da conversa (fonte `acp`). Fora de sessão: probe → curated → custom (ordem atual). `AiChatModelDto` ganha `Source` (`"acp"|"probe"|"curated"|"custom"`) para a UI etiquetar. Trocar modelo em sessão ativa chama `session/set_config_option` — já suportado.

### RF-006 — Auditoria da escolha

`AiChatThread` persiste `ModelTier` e `ModelSource`. Migration EF aditiva (colunas nullable, default null) — threads antigas permanecem válidas.

## 5. Non-Functional Requirements

- **NFR-001**: nenhuma quebra de contrato — campos novos opcionais/nullable; clientes antigos continuam criando threads.
- **NFR-002**: `dotnet build` zero warnings; `dotnet format` limpo.
- **NFR-003**: toda RF com teste (unit + integração do endpoint).

## 6. Acceptance Criteria

1. Menu e página exibem "AI Code"; `/ai-chat` continua acessível.
2. Novo thread pode escolher repo via dropdown GitHub, agent CLI, tier ou modelo explícito.
3. `Mode=agent` + repo selecionado → workspace resolvido para `~/repos/<name>` sem input manual.
4. Em sessão ACP com `configOptions` de modelo, o dropdown mostra o catálogo do agente marcado como "reported by agent".
5. Threads antigas carregam normalmente após a migration.
6. Cobertura não desce abaixo do gate (≥73%).

## 7. Testing Strategy

- Unit: `AgentCliModels` tier→model resolution; `AiChatCatalogService` prioridade de fontes; `AiChatThread` novos campos.
- Integration: `POST /api/ai-chat/threads` com `RepositoryFullName` + `ModelTier`; resolução de workspace; `400` em repo inválido.
- BDD em português (`Dado_Quando_Entao`) seguindo o padrão do repo.
