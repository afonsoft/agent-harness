# SPEC-20260929-ai-code-provider-chat

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ai-code-provider-chat` |
| Type | `Feature` (Frontend + API + Background) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / EF Core SQLite / Blazor WASM / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260929-ai-code-provider-chat` |
| Ticket | [#392](https://github.com/afonsoft/agent-harness/issues/392) |
| Status | `Done` — delivered in PR |

## 1. User Story

**As a** Harness operator
**I want** an open-webui-style provider chat inside the AI Code page — powered
by any OpenAI-compatible endpoint configured in Settings, with persistent
history, host tools (shell, files, CLIs), web search, code interpretation and
image generation — **So that** I can talk directly to LLM models (not only to
agent CLIs) and let the model act on the host server with confined, auditable
tools.

**Problem context:**

1. Today every AI Code thread is bound to an agent CLI (ACP/PTY) — there is no
   direct LLM provider in the server (`MockLLMProvider` is dev/test only). The
   `ILLMProvider` contract (`CompleteAsync`/`StreamAsync`) already exists but
   has no real implementation and no UI.
2. Operators want a general-purpose chat like open-webui: sidebar with
   searchable history, model picker per conversation, markdown + code rendering,
   and agentic extras (tools, search, image generation) without leaving the site.
3. Host access today requires going through an agent CLI; a direct tool loop
   (OpenAI function calling) with the existing security gateway (path jail,
   command classification, secret scrubbing) is missing.

## 2. Scope

**In scope:**

- **Provider catalog (Settings → Chat)**: CRUD of OpenAI-compatible providers
  (name, base URL, API key masked, enabled); live model discovery via
  `GET {baseUrl}/v1/models`; capability defaults (chat / code / image model)
  and search backend config (SearxNG | Tavily | Brave: url + key).
- **Chat mode in AI Code**: new command-bar mode `Chat (Provider)` — provider +
  model picker replaces the agent CLI picker; open-webui-like layout with a
  persistent, collapsible history sidebar (search, new, delete) while in this
  mode; markdown, code blocks with copy, inline images.
- **Conversation persistence**: new `ChatConversation` + `ChatMessage`
  entities (SQLite/EF) — title, provider, model, messages (user/assistant/tool),
  tool calls/results, token usage; search by title and content.
- **Streaming**: SSE per conversation (`chat.delta`, `chat.tool_call`,
  `chat.tool_result`, `chat.done`) with heartbeat, following the existing
  `ai_chat.*` SSE conventions; stop button cancels the run.
- **Tool loop (auto confinado)**: server-side OpenAI function-calling loop with
  built-in tools — `shell_exec`, `read_file`, `write_file`, `list_dir`,
  `run_cli`, `web_search`, `code_interpreter`, `generate_image` — all confined
  by the existing security gateway (command classification, path jail,
  secret scrubbing), timeout + output truncation, writes jailed to the
  workspace, master switch `Taskboard:Chat:Tools:Enabled`.
- **Web search tool**: SearxNG (no key) / Tavily / Brave backends; results
  (title, url, snippet) injected as tool output.
- **Code interpreter tool**: local confined execution — python3 / node / dotnet
  script run from a temp file with timeout; stdout/stderr captured as tool
  output.
- **Image generation**: `POST {baseUrl}/v1/images/generations` of the selected
  provider; result rendered inline (b64 → data URL) and persisted on the
  message.
- **Per-capability models**: Settings defaults per capability; the command
  bar model picker switches the conversation model at any time.
- Unit + integration tests (pt-BR BDD names).

**Out of scope:**

- New top-level nav item — the Chat lives inside AI Code (user decision).
- Voice input/output, TTS/STT.
- RAG / file upload as chat attachments (RAG MCP already exists separately).
- Docker-sandboxed code execution (local confined execution only; network
  isolation is best-effort — documented limitation).
- MCP tool protocol exposure of the chat tools; custom/user-defined tools.
- Multi-user sharing of conversations (single local user model as the rest
  of the app).
- Streaming image generation; editing/resending assistant messages.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Domain` — new entities `ChatConversation`, `ChatMessage`,
  `ChatProvider` (aggregate roots under `Entities/Chat`).
- `Taskboard.Application.Contracts` — `Chat` DTOs, `IChatService`,
  `IChatTool` abstraction, `ISearchBackend`.
- `Taskboard.Application` — `ChatService` (send/stream/tool loop),
  `OpenAiCompatibleClient` (chat completions + models + images),
  `SearchBackends` (SearxNG/Tavily/Brave), tool implementations reusing
  `ISecurityGateway`, `PathJailValidator`, `SecretScrubber`.
- `Taskboard.EntityFrameworkCore` — entity configurations + migration
  `AddChatConversations`.
- `Taskboard.Server` — `/api/local/chat/*` endpoint group; SSE stream service
  reuse (`IThreadEventStreamService` pattern).
- `Taskboard.Blazor` — `AiChat.razor` gains Mode `Chat (Provider)`: provider/
  model picker, persistent history sidebar (open-webui style), markdown/code/
  image renderers, tool-call cards; `Settings.razor` gains a **Chat** section
  (providers CRUD + capability defaults + search backend).
- `RuntimeConfigurationService` — new catalog keys (tools switch, capability
  defaults, search backend).

**Files to read before implementing:**

- `src/Taskboard.Application.Contracts/AiChat/ILLMProvider.cs` — provider
  contract to implement (`OpenAiCompatibleLLMProvider`).
- `src/Taskboard.Application/AiChat/AiChatService.cs` +
  `src/Taskboard.Server/Program.cs` (ai endpoints, ~1377-1650) — SSE
  conventions, thread persistence, error envelopes.
- `src/Taskboard.Blazor/Components/Pages/AiChat.razor` — command bar, message
  rendering, composer, history overlay (extend, don't fork).
- `src/Taskboard.Application/Harness/SecurityGateway.cs`,
  `PathJailValidator.cs`, `SecretScrubber.cs` (names as found) — confinement
  primitives to reuse.
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` —
  catalog pattern for new keys.
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — section pattern
  (Features/Configuration) for the new Chat section.
- `src/Taskboard.EntityFrameworkCore/Configurations/` — EF configuration
  conventions; `CliMetricsConfigurations.cs` as example.
- `.specs/SPEC-20260921-ai-chat-cli-backend.md`,
  `.specs/SPEC-20260929-ai-chat-view-first.md`.

**Files to create or modify:**

```text
src/Taskboard.Domain/Entities/Chat/ChatConversation.cs             [new]
src/Taskboard.Domain/Entities/Chat/ChatMessage.cs                  [new]
src/Taskboard.Domain/Entities/Chat/ChatProvider.cs                 [new]
src/Taskboard.Application.Contracts/Chat/*.cs                      [new — DTOs, IChatTool, OpenAiCompatibleClient, ChatImageStore]
src/Taskboard.Application/Chat/ChatService.cs                      [new — tool loop + SSE events]
src/Taskboard.Integrations/Chat/Tools/*.cs                         [new — tools confinadas (Integrations: PathJail/Scrubber)]
src/Taskboard.Integrations/Chat/SearchBackends/SearchBackends.cs   [new]
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs [mod]
src/Taskboard.EntityFrameworkCore/Configurations/ChatConfigurations.cs [new]
src/Taskboard.EntityFrameworkCore/Migrations/*_AddChatConversations.cs [new]
src/Taskboard.Server/Program.cs                                    [mod — chat endpoints]
src/Taskboard.Blazor/Components/Chat/ProviderChat.razor(.css)      [new]
src/Taskboard.Blazor/Components/Pages/AiChat.razor                 [mod — Mode Chat (Provider)]
src/Taskboard.Blazor/Components/Pages/Settings.razor               [mod — seção Chat]
src/Taskboard.Blazor/Services/TaskboardClient.cs                   [mod]
tests/Taskboard.Tests.Unit/Chat/*Tests.cs                          [new]
tests/Taskboard.Tests.Integration/ChatEndpointsTests.cs            [new]
```

> Nota de implementação: as tools vivem em `Taskboard.Integrations` (não em
> `Taskboard.Application`) porque dependem de `PathJailValidator`/`SecretScrubber`
> — a regra de dependência proíbe Application → Integrations. O cliente HTTP e
> o image store vivem em `Application.Contracts` para serem visíveis a ambos.

## 4. Requirements

### RF-001: Catálogo de providers OpenAI-compatíveis
- **Description:** The system must let the operator register multiple
  OpenAI-compatible providers (name, base URL, API key, enabled) in
  Settings → Chat, persisted in SQLite.
- **Rules:** API key is write-only (masked `••••` on read, never logged);
  base URL must be absolute http(s); duplicate names rejected; a provider can
  be disabled without deleting (conversations keep their historical provider
  id; sending fails with a clear error if the provider is gone/disabled).
- **Input → Output:** `POST /api/local/chat/providers {name, baseUrl, apiKey}` →
  `201 {provider}`; `GET` lists with `HasApiKey` + masked hint, never the key.

### RF-002: Descoberta de modelos
- **Description:** The system must fetch models live from
  `{baseUrl}/v1/models` (Bearer key) and cache per provider for 1 hour.
- **Rules:** unreachable endpoint → 502 with the provider name in the error;
  model ids sorted; result feeds the command-bar picker and the capability
  default dropdowns in Settings.
- **Input → Output:** `GET /api/local/chat/providers/{id}/models` →
  `{ models: ["gpt-4o", ...], cached: true|false }`.

### RF-003: Defaults de modelo por capacidade
- **Description:** Settings must expose per-capability default model selection
  (chat, code interpretation, image) scoped to a provider.
- **Rules:** stored as config keys (`Taskboard:Chat:DefaultChatModel`,
  `Taskboard:Chat:DefaultCodeModel`, `Taskboard:Chat:DefaultImageModel`,
  values `"{providerId}:{modelId}"`); a new conversation pre-fills the chat
  default; the code interpreter and image tools resolve their model from these
  defaults at call time; the command-bar picker can override the conversation
  model at any moment (persisted on the conversation).

### RF-004: Conversas e histórico persistente
- **Description:** The system must persist provider conversations
  (`ChatConversation`: provider, model, title, timestamps) and messages
  (`ChatMessage`: role, content, tool calls/results, tokens, image data ref)
  in SQLite, with list/search/delete.
- **Rules:** title auto-derives from the first user message (~60 chars, same
  rule as AI Code threads); search matches title and message content
  (case-insensitive); delete removes messages (cascade); history sidebar
  lists newest-first with model badge.
- **Input → Output:** `GET /api/local/chat/conversations?q=` →
  `{ conversations: [{id, title, model, updatedAt, preview}] }`.

### RF-005: Chat streaming com tool loop
- **Description:** Sending a message must stream the assistant response over
  SSE and execute a server-side tool loop when the model emits `tool_calls`.
- **Rules:** loop = up to `Taskboard:Chat:MaxToolIterations` (default 8)
  model↔tool round-trips per user message; each tool call emits
  `chat.tool_call` before execution and `chat.tool_result` after; final text
  streams as `chat.delta` chunks; `chat.done` closes with usage; the stop
  endpoint cancels the in-flight run (partial assistant message persisted);
  failures surface `problem+json` with the provider reason.
- **Input → Output:** `POST .../conversations/{id}/messages {content}` → SSE
  stream; `POST .../conversations/{id}/stop` → 202.

### RF-006: Tools de host auto confinadas
- **Description:** The chat must expose built-in host tools that run without
  per-call approval but under confinement.
- **Rules:** master switch `Taskboard:Chat:Tools:Enabled` (default `true`);
  `shell_exec` and `code_interpreter` commands are classified by the existing
  security gateway — `Dangerous` → refused (tool result carries the refusal),
  `WorkspaceWrite` allowed only inside the workspace, `Safe` runs with
  timeout (`Taskboard:Chat:ToolTimeoutSeconds`, default 60) and output
  truncated (16 KB); `read_file`/`write_file`/`list_dir` are path-jailed to
  the workspace (workspace = globally selected repo dir); every tool result is
  secret-scrubbed before it reaches the model or the transcript.
- **Input → Output:** tool schema follows OpenAI function-calling
  (`{name, description, parameters}`); tool results are JSON strings.

### RF-007: Busca na internet
- **Description:** The `web_search` tool must query the configured backend —
  SearxNG (`Taskboard:Chat:SearchUrl`, no key), Tavily or Brave (key) — and
  return the top N results (title, url, snippet).
- **Rules:** backend selected by `Taskboard:Chat:SearchBackend`
  (`searxng|tavily|brave|none`, default `none` → tool returns "search not
  configured"); results truncated to `Taskboard:Chat:SearchMaxResults`
  (default 5); URLs are data, never fetched automatically.
- **Input → Output:** query → JSON array of results as tool output.

### RF-008: Interpretação de código
- **Description:** The `code_interpreter` tool must execute a code snippet
  locally with the runtime chosen by the model (`python3`, `node`, `dotnet`)
  and return stdout/stderr/exit code.
- **Rules:** snippet written to a temp file under the workspace `.chat-tmp/`;
  same timeout/scrubbing/classification rules as RF-006; runtimes resolved
  from PATH, missing runtime → clear tool error; no per-call approval
  (auto-confined per user decision).
- **Input → Output:** `{language, code}` → `{stdout, stderr, exitCode,
  durationMs}`.

### RF-009: Geração de imagem
- **Description:** The `generate_image` tool (and a composer shortcut) must
  call `POST {baseUrl}/v1/images/generations` of the conversation's provider
  with the capability default image model.
- **Rules:** response `b64_json` persisted as a message attachment and
  rendered inline; provider without image support → clear tool error;
  generated images are stored under the data dir (`chat-images/`) and served
  via an authenticated local endpoint.
- **Input → Output:** `{prompt, size?}` → message with image + markdown
  render.

### RF-010: UI — modo Chat no AI Code
- **Description:** The AI Code command bar must gain Mode `Chat (Provider)`:
  provider + model pickers replace the agent CLI picker; a persistent
  collapsible history sidebar (open-webui style) lists conversations with
  search, new-chat and delete; messages render markdown, code blocks (copy),
  tool-call cards (reuse existing renderers) and inline images.
- **Rules:** switching mode keeps the existing Assistant/Agent behavior
  untouched; the sidebar is visible by default in Chat mode on wide screens
  and collapses to the existing overlay pattern on narrow screens; the
  composer keeps Send/Stop; conversation title/model badge shown in the
  header.
- **Input → Output:** user selects Chat mode → sidebar + picker; send →
  streamed answer.

### RF-011: Settings — seção Chat
- **Description:** Settings must gain a **Chat** section: provider CRUD table
  (name, baseUrl, masked key, enabled, Test button hitting `/v1/models`),
  capability default selects (fed by RF-002 models), search backend config,
  and the tools switch.
- **Rules:** follows the existing section patterns (Features/Configuration);
  keys stay visible in the Configuration table (single source of truth).
- **Input → Output:** save provider → toast + list refresh; Test →
  `N models found` or error.

### RF-012: Testes
- **Description:** Every RF covered by pt-BR BDD tests: unit (client parsing,
  tool loop, jail/gateway confinement, search backends, capability defaults)
  and integration (provider CRUD, conversation CRUD, send/stream with a fake
  provider, tool refusal paths).

## 5. API Contract

All endpoints under `/api/local/chat/*` (session cookie / API key, existing
local API conventions; errors as `problem+json` with `code`).

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/providers` | list providers (key masked) |
| `POST` | `/providers` | create provider |
| `PUT` | `/providers/{id}` | update provider |
| `DELETE` | `/providers/{id}` | delete provider |
| `GET` | `/providers/{id}/models` | live model discovery (1h cache) |
| `GET` | `/conversations?q=` | list/search conversations |
| `POST` | `/conversations` | create conversation (provider, model) |
| `GET` | `/conversations/{id}` | full transcript |
| `DELETE` | `/conversations/{id}` | delete conversation + messages |
| `PATCH` | `/conversations/{id}` | rename / change model |
| `POST` | `/conversations/{id}/messages` | send → SSE stream |
| `POST` | `/conversations/{id}/stop` | cancel in-flight run |
| `GET` | `/images/{file}` | authenticated image fetch |

**SSE events:** `chat.delta {content}`, `chat.tool_call {name, arguments}`,
`chat.tool_result {name, result, refused}`, `chat.done {usage, finishReason}`,
`: hb` heartbeat every 15s.

**Expected errors:** `400` validation · `404` unknown id · `409` run in
flight · `502` provider unreachable/bad response (provider name in detail,
never the API key).

## 6. Acceptance Criteria

- [ ] **Dado** provider configurado no Settings **quando** crio uma conversa em Chat (Provider) e envio "olá" **então** a resposta do modelo chega em streaming e a conversa aparece no histórico com título derivado.
- [ ] **Dado** histórico com 10 conversas **quando** busco por termo do conteúdo **então** a conversa correspondente é listada.
- [ ] **Dado** tool `shell_exec` com comando `Dangerous` **quando** o modelo o invoca **então** o resultado carrega a recusa do gateway e o transcript registra `tool_result` com `refused=true`.
- [ ] **Dado** tool `write_file` com path fora do workspace **quando** executada **então** path jail bloqueia e o resultado explica o confinamento.
- [ ] **Dado** `Taskboard:Chat:Tools:Enabled=false` **quando** envio mensagem **então** nenhuma tool é anunciada ao modelo.
- [ ] **Dado** busca configurada (SearxNG) **quando** o modelo chama `web_search` **então** o tool result contém até 5 resultados com título/url/snippet.
- [ ] **Dado** snippet python **quando** `code_interpreter` executa **então** stdout/stderr/exitCode voltam como tool result e aparecem no card do chat.
- [ ] **Dado** provider com suporte a imagens **quando** gero imagem **então** a imagem aparece inline na conversa e persiste após refresh.
- [ ] **Dado** default de imagem configurado **quando** troco o modelo do chat no command bar **então** só o modelo do chat muda (imagem continua no default da capacidade).
- [ ] **Dado** provider indisponível **quando** envio mensagem **então** 502 com motivo legível e nenhum segredo no erro.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Provider deletado com conversa antiga | abrir conversa | erro claro, transcript read-only |
| Loop de tools sem resposta final | 8 iterações | para com `chat.done` + aviso de limite |
| Modelo não suporta function calling | tools anunciadas | erro do provider surfaced; conversa continua sem tools após toggle |
| Key inválida | `/v1/models` 401 | erro legível no Settings (Test) e no envio |
| Conversa com modelo trocado no meio | PATCH model | próximas mensagens usam o novo modelo |
| Output de tool gigante | 1 MB de stdout | truncado a 16 KB com marcador |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler arquivos da seção 3; confirmar nomes reais dos primitivos de segurança (gateway/jail/scrubber) e padrões SSE.
- [ ] **T2 — Domínio + persistência:** entidades `ChatProvider`/`ChatConversation`/`ChatMessage`, configurações EF, migration.
- [ ] **T3 — Provider client:** `OpenAiCompatibleClient` (chat completions SSE, models, images) sobre `ILLMProvider`.
- [ ] **T4 — Tool loop + tools:** `ChatService` com loop function-calling; tools RF-006…RF-009 confinadas; search backends.
- [ ] **T5 — Endpoints:** grupo `/api/local/chat/*` com SSE e envelopes de erro.
- [ ] **T6 — UI:** modo Chat no AiChat.razor (sidebar + pickers + renderers) e seção Chat no Settings.razor.
- [ ] **T7 — Config:** chaves novas no catálogo (`RuntimeConfigurationService`) + Features/Chat toggles.
- [ ] **T8 — Testes:** unit + integração (RF-012), pt-BR.
- [ ] **T9 — Verification:** `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes`; cobrir todos os critérios da seção 6.
- [ ] **T10 — Done + PR:** DoD completo → `Status = Done` → PR na branch da feature.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
|---|---|
| Feature / .NET | `dotnet build` limpo; `dotnet test` verde (unit + integration); novos testes cobrindo RF-001…RF-012; coverage gate ratchet respeitado (≥ 77%) |

## 8. Organization Guardrails

- Branch `feature/devin-20260929-ai-code-provider-chat`; nunca commitar em `main`/`develop`.
- Não editar `.github/workflows/**`.
- **Secrets:** API keys de provider/search nunca em logs, erros, transcripts ou DTOs (write-only, mascaradas).
- **Confinamento:** tools nunca executam comandos `Dangerous`; escrita fora do workspace bloqueada; scrubbing de segredos obrigatório antes de todo tool result.
- Mudança de contrato: endpoints novos sob `/api/local/chat/*` — sem tocar nas rotas existentes do AI Code.
- Testes obrigatórios antes do merge (Hard Rule 5); nomes BDD em pt-BR.
- Privacidade: histórico fica local (SQLite); nada é enviado a terceiros além do provider configurado pelo usuário.

## 9. Definition of Done

- [ ] RF-001…RF-012 implementados e cobertos por testes verdes.
- [ ] `dotnet build` limpo (TreatWarningsAsErrors) e `dotnet test` verde localmente.
- [ ] Critérios de aceitação (seção 6) todos verificados.
- [ ] `docs/features.md` + `docs/features.pt-br.md` atualizados (Chat mode + Settings Chat).
- [ ] Migration `AddChatConversations` aplicada sem breaking change.
- [ ] SPEC `Status = Done` e PR aberto na branch da feature.
- [ ] Nenhum arquivo de workflow alterado; nenhum secret commitado ou logado.
