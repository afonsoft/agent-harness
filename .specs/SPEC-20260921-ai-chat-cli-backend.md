# SPEC-20260921-ai-chat-cli-backend

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-chat-cli-backend` |
| Type | `Feature` (com correção de UX) |
| Stack | `.NET 10 / Blazor WASM / SSE / ACP` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-ai-chat-cli-backend` |
| Ticket | [#270](https://github.com/afonsoft/agent-harness/issues/270) |
| Status | `Done` |

## 1. User Story

**As a** usuário do Harness
**I want** que o AI Chat use os agentes CLI já instalados e autenticados como backend de LLM — com o dropdown de modelos refletindo os modelos reais da CLI escolhida
**So that** eu não precise configurar nenhum "provider OpenAI-compatible" em Settings, e a resposta do chat seja real (não um mock).

**Problem context:**
Respostas às perguntas levantadas, com evidências de código:

1. **De onde vêm os modelos hoje?** `GET /api/local/ai/catalog` → `AiCatalogService` (`src/Taskboard.Server/Services/AiCatalogService.cs`): 4 modelos **hardcoded** (`gpt-4o`, `gpt-4o-mini`, `claude-sonnet-4`, `claude-haiku-4`) + adições em memória via `POST /api/local/ai/catalog` (perdidas no restart). Não têm relação com o que está instalado/autenticado na máquina.
2. **Qual agent/LLM executa?** Depende do `Mode` da thread:
   - `assistant` → `AiChatService.ExecuteRunAsync` → `ILLMProvider` registrado como **`MockLLMProvider`** (`Program.cs:132`) — resposta fake `"Mock streaming response to: …"`. **Não existe provider real** (nenhum OpenAI/Anthropic/OpenAI-compatible implementado, nenhuma config em Settings para isso).
   - `agent` → `AgentSessionManager` → `AcpSessionClient`/`KnownCliAgentAdapter` — sessão interativa real com o CLI (SPEC-20260919-web-cli-agent), gated por `Taskboard:WebCliAgent:Enabled`.
3. **O dropdown de modelo ignora o agente:** `NewThreadDialog` mostra o catálogo fake para ambos os modos; em modo `agent` o modelo sequer é repassado à sessão/CLI (thread guarda `ModelRef`, mas o spawn usa o default da CLI ou o tier configurado em `AgentModelConfigService`).
4. **Já existe catálogo real por CLI:** `GET /api/agents/{agentType}/models/available` (`AgentModelCatalogService` — probe `opencode models`, `devin models list`, `agy models`, cache 5min) + `AgentCliModels.Catalog` (curado) + overrides por tier (`GET/PUT /api/agents/{agentType}/models`). O AI Chat não consome nada disso.

## 2. Scope

**In scope:**
- Catálogo do AI Chat alimentado pelas CLIs reais: por agente elegível (instalado + autenticado + habilitado), modelos = probe (`models/available`) ∪ catálogo curado (`AgentCliModels.Catalog`) ∪ overrides de tier salvos — deduplicado.
- `NewThreadDialog`: seleção de **Agent CLI** primeiro (somente elegíveis), depois **Model** daquela CLI; em modo `assistant` o modelo também é por-agente.
- Execução do modo `assistant` por CLI real: agente com `SupportsInteractiveSession` usa o caminho de sessão (`AgentSessionManager`, reusa streaming/permissions/cancel); sem suporte, fallback one-shot `IAgentAcpClient.ExecuteAsync` (já existe como RF-008 do web-cli-agent).
- `AiChatThread` em modo `assistant` passa a registrar `AgentType`; header da thread mostra `agent · model`.
- `AiCatalogService`: remover defaults hardcoded; vira agregador por-agente (ou é substituído por endpoint novo — ver RF-001). `MockLLMProvider` permanece apenas para testes/dev atrás de flag.
- Migração/compat: threads `assistant` antigas sem `AgentType` seguem legíveis (read-only: eventos/SSE/listagem), mas novo envio exige re-cadastro do agente ou é bloqueado com mensagem clara.

**Out of scope:**
- Provider OpenAI-compatible (base URL + API key) em Settings — **explicitamente descartado**; a decisão é CLI-as-backend.
- Agent loop in-process .NET com tools próprias (fase 2 já anotada em SPEC-20260919).
- Edição de tier/modelo por CLI (já existe em `/agents` → `AgentModelConfigDialog`).
- Binding de workspace para modo `assistant` (chat permanece sem workdir; cwd = workspace root).
- Multi-usuário/compartilhamento de threads.

## 3. Technical Context

**Where the change happens:**
- `Taskboard.Server`: `AiCatalogService` (agregação por agente), `Program.cs` (`local/ai/catalog`, `local/ai/threads`, run/prompt routing assistant→CLI), novo `CliChatRunner` ou extensão do `AgentSessionManager` para threads assistant sem workdir.
- `Taskboard.Application`: `AiChatService.ExecuteRunAsync` — despacha para o backend CLI em vez de `ILLMProvider` quando `AgentType` presente; mantém `ILLMProvider` só quando flag de mock/dev.
- `Taskboard.Application.Contracts`: `AiChatModelDto` + `AgentType`; `CreateAiChatThreadRequest` — `AgentType` obrigatório em ambos os modos; novo DTO de catálogo por agente.
- `Taskboard.Blazor`: `NewThreadDialog` (agent picker → model picker dinâmico), `AiChat.razor` (header `agent · model`, empty states).
- `Taskboard.Integrations`: `AgentModelCatalogService` (reuso), `AgentCliModels` (reuso), adapters (`SupportsInteractiveSession`, `BuildSessionCommand` — `model` passa a ser passado via flag `AgentCliModels.ModelFlag` quando setado).

**Files to read before implementing:**
- `src/Taskboard.Server/Services/AiCatalogService.cs`, `src/Taskboard.Server/Services/AgentSessionManager.cs`, `src/Taskboard.Server/Program.cs` (1066-1247, 1730-1808)
- `src/Taskboard.Application/AiChat/AiChatService.cs`, `src/Taskboard.Application/AiChat/MockLLMProvider.cs`
- `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs`, `Dtos/AiChatThreadDto.cs`, `Dtos/AiChatModelDto.cs`, `Requests/CreateAiChatThreadRequest.cs`
- `src/Taskboard.Application.Contracts/Agents/{AgentCliModels,IAgentModelCatalogService,IAgentModelConfigService,AgentModelConfig,AgentInfo,IAgentEligibilityService}.cs`
- `src/Taskboard.Integrations/Agents/{AgentModelCatalogService,AgentModelConfigService,KnownCliAgentAdapter,AcpSessionClient,JsonRpcAcpClient}.cs`
- `src/Taskboard.Blazor/Components/Pages/{AiChat.razor,NewThreadDialog.razor,RunAgentDialog.razor,AgentModelConfigDialog.razor}`
- `src/Taskboard.Blazor/Services/{TaskboardClient.cs,HttpAgentModelConfigService.cs,HttpAgentOrchestrationService.cs}`
- `.specs/SPEC-005-ai-chat.md`, `.specs/SPEC-20260919-web-cli-agent.md`, `.specs/SPEC-20260918-agent-model-tiers.md`, `.specs/SPEC-20260918-agent-model-config.md`, `.specs/SPEC-20260917-agent-eligibility-task-badge.md`

**Files to create or modify:**
```text
src/Taskboard.Server/Services/AiCatalogService.cs              [mod: agregador por agente, sem defaults]
src/Taskboard.Server/Services/AgentSessionManager.cs           [mod: sessão assistant sem workdir]
src/Taskboard.Server/Program.cs                                [mod: catalog + thread create + run routing]
src/Taskboard.Application/AiChat/AiChatService.cs              [mod: dispatch CLI]
src/Taskboard.Application.Contracts/Dtos/AiChatModelDto.cs     [mod: +AgentType]
src/Taskboard.Application.Contracts/Dtos/AiChatThreadDto.cs    [mod, se necessário]
src/Taskboard.Application.Contracts/Requests/CreateAiChatThreadRequest.cs [mod: AgentType obrigatório]
src/Taskboard.Domain/Entities/AiChatThread.cs                  [mod: AgentType em assistant]
src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs      [mod: model flag no spawn]
src/Taskboard.Blazor/Components/Pages/NewThreadDialog.razor    [mod]
src/Taskboard.Blazor/Components/Pages/AiChat.razor             [mod]
src/Taskboard.Blazor/Services/TaskboardClient.cs               [mod]
tests/**                                                        [new/mod]
docs/**                                                         [mod en + pt-br]
```

## 4. Requirements

### RF-001: Catálogo por agente
- **Description:** `GET /api/local/ai/catalog` retorna modelos agrupados por agente elegível: para cada `AgentType` elegível (`IAgentEligibilityService` = instalado + autenticado + habilitado), modelos = `IAgentModelCatalogService.ListAvailableAsync` ∪ `AgentCliModels.Catalog` ∪ tiers salvos (`IAgentModelConfigService`), deduplicado, ordenado (probe primeiro). `AiChatModelDto` ganha `AgentType`. Nenhum modelo hardcoded de provider (gpt-4o etc.) permanece.
- **Input → Output:** `GET` → `200 { models: [{ id, name, provider: <agentType>, agentType, supportsReasoning }] }`; zero agentes elegíveis → `{ models: [] }`.

### RF-002: Thread creation exige agente
- **Description:** `CreateAiChatThreadRequest.AgentType` obrigatório nos dois modos; servidor rejeita agente inelegível (`422 agent-not-eligible`). `assistant` = conversa sem workspace (cwd = `WorkspaceService.EnsureRoot()`); `agent` = comportamento atual (workdir por repo/path).
- **Input → Output:** `{ title, mode, agentType, model?, ... }` → `201 { thread }` com `agentType` persistido; `400` sem agentType; `422` inelegível.

### RF-003: Model picker por agente na UI
- **Description:** `NewThreadDialog` lista agentes elegíveis (nome + status); ao trocar o agente, o dropdown Model recarrega com os modelos daquele agente (catálogo RF-001); "CLI default" como primeira opção (deixa a CLI decidir). Modo `agent` mantém workspace path; modo `assistant` esconde workdir.
- **Rules:** sem agentes elegíveis → alerta com link para `/agents`/`/settings` (padrão `RunAgentDialog`); modelo é opcional (default da CLI quando vazio).

### RF-004: Execução assistant via CLI
- **Description:** `POST .../runs` em thread `assistant` com `AgentType` → executa o CLI via `IAgentAcpClient.ExecuteAsync` (one-shot) com o transcript da thread como prompt (`AgentThreadPromptBuilder.BuildAssistantPrompt`), cwd = workspace root. Saída publicada como `ai_chat.event`/`ai_chat.run` existentes; exit code decide sucesso/falha do run.
- **Implementado:** one-shot para todas as CLIs em modo assistant — `session/prompt` é fire-and-forget (sem sinal de conclusão correlacionável ao `AiChatRun`), então a sessão interativa permanece exclusiva do modo `agent` (`/prompt` via `AgentSessionManager`). Multi-turn = histórico embutido no prompt.
- **Rules:** `ModelRef` da thread é passado à CLI via `ResolvedModelName` (`AgentCliInvocation.BuildArguments` → `AgentCliModels.ModelFlag`); `"default"` → `OmitModelFlag` → sem flag de modelo (a CLI decide — o curado Normal NÃO é injetado); `Taskboard:WebCliAgent:Enabled` não afeta o caminho assistant (one-shot não depende de sessão). Elegibilidade é revalidada a cada run — agente desabilitado/desautenticado após a criação da thread → run falha com evento `error`. Linhas emitidas pela CLI são persistidas em ordem estrita (cadeia sequencial de emits) e todas completam antes do run fechar. No modo `agent`, o modelo escolhido chega à sessão interativa via `BuildSessionCommand` → flag de modelo.

### RF-005: Migração automática de threads legadas + mock só para dev/teste
- **Description:** ao enviar mensagem numa thread `assistant` **legada sem `AgentType`**, o servidor faz bind automático ao primeiro agente elegível (ordem estável = ordem retornada por `IAgentEligibilityService`) e persiste o `AgentType` na thread antes de executar — a thread passa a rodar via CLI normalmente. Se nenhum agente for elegível, o run falha com evento `error` claro ("no eligible agent CLI — authenticate one in Agents/Settings"). `MockLLMProvider` deixa de ser o default: só é usado quando `Taskboard:AiChat:MockProvider=true` (dev/testes).
- **Input → Output:** run em thread legada sem agente, com ≥1 agente elegível → `AgentType` vinculado + execução CLI; sem agente elegível → evento `error` + run `failed`; com flag mock → comportamento atual.

### RF-006: Thread header mostra backend real
- **Description:** header da thread exibe `agent: <type>` + `model: <model>` nos dois modos; sidebar badge de modelo permanece.

### RF-007: Catálogo custom (POST) vinculado a agente
- **Description:** `POST /api/local/ai/catalog` exige `agentType` e valida contra agentes elegíveis; entradas custom ficam em memória (como hoje) e aparecem no grupo do agente — `GET` filtra entradas cujo agente deixou de ser elegível. `409` em duplicata (inalterado).

**Business rules / invariants:**
- Nenhuma chamada de rede a provedor de LLM é feita pelo servidor — todo LLM passa pelo CLI do agente (credenciais são da CLI, não do Harness).
- Nenhum segredo/API key novo em Settings ou `ConfigurationOverrides`.
- Threads/eventos permanecem append-only; histórico de threads antigas preservado.
- Endpoints novos/alterados mantêm `RequireAuthorization` e formato `{ error: { code, message } }`.

## 5. API Contract

```http
GET  /api/local/ai/catalog
  → 200 { models: [{ id, name, provider, agentType, supportsReasoning }] }
POST /api/local/ai/catalog  { id, name, agentType, supportsReasoning? }
  → 201 { model } | 409 MODEL_EXISTS | 422 agent-not-eligible
POST /api/local/ai/threads  { title, mode: "assistant"|"agent", agentType, model?,
                              reasoningEffort?, sandbox?, workspacePath?|repositoryFullName? }
  → 201 { thread } | 400 INVALID_AGENT | 422 agent-not-eligible
POST /api/local/ai/threads/{id}/runs    → 201 { run }  (assistant: despacha CLI)
POST /api/local/ai/threads/{id}/prompt  → 202 | 409 THREAD_NOT_AGENT  (inalterado)
GET  /api/agents/{agentType}/models/available   → reuso (probe, já existe)
```

**Expected errors:** `400` validação · `401` auth · `404` thread/flag off · `409` conflito de estado · `422` agente inelegível/sem suporte a modelo.

## 6. Acceptance Criteria

- [ ] **Given** OpenCode elegível **when** abro "New thread" **then** o dropdown Model lista os modelos reportados por `opencode models` + curado (não gpt-4o hardcoded).
- [ ] **Given** thread `assistant` com `agentType=OpenCode` **when** envio mensagem **then** a resposta vem do CLI real (streaming), não do mock.
- [ ] **Given** agente sem `SupportsInteractiveSession` **when** thread `assistant` envia mensagem **then** executa one-shot e posta o resultado como eventos.
- [ ] **Given** nenhum agente elegível **when** abro "New thread" **then** alerta orienta a autenticar/habilitar em Agents/Settings e Create fica desabilitado.
- [ ] **Given** thread `assistant` legada sem agente e ≥1 agente elegível **when** envio mensagem **then** a thread ganha `AgentType` automaticamente (persistido) e responde via CLI; sem agente elegível → run falha com evento `error` orientando a autenticar um agente.
- [ ] **Given** modelo escolhido **when** a sessão/execução inicia **then** o argv do CLI inclui a flag de modelo correta (`--model`, `-m` conforme `AgentCliModels`).
- [ ] **Given** flag `WebCliAgent:Enabled=false` **when** uso assistant **then** execução one-shot funciona sem sessão; modo `agent` permanece oculto.
- [ ] **Given** `POST catalog` com `agentType` inelegível **when** submeto **then** `422`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Probe da CLI falha/timeout | `opencode models` indisponível | catálogo mostra curado + overrides; UI anota "probe failed" |
| Agente desabilitado após thread criada | toggle off em Settings | envio → `422 agent-not-eligible` + evento error |
| Thread legada + zero agentes elegíveis | send message | run `failed` + evento `error` "no eligible agent CLI" |
| Thread legada + múltiplos agentes elegíveis | send message | bind ao primeiro da ordem de `IAgentEligibilityService`; persistido |
| Modelo inválido para a CLI | string arbitrária | passa à CLI (ela valida); erro da CLI vira evento `error` |
| Thread agent sem model | model vazio | CLI decide (sem flag de modelo no argv) |
| Cache de probe stale | CLI atualizada | `?refresh=true` já existe; catálogo usa TTL 5min atual |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler seção 3; confirmar contratos (`AgentInfo`, eligibility, `SupportsInteractiveSession`, `BuildSessionCommand`).
- [ ] **T2 — Catálogo:** `AiCatalogService` vira agregador por agente; `AiChatModelDto.AgentType`; endpoint + POST validado; testes.
- [ ] **T3 — Domínio/API:** `AgentType` obrigatório no create; `AiChatThread` assistant com agente; run routing (sessão vs one-shot); erro para legadas; testes de endpoint.
- [ ] **T4 — Model flag:** adapters passam `ModelRef` via `AgentCliModels.ModelFlag` no spawn/sessão; testes de argv.
- [ ] **T5 — UI:** `NewThreadDialog` (agent picker dinâmico + model por agente + empty state), `AiChat.razor` header `agent · model`, `TaskboardClient`; testes de lógica.
- [ ] **T6 — Verify:** `dotnet build` (warnings=errors), `dotnet test` (≥ gate ratchet), docs en/pt-br, SPEC → Done + PR.

## 8. Organization Guardrails

- **Branches:** nunca commit em `main`/`master`/`develop` — usar `feature/devin-20260921-ai-chat-cli-backend`.
- **Workflows:** não editar `.github/workflows/**`.
- **Segurança:** nenhuma credencial nova; env dos processos agente segue `WithoutTaskboardEnv`; nada de API keys em logs/status.
- **Arquitetura:** ABP N-Layer — contratos em `Application.Contracts`, spawn/ACP em `Integrations`, orquestração em `Server`, zero lógica em `.razor`/endpoints.
- **Specs:** rotas novas/alteradas documentadas aqui; `SPEC-005` marcado como superado nas partes de catálogo/provider (atualizar referência).
- **Escopo:** sem provider HTTP direto; sem agent loop in-process; sem edição de tiers (já existe).

## 9. Definition of Done

- [ ] RF-001…RF-007 implementados; ACs cobertos por testes.
- [ ] `dotnet build` limpo; `dotnet test` verde; cobertura ≥ gate vigente (73%+).
- [ ] AI Chat funciona ponta-a-ponta com CLI real sem configuração adicional.
- [ ] `MockLLMProvider` fora do caminho default de produção; docs en/pt-br atualizadas; SPEC → Done + PR.

## Open Questions / Pending Ambiguity

- **Q-A — resolvida (aprovado):** thread `assistant` sem workspace executa sessão do CLI com cwd = workspace root (`WorkspaceService.EnsureRoot()`), sandbox read-only por padrão.
- **Q-B — resolvida (aprovado):** threads `assistant` legadas sem `AgentType` fazem **migração automática** — bind ao primeiro agente elegível persistido na thread (RF-005); sem agente elegível → erro explícito.
