# SPEC-20260918 — Agent Execution UX: modais maiores, clear de logs, Agent Config, prompt default, fix MCP provisioning e logs de sync

## 0. Metadata

| Campo | Valor |
|---|---|
| Feature | `agent-execution-ux` |
| Type | `Feature` (Frontend + API) + `Bugfix` (MCP provisioning) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Blazor WASM / Blazor.Bootstrap` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260918-agent-execution-ux` |
| Ticket | N/A |
| Status | `Done` |

## 1. User Story

**As a** Taskboard administrator running agent CLIs against kanban issues,
**I want** roomier task/issue dialogs, a clearable agent-log console, an
"Agent Config" tab that shows the repository link and the exact CLI
invocation, a shared default prompt (clone repo → apply card corrections →
use skills → consult the knowledge MCP), working MCP provisioning into
every enabled CLI (including Antigravity), and visible install/sync logs,
**so that** agent execution is transparent, configurable and debuggable
end-to-end.

Problem context: the New Task and Issue dialogs are cramped
(`ModalSize.Small`/`Large`); the log tab can't be cleared; the agent tab
name is confusing and shows no repo link or command preview; every run
needs a hand-written prompt; RAG MCP sync silently installs nothing (the
stored `Taskboard:Rag:Url` is empty and Antigravity has no config target,
always `Skipped`); and skills/MCP sync runs give no visible output.

## 2. Scope

### In scope

- `_newTaskModal` and `_detailModal` widened to ~70 % viewport
  (`width:70vw; max-width:70vw` ≥ 768 px), keeping
  `modal-fullscreen-sm-down` on mobile.
- `TaskLogTab` gains **Limpar**: clears the rendered list **and** persisted
  logs via new `DELETE /api/agents/logs/{issueId}`.
- `TaskDetailDialog` tab **Config Agent → Agent Config**:
  clickable repository link, read-only preview of the CLI invocation for
  the selected agent (argv template, e.g.
  `devin --respect-workspace-trust false -p <prompt>`), and the prompt
  textarea pre-filled with the default prompt (RF-004).
- New `Taskboard:Agents:DefaultPrompt` runtime override editable in the
  **CLI Agents** page; placeholders `{repoUrl}` `{issueTitle}` `{issueBody}`
  substituted at execution-request time; initial content covers clone,
  corrections, skills and knowledge-MCP usage.
- **MCP provisioning fix**: Antigravity target via `agy mcp add <name>
  <url> -t http -H "Authorization: Bearer <key>"` (idempotent add/update,
  `agy mcp remove` when URL cleared); keep file-merge for the other CLIs.
- **Sync logs**: circular in-memory log buffer (~200 lines) in
  `McpProvisioningService` and the skills-sync service, exposed via
  `GET /api/mcp/log` and a skills log endpoint; rendered read-only in the
  Skills page and the RAG/MCP settings section.
- Tests: log endpoints, clear-logs, default-prompt resolution/substitution,
  agy provisioning path (invocation building, not a real `agy` call), and
  updated UI behavior where testable.

### Out of scope

- Streaming/live-follow of sync logs (polling on demand is enough).
- Persisting agent-run logs across restarts beyond what already exists.
- Changing `AgentCommand` templates (fixed in `d522787`).
- MCP provisioning for `OpenHands` (no CLI on this host; stays `Skipped`).

## 3. Technical Context

Read first:

- `src/Taskboard.Blazor/Components/GitHub/KanbanBoard.razor` (modals at
  lines 6-8; `_detailModal`/`_newTaskModal`).
- `src/Taskboard.Blazor/Components/GitHub/TaskDetailDialog.razor` (tabs;
  `AgentConfigTab` usage at ~line 107).
- `src/Taskboard.Blazor/Components/GitHub/AgentConfigTab.razor`,
  `TaskLogTab.razor`, `AgentSelectionModal.razor`.
- `src/Taskboard.Blazor/Components/Pages/Agents.razor` (CLI Agents page),
  `Settings.razor` (RAG/MCP section ~lines 137-182, `SaveRagAsync` /
  `SyncMcpAsync` / `VerifyMcpAsync`).
- `src/Taskboard.Blazor/Components/Pages/Skills.razor`.
- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs`,
  `src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs` (no Antigravity
  target — confirmed gap).
- `src/Taskboard.Integrations/Agents/KnownCliAgentAdapter.cs`
  (`BuildArguments` — the argv templates the preview must mirror).
- `src/Taskboard.Integrations/Agents/AgentOrchestrationService.cs`
  (in-memory `_logs` + `IAgentLogRepository`; `GetLogsAsync`,
  `AppendLog`, `CancelAsync`).
- `src/Taskboard.Server/Program.cs` (agent endpoints ~1345-1382, mcp
  endpoints ~1418-1464, `ResolveEnabledAgentsAsync`).
- Skills sync: `ISkillsSyncService` / `SkillsSyncHostedService` and the
  `skills/*` endpoints in `Program.cs`.

Confirmed host facts:

- `agy mcp add <name> <commandOrUrl> [-t http] [-H "K: V"]`, `agy mcp
  remove`, `agy mcp list` — official CLI; config persists in
  `~/.gemini/config/mcp_config.json` (`mcpServers` map).
- `Taskboard:Rag:Url` override is currently **empty** → provisioning runs
  in remove mode; the UI must surface this clearly.

## 4. Requirements

- **RF-001**: `_newTaskModal` e `_detailModal` ocupam ~70 % da viewport em
  telas ≥ 768 px (CSS `.modal-70` ou equivalente aplicado via
  `Scrollable`/`Class`), mantendo fullscreen < 768 px.
- **RF-002**: `TaskLogTab` tem botão **Limpar** que chama `DELETE
  /api/agents/logs/{issueId}` (limpa memória + `IAgentLogRepository`) e
  esvazia `_messages`; erro → toast danger. Endpoint: `204` em sucesso,
  `401` anônimo.
- **RF-003**: Aba renomeada para **Agent Config**; exibe (a) link
  `<a href>` do repositório (`RepositoryFullName` →
  `https://github.com/{full}`), (b) preview monoespaçado readonly do argv
  do agente selecionado (espelho de `BuildArguments`, com o prompt como
  `<prompt>`), (c) textarea de prompt.
- **RF-004**: `Taskboard:Agents:DefaultPrompt` salvo via runtime
  configuration (mesma infra `RuntimeConfigurationService`); editável na
  página **CLI Agents** com hint dos placeholders. Ao abrir a aba Agent
  Config, a textarea é preenchida com o template substituído
  (`{repoUrl}`, `{issueTitle}`, `{issueBody}` → clone URL `https://` +
  `.git`, título e corpo da issue); edições do usuário não são
  sobrescritas.
- **RF-005**: `McpProvisioningService` provisiona Antigravity via
  `agy mcp add` (add/update) quando URL configurada e `agy mcp remove`
  quando limpa; falha de processo → `McpAgentState.Failed` com stderr
  sanitizado. `ReadAgentStatus` lê `~/.gemini/config/mcp_config.json` →
  `mcpServers[name].serverUrl`. Outros CLIs inalterados.
- **RF-006**: `McpProvisioningService` mantém ring buffer (~200 linhas) de
  linhas de log por run (`state`, por-agente resultado, erro sanitizado —
  nunca a API key); `GET /api/mcp/log` retorna `{ lines: [] }`; seção
  RAG/MCP do Settings renderiza console readonly após Save & Sync / Sync.
- **RF-007**: O serviço de skills sync expõe o mesmo mecanismo (buffer +
  `GET /api/skills/log` ou equivalente existente estendido); a página
  Skills renderiza o console do último install/sync (inclui `npx skills
  add`, `install.sh`, sync status) — read-only, atualizado por botão
  "Atualizar log" e ao disparar install/sync.
- **RF-008**: Se `Taskboard:Rag:Url` estiver vazio, a seção RAG/MCP
  mostra aviso "URL não configurada — sync remove a entrada dos CLIs" e o
  log reflete `NotConfigured`/`Removed` por agente.
- **RNF**: nunca logar nem serializar `Taskboard:Rag:ApiKey`; sanitização
  existente estendida ao buffer de log.
- **Não-goal**: nenhuma mudança de contrato que quebre clientes existentes.

## 5. API Contract

```http
DELETE /api/agents/logs/{issueId}          → 204 | 401
GET    /api/mcp/log                        → 200 { lines: string[] } | 401
GET    /api/skills/log                     → 200 { lines: string[] } | 401
GET    /api/agents/prompt-template         → 200 { template, rendered? } | 401
PUT    /api/agents/prompt-template         → 204 | 400 | 401   { template }
```

- `PUT prompt-template`: `template` vazio → limpa override (volta ao
  default embutido); `400` se exceder ~8 KB.
- Preview de argv pode ser endpoint leve (`GET /api/agents/commands`) ou
  função duplicada no client — decisão na implementação; se endpoint:
  `200 { commands: [{ agentType, argv: [] }] }`.
- Placeholders resolvidos client-side na aba Agent Config (issue já está
  carregada); `{repoUrl}` = `https://github.com/{RepositoryFullName}.git`.

## 6. Acceptance Criteria

- **AC-1**: Dado modal Nova Tarefa ou Issue aberto em viewport ≥ 768 px,
  quando renderizado, então largura ≈ 70 % da tela; em < 768 px continua
  fullscreen.
- **AC-2**: Dada aba Logs do Agente com mensagens, quando clico Limpar,
  então a lista esvazia e `GET logs/{issueId}` retorna vazio; reload não
  restaura as linhas.
- **AC-3**: Dada aba Agent Config com agente Devin selecionado, quando
  renderizada, então exibe link `github.com/{owner}/{repo}` e preview
  `devin --respect-workspace-trust false -p <prompt>`; ao trocar para
  Codex o preview muda para `codex exec --approve-for-me
  --skip-git-repo-check <prompt>`.
- **AC-4**: Dado `DefaultPrompt` configurado com `{repoUrl}`, quando abro
  Agent Config numa issue, então a textarea mostra o template com a URL
  de clone substituída; se eu editar e reabrir, minha edição por-issue
  (localStorage) prevalece.
- **AC-5**: Dado URL+key salvas e Antigravity habilitado, quando disparo
  Sync, então `agy mcp add knowledge <url> -t http -H "Authorization:
  Bearer <key>"` é invocado, `mcp/status` reporta `Configured` para
  Antigravity e `agy mcp list` mostra `knowledge`; quando limpo a URL,
  `agy mcp remove` roda e status vira `NotConfigured`.
- **AC-6**: Dado um run de sync (MCP ou skills), quando abro o console de
  log na UI, então vejo linhas por-agente/arquivo do último run sem a API
  key; erro de processo aparece como linha `Failed` com stderr.
- **AC-7**: Dado `Rag:Url` vazio, quando clico Sync, então a UI mostra o
  aviso e cada agente sai `NotConfigured`/`Removed` — nunca
  `Configured`.
- **AC-8**: Todos os endpoints novos retornam `401` anônimo e `200/204`
  autenticado (testes de integração).

## 7. Task Plan

1. **T1 — Modais ~70 %**: CSS `.modal-70` (≥768 px) + `Class`/estilo nos
   dois modais; validar mobile intacto. Build.
2. **T2 — Clear logs**: `DELETE /api/agents/logs/{issueId}` no
   orchestrator (memória + repo) + endpoint + botão Limpar no
   `TaskLogTab`. Testes: unit (orchestrator) + integração (401/204).
3. **T3 — Agent Config tab**: rename; repo link; argv preview (fonte
   única — expor template do adapter via contrato para não duplicar);
   prompt textarea.
4. **T4 — Default prompt**: override `Taskboard:Agents:DefaultPrompt` +
   endpoints GET/PUT + textarea na página CLI Agents + substituição de
   placeholders na aba Agent Config. Testes de substituição.
5. **T5 — MCP Antigravity**: implementar provision via `agy mcp
   add/remove` + `ReadAgentStatus` lendo `mcp_config.json`; testes do
   comando montado e do parse (fake home + runner injetado).
6. **T6 — Logs de sync**: ring buffer + `GET /api/mcp/log` +
   `GET /api/skills/log` + consoles na UI (RAG section + Skills page) +
   aviso de URL vazia (RF-008). Testes.
7. **T7 — Validação**: `dotnet build` + `dotnet test` completo; smoke no
   host (`agy mcp add/remove` real com URL dummy, sem key).
8. **T8 — Docs/entrega**: `docs/api.md`(+pt-br) novos endpoints, spec →
   `Done`, commit/push/PR/merge, publish + restart `taskboard-server`.

## 8. Organization Guardrails

- Branch `feature/devin-20260918-agent-execution-ux`; commits `fix:`/
  `feat:`/`docs:`; sem push direto em `main`.
- Nunca logar `Taskboard:Rag:ApiKey` (sanitizar stderr do `agy mcp` —
  o header vai na argv, garantir que erro/exception não vaze a key;
  preferir `agy mcp add` sem `-H` quando key vazia).
- Sem mudança em `.github/workflows/**`.
- Placeholders/documentos novos: pt-BR na UI, en no código/docs.

## 9. Definition of Done

- [ ] RF-001…RF-008 implementados e testados.
- [ ] `dotnet build` limpo (0 warnings) e `dotnet test` verde
  (unit + integration).
- [ ] `agy mcp list` no host mostra o servidor após sync real.
- [ ] `docs/api.md` e `docs/api.pt-br.md` atualizados.
- [ ] PR aberto, CI verde, merge, publish + restart do
  `taskboard-server` verificado (`/api/meta` 200).
- [x] Spec atualizado para `Status: Done`.
