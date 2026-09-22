# Documentação da API

## Endpoints REST

### Storage do Cliente

```http
GET    /api/client-storage
PUT    /api/client-storage
```

### IA

```http
GET    /api/local/ai/threads
POST   /api/local/ai/threads
DELETE /api/local/ai/threads/:id
GET    /api/local/ai/threads/:id/events
POST   /api/local/ai/threads/:id/events
POST   /api/local/ai/threads/:id/runs
PATCH  /api/local/ai/threads/:threadId/runs/:runId
GET    /api/local/ai/catalog
GET    /api/local/ai/composer/candidates
POST   /api/local/ai/composer/rebind
```

### Cloud

```http
GET    /api/meta
GET    /api/local/cloud-session
PUT    /api/local/cloud-session
```

### Workflow

```http
GET    /api/workflow-capabilities
PUT    /api/workflow-capabilities
GET    /api/device-workspaces
PUT    /api/device-workspaces   { "workspaceId", "workspace" }
```

### Jira

```http
GET    /api/local/jira-connection
POST   /api/local/jira-connection
POST   /api/local/jira-connection/sync
```

### Configurações

```http
GET    /api/settings
PUT    /api/settings
GET    /api/configuration
PUT    /api/configuration/{key}
DELETE /api/configuration/{key}
```

### Skills de Agente

```http
GET    /api/skills
GET    /api/skills/{source}/{name}
GET    /api/skills/{source}/{name}/files/{**path}
GET    /api/skills/sync/status
POST   /api/skills/sync
GET    /api/skills/install/status
POST   /api/skills/install
POST   /api/skills/install/verify
```

`POST /api/skills/install` executa `npx skills add <repo> -g --all --copy` e o `install.sh --all` do repositório em background (retorna `202` + status em andamento; chamadas concorrentes são coalescidas). `verify` re-varre os diretórios globais de skills sem spawnar processos.

### Provisionamento MCP (RAG)

```http
GET    /api/mcp/status
POST   /api/mcp/sync
POST   /api/mcp/remove
GET    /api/mcp/log
PUT    /api/mcp/rag
```

`PUT /api/mcp/rag` aceita `{ "name", "url", "apiKey" }` — campos `null` preservam o valor gravado; `url` vazia é rejeitada (`400 rag-url-required`) pois a remoção é a ação explícita `POST /api/mcp/remove`. `POST /api/mcp/sync` recarrega os overrides do banco e provisiona a config salva — retorna `400 rag-not-configured` quando não há URL gravada (Sync nunca remove implicitamente). `POST /api/mcp/remove` remove a entrada gerenciada de todos os alvos independente da URL gravada. Resultados por agente reportam `configured` / `updated` / `removed` / `not-configured` / `skipped` / `repaired` / `failed` (`updated` = entrada existente com valor diferente foi sobrescrita). O provisionamento faz merge da entrada `<name>` em `~/.config/devin/mcp_config.json`, `~/.claude.json`, `~/.codex/config.toml`, `~/.config/opencode/opencode.json`, `~/.openhands/mcp.json`, `~/.kimi-code/mcp.json`, `~/.grok/config.toml`, `~/.qwen/settings.json`, `~/.copilot/mcp-config.json` e `~/.kiro/settings/mcp.json`; Antigravity e Cline são provisionados via CLI (`agy mcp add/remove`, `cline mcp add/remove`); Continue recebe um arquivo JSON em `~/.continue/mcpServers/<name>.json` (escrita atômica, backup `.bak`, `0600`). A API key nunca é retornada por nenhum endpoint. Chaves de configuração: `Taskboard:Rag:ServerName` / `Taskboard:Rag:Url` / `Taskboard:Rag:ApiKey` (aliases de env `TASKBOARD_RAG_NAME` / `TASKBOARD_RAG_URL` / `TASKBOARD_RAG_API_KEY`), persistidas como overrides em SQLite.

### MCP Server (Streamable HTTP)

```text
POST   /api/mcp
```

As mesmas tools servidas pelo executável stdio `Taskboard.Mcp` também são expostas via Streamable HTTP — **stateless por padrão** (MCP C# SDK v2, spec rev. 2026-07-28: sem handshake `initialize`, sem `Mcp-Session-Id`; clientes legados continuam funcionando via fallback automático). O endpoint herda a autorização do grupo `/api` — cookie de sessão ou `X-Api-Key`; requisições anônimas retornam `401`.

Clientes devem enviar `Accept: application/json, text/event-stream`. Corpos são JSON-RPC 2.0 (`tools/list`, `tools/call`, ...).

Tools (idênticas ao servidor stdio): `get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`, `cloud_status`.

As tools chamam a API local via `ITaskboardApiClient` em loopback — o servidor se autentica com `Taskboard:ApiKey` (ou `TASKBOARD_API_KEY`) quando configurado; sem API key o endpoint ainda responde `tools/list`, mas chamadas de tools que batem em `/api` falham com `401`.

Exemplo:

```bash
curl -X POST http://127.0.0.1:47823/api/mcp \
  -H "X-Api-Key: <key>" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### CLI Agents e Terminal

```http
GET    /api/agent-clis
POST   /api/agent-clis/{kind}/install
GET    /api/agent-clis/{kind}/install/status
POST   /terminal-hub/negotiate   (hub SignalR)
```

`GET /api/agent-clis` retorna uma entrada por CLI de agente suportado (Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code, Kiro CLI): `agent`, `displayName`, `binary`, `installed`, `version`, `authStatus` (`0` desconhecido / `1` autenticado / `2` não autenticado — probe apenas de existência do arquivo de credencial, o conteúdo nunca é lido), `configDir`, `loginCommand`, `installHint`, `requiredTool`, `prerequisiteMet`. Baseado em `Taskboard:HomeDir` (padrão `$HOME`).

`POST /api/agent-clis/{kind}/install` executa o comando de instalação fixo e allowlisted do `{kind}` (nome do CLI, case-insensitive — `cline`, `qwen`, `kiro`, ...) em background: `202` enquanto executa, `200` quando um run anterior já terminou, `404` para kinds desconhecidos. `GET .../install/status` retorna `{ kind, state (0 idle / 1 running / 2 succeeded / 3 failed), startedAtUtc, exitCode, lines: [{ atUtc, stream, content }] }` — buffer em memória limitado (~500 linhas, sanitizado) que a UI consulta para renderizar o popup de instalação.

`/terminal-hub` é um hub SignalR que transmite bash PTYs interativos (`script -qfc`, `TERM=xterm-256color`) para a página `/terminal` — **múltiplas sessões em abas** por usuário autenticado (máx. 8), cada uma identificada por `sessionId` e multiplexada numa única conexão; sessões fecham após 30 minutos ociosas ou quando a conexão termina. Métodos do hub: `Open(string? repo) → string sessionId`, `Input(string sessionId, string data)`, `Resize(string sessionId, int cols, int rows)`, `Close(string sessionId)`; `repo` (`owner/name`) define o cwd da nova sessão para o workdir do clone — resolvido server-side e confinado à raiz do workspace — enquanto sessões vivas e reattach mantêm o cwd original; callbacks do cliente: `output(string sessionId, string chunk)`, `closed(string sessionId, string reason)` (`exited` / `idle-timeout` / `closed`). Controlado por `Taskboard:Terminal:Enabled` (padrão `true`, env `TASKBOARD_TERMINAL_ENABLED`, editável em runtime).

### VS Code Web (code-server)

```http
GET    /api/vscode/status
POST   /api/vscode/install
GET    /api/vscode/install/status
POST   /api/vscode/restart
GET    /api/vscode/workdir?repo=owner/name
GET    /api/vscode/open?repo=owner/name  → 302 → /vscode/?folder=<workdir> (link direto para o editor)
GET    /vscode                  → 302 → /vscode/ (barra final exigida pelo code-server)
GET    /vscode/{**}             → proxy reverso YARP → http://127.0.0.1:8377
```

`GET /api/vscode/status` retorna `{ installed, binaryPath?, version?, running, port, homeDirectory, workspaceRoot }`. `POST /api/vscode/install` executa o comando allowlisted fixo `bash -c "curl -fsSL https://code-server.dev/install.sh | sh -s -- --method=standalone"` em background (`202`/`200`, mesmo contrato de `install/status` dos CLIs de agente — buffer limitado de ~500 linhas sanitizadas, sem argumentos do cliente). `GET /api/vscode/workdir?repo=owner/name` resolve o workdir do card sob o workspace root → `{ path, exists }` (cai para o root quando o diretório do repo ainda não existe; entrada fora do padrão `owner/name` → `404`). `POST /api/vscode/restart` mata e recria o code-server gerenciado — kill → spawn → espera do listener, serializado dentro do process manager — e retorna `200` com o status pós-restart; use quando o editor trava e para de responder.

A rota `/vscode/{**}` é um proxy reverso YARP para o processo filho gerenciado `code-server` (`--bind-addr 127.0.0.1:<porta> --auth none --disable-telemetry`, spawn lazy e morto junto com o host): remove o prefixo `/vscode` (code-server é agnóstico de path — URLs relativas resolvem sob o `/vscode/` do browser), faz upgrade de WebSocket e exige a auth por cookie do app — a única porta de entrada, já que o code-server roda sem auth no loopback. `503` quando não instalado ou o processo falhou ao subir. Porta: `Taskboard:Vscode:Port` (padrão `8377`).

### Workspace root

`Taskboard:WorkspaceRoot` (padrão `~/repos`, criado sob demanda) é o diretório de trabalho default dos agent runs sem `RepoPath` explícito — clones caem em `<root>/<repo-name>` onde o nome é o último segmento de `owner/name` sanitizado para `[A-Za-z0-9._-]` (traversal não escapa do root). Os workdirs resolvidos alimentam o "Open in VS Code".

### Kanban GitHub

```http
GET   /api/github/repositories
GET   /api/github/repos/{owner}/{repo}/issues
GET   /api/github/repos/{owner}/{repo}/issues/{number}
POST  /api/github/repos/{owner}/{repo}/issues
PATCH /api/github/repos/{owner}/{repo}/issues/{number}
PUT   /api/github/repos/{owner}/{repo}/issues/{number}/column
PUT   /api/github/repos/{owner}/{repo}/issues/{number}/priority
POST  /api/github/repos/{owner}/{repo}/issues/{number}/labels
POST  /api/github/repos/{owner}/{repo}/issues/{number}/close
GET   /api/github/repos/{owner}/{repo}/issues/{number}/comments?take=50
POST  /api/github/repos/{owner}/{repo}/issues/{number}/comments
GET   /api/github/issues/{issueId}/history?take=50
GET   /api/github/repos/{owner}/{repo}/timeline?days=90
GET   /api/github/repos/{owner}/{repo}/metrics?days=90
GET   /api/github/repos/{owner}/{repo}/workflows
GET   /api/github/repos/{owner}/{repo}/workflows/{workflowId}/runs?take=10
POST  /api/github/repos/{owner}/{repo}/pulls
```

O estado do kanban é baseado em labels: `PATCH .../issues/{n}` edita `{ title?, body }` (corpo markdown renderizado sanitizado na UI); `PUT .../priority` `{ "priority": "none|urgent|high|medium|low" }` troca as labels `priority:*` (`none` remove); `POST .../close` `{ "resolution": "canceled|archived" }` fecha a issue — `canceled` também aplica a label `canceled` (coluna Canceled), `archived` fecha sem label de coluna (Archived). Todos retornam `200 { issue }`; enum inválido → `400`, issue desconhecida → `404`, anônimo → `401`. `GET .../issues/{number}` busca uma issue diretamente — diferente da lista, ignora a janela de visibilidade do board, então issues fechadas/antigas também resolvem (usado para montar o contexto do prompt do agente); `200 { issue }` ou `404` quando o número não existe.

Cada mutação do board também é persistida como `IssueHistoryEvent` (`column-moved` com `from`/`to`, `edited` com os campos alterados, `closed` com a resolução, `pipeline-run` com o id da execução do pipeline em `detail`) indexada pelo id da issue do GitHub — best-effort, nunca falha a mutação. `GET .../issues/{issueId}/history` mescla esses eventos com os agent runs da issue em `200 { items: [{ kind, occurredAt, agentType?, agentRunState?, finishedAt?, from?, to?, detail? }] }` do mais recente ao mais antigo — os dados da aba `Histórico` no dialog da issue.

Comentários da issue vivem no GitHub (nunca persistidos localmente): `GET .../issues/{n}/comments` retorna `200 { comments: [{ id, authorLogin, body, createdAt, updatedAt, htmlUrl }] }` em ordem cronológica, `POST` `{ "body" }` cria um (`400 { "error": "empty-body" }` em corpo vazio, `404 { "error": "issue-not-found" }`). A aba `Comentários` lista/posta, e o renderizador do prompt do agente anexa os comentários automaticamente numa seção `Comments:` limitada (~3k chars, omitida quando vazia, falha no fetch nunca bloqueia a execução).

O timeline do Gantt e as métricas de kanban vêm de `GET .../repos/{owner}/{repo}/timeline` → `200 { issues: [{ id, number, title, column, priority, assigneeLogin, createdAt, closedAt, milestoneNumber, milestoneDueOn, transitions: [{ at, from, to }] }], milestones: [{ number, title, dueOn, state }] }` e `GET .../repos/{owner}/{repo}/metrics` → `200 { leadTimeAvgDays, leadTimeMedianDays, cycleTimeAvgDays, throughputPerWeek: [{ weekStart, closed }], wip, openMedianAgeDays }`. Ambos aceitam `days` (padrão 90) — issues fechadas antes da janela são excluídas; as transições reconstroem os eventos `labeled`/`unlabeled` do GitHub mesclados com os `IssueHistoryEvent` locais (deduplicados por `(at, from, to)`); o fetch de timeline por issue é limitado (≤8 concorrentes) e best-effort. Repositório desconhecido → `404 { "error": "repo-not-found" }`.

A página `/workflow` é um monitor read-only de GitHub Actions: `GET .../workflows` → `200 { workflows: [{ id, name, path, state, htmlUrl, lastRun }] }` (cada workflow embute o run mais recente — buscado por workflow, limitado a 20 fetches e ≤8 concorrentes, best-effort: histórico não listável deixa `lastRun` nulo) e `GET .../workflows/{id}/runs?take=10` → `200 { runs: [{ id, name, displayTitle, runNumber, event, status, conclusion, headBranch, headSha, actorLogin, createdAt, updatedAt, runStartedAt, htmlUrl }] }`. Repo desconhecido → `404 { "error": "repo-not-found" }`. Os badges mapeiam `success`→verde, `failure`/`timed_out`/`startup_failure`/`action_required`→vermelho, `in_progress`/`queued`/`requested`/`waiting`/`pending`→âmbar pulsante (dirige o auto-refresh de 60s), demais→cinza.

`POST .../pulls` body `{ title, head, baseBranch, body? }` abre um pull request via Octokit → `201 { prUrl }`; usado pela ação `create-pr` do cockpit após o push da branch do worktree.


### Orquestração de Agentes

```http
GET    /api/agents
POST   /api/agents/executions
GET    /api/agents/{agentType}/models
PUT    /api/agents/{agentType}/models
DELETE /api/agents/{agentType}/models
GET    /api/agents/runs?issueId={id}&take={n}
GET    /api/agents/runs/active
GET    /api/agents/logs/{issueId}
GET    /api/agents/events?scopeKind={run|thread|issue}&scopeId={id}&after={seq}&take={n}
POST   /api/agents/control
POST   /api/agents/permissions/reply
GET    /api/agents/state?scopeKind={run|thread|issue}&scopeId={id}
POST   /api/agents/executions/{issueId}/cancel
```

`GET /api/agents` lista apenas agentes *elegíveis* — instalados no PATH, com CLI autenticado (probe de credencial) e habilitados em Settings → Agents; agentes em execução aparecem como `Busy`. `POST /api/agents/executions` retorna `400 { "error": "invalid-repository" }` quando `repositoryFullName` não é `owner/name`, `202` quando enfileirado ou `422 { "error": "agent-not-eligible" }` para agente desabilitado/não autenticado/não instalado.

O body das execuções aceita `modelTier` opcional (`"lite" | "normal" | "ultra"`, padrão `"normal"` — ausente em payloads antigos): o servidor mapeia `(agentType, tier)` para um modelo concreto via a tabela curada `AgentCliModels` e injeta a flag de modelo do CLI no argv (`claude --model sonnet`, `codex -m gpt-5.6-luna`, `devin --model swe`, `agy --dangerously-skip-permissions --model gemini-3.1-pro-low`, `opencode run --auto -m opencode/claude-sonnet-5`, …). CLIs sem flag de modelo headless (Cline, Continue, Kiro, OpenHands) não recebem flag em nenhum tier. Itens de `GET /api/agents/runs` trazem `modelTier` e o `modelName` resolvido (null em runs antigas e agentes gerenciados pela CLI).

`GET /api/agents/{agentType}/models` retorna o mapeamento efetivo por tier (`lite`/`normal`/`ultra`), a `source` (`override` ou `default`), os `defaults` curados e o `catalog` de modelos conhecidos para pickers; `422 { "error": "model-selection-unsupported" }` para CLIs gerenciadas. `PUT` salva override por CLI (`{ "lite", "normal", "ultra" }` — slots nulos mantêm o curado, nomes ≤128 chars) e `DELETE` o remove; overrides vencem a tabela curada na execução.

`GET /api/agents/{agentType}/models/available` retorna `{ models: [...] }` — os ids de modelo que a CLI instalada reporta via comando headless (`opencode models`, `devin models list`, `agy models`; probe limitado a 10s, cache de 5min — `?refresh=true` fura o cache, usado pelo botão Sync do dialog). Array vazio quando a CLI não tem probe documentado (Claude, Codex, …), não está instalada ou o probe falha; `422 model-selection-unsupported` para CLIs gerenciadas. O dialog de modelos mescla esses ids com o `catalog` curado num autocomplete editável.

`GET /api/agents/runs?issueId=` retorna os runs mais recentes da issue (`{ id, issueId, agentType, state, startedAt, finishedAt }`, mais novo primeiro; `state`: `0` Queued / `1` Running / `2` Succeeded / `3` Failed / `4` Canceled). `GET /api/agents/runs/active` retorna o run mais recente por issue — usado para os badges de agente nos cards do kanban.

`GET /api/agents/events` (SPEC-20260921-agent-execution-event-pipeline) faz replay do fluxo normalizado de eventos de execução persistido por escopo — `scopeKind` é `run` (pipeline do cockpit), `thread` (sessão de agente do AI Chat) ou `issue` (run one-shot do board). Eventos carregam `sequence` (monotônica por escopo), `kind` (`lifecycle|message|thought|plan|tool_call|tool_output|permission|output|diff|verification|metric|error|approval|steer|activity|commands|session_info|terminal`), correlação opcional `stageId`/`sessionId`/`toolCallId`/`parentEventId`/`messageId`/`planId`/`patchOp`, `payloadJson` (redigido + truncado) e `rawJson`. `after`/`take` paginam por sequência (`400` para escopo inválido). Eventos ao vivo também fluem pelo SignalR `/agent-log-hub` no grupo `agent:{scopeKind}:{scopeId}` (`SubscribeToScope`, `ReceiveAgentEvent`). Sessões ACP implementam o protocolo v1 real (SPEC-20260921-acp-v1-conformance): `initialize` com negociação de capabilities (`agentCapabilities`, `authMethods` → `authenticate` quando exigido) → `session/new` (com `cwd`, `mcpServers` — o MCP RAG/Knowledge configurado é injetado — e `additionalDirectories` quando suportado) ou `session/resume`/`session/load` após reconnect → `session/prompt` com content blocks. `session/cancel` é notification e permissões pendentes são respondidas `cancelled`; replies de `session/request_permission` mapeiam `allow`/`deny`/`always` para os kinds tipados das options (`allow_once`/`allow_always`/`reject_once`/`reject_always`). `fs/read_text_file`/`fs/write_text_file` client-side (sandboxed ao workspace, escrita passa pelo gate de permissão) e `terminal/*` (opt-in via `Taskboard:Acp:ClientTerminal`) são servidos quando anunciados; métodos não declarados recebem `-32601`. `Taskboard:Acp:SessionRuns=true` roteia runs one-shot do board/cockpit pela mesma máquina de sessão; `Taskboard:Acp:TcpPort=N` conecta a um servidor ACP já em execução (ex. `copilot --acp --port N`) em vez de spawnar subprocesso.

A negociação de protocolo é version-aware (SPEC-20260921-acp-v2-readiness): o cliente oferece `protocolVersion` até `Taskboard:Acp:MaxProtocolVersion` (default `1`) e escolhe um `IAcpDialect` por conexão a partir da resposta do agente — um agente v1-only cai transparentemente para v1 mesmo quando `2` é oferecido, e respostas acima da oferta falham com `unsupported_version`. ACP v2 é estritamente opt-in enquanto draft (`Taskboard:Acp:MaxProtocolVersion=2`): `session/prompt` retorna um ack `messageId` e o turno fecha num `state_update` `idle` com `stopReason` (`ITurnTracker` por dialeto); `tool_call_update`, `agent_message*`/`plan_update` são upserts correlacionados por `toolCallId`/`messageId`/`planId` e os eventos carregam `patchOp` (`append`/`replace`/`clear`) para a timeline dobrá-los no mesmo modelo normalizado; `session/resume`+`replayFrom` substitui `session/load` e `session/list`/`session/delete` viram capabilities baseline. A superfície v1 removida (`fs/*`, `terminal/*`, `session/load`, `session/set_mode`) nunca é servida (`-32601`) nem enviada em conexões v2 — as ferramentas do cliente rodam como servidores MCP. Arrays batch NDJSON são tolerados e variantes de update desconhecidas/extensões `_` são preservadas em raw para forward compatibility.

`POST /api/agents/control` (SPEC-20260921-board-cockpit-agent-observability) é a superfície de controle unificada — body `{ scopeKind, scopeId, action, stageId?, content?, configId? }`. Pares suportados: `issue`+`cancel`/`steer`/`retry` (run do board — resolve para a execução de pipeline mais recente da issue, com fallback para o orquestrador one-shot legado; `retry` usa `stageId` ou o estágio falho), `run`+`cancel`/`steer`/`retry` (pipeline do cockpit; `retry` exige `stageId`, `steer` exige `content`), `thread`+`cancel`/`steer`/`set_config`/`set_mode` (sessão do AI Chat; `set_config` exige `configId`+`content` como valor da option — ex. trocar o modelo — e mapeia para `session/set_config_option` do ACP, `set_mode` mapeia para `session/set_mode` em agentes que expõem `modes`). `400` para escopo/ação desconhecida, `409` quando a ação não é suportada ou o alvo não tem run/sessão ativa, `202`/`200` em caso de sucesso. Toda ação aceita emite um evento `lifecycle`/`steer` normalizado no fluxo do escopo.

`POST /api/agents/permissions/reply` body `{ scopeKind, scopeId, requestId, outcome, comment? }` roteia respostas de permissão por escopo: `thread` → o gate de permissão da sessão ACP (`410` quando a request expirou/é desconhecida), `run` → o gate de estágio do pipeline quando `requestId` é `stage:{stageKey}` (`outcome` `deny` rejeita, qualquer outro aprova), `issue` → o gate de estágio do pipeline da issue para requestIds com prefixo `stage:` (`409` quando não há run ativo ou a request não é de estágio).

`GET /api/agents/state?scopeKind&scopeId` retorna `{ scopeKind, scopeId, state, lastEventSequence, sessionId?, sessionInfoJson? }` — `run` resolve o status do pipeline explicitamente (`running|waiting_permission|awaiting_retry|paused|completed|stopped|queued`, `404` run desconhecido), `thread` reporta `running`/`idle` pela sessão ACP ativa mais o snapshot do peer negociado (`sessionInfoJson`: `protocolVersion`, identidade do agente, `modes`, `configOptions`, `authMethods` — o catálogo real de modelos do agente vive nas `configOptions` com `category:"model"`), `issue` resolve o status da execução de pipeline mais recente da issue (registros legados de `AgentRun` como fallback). Usado pela UI para reconstruir a timeline após reconnect. Eventos de pipeline de runs vinculados a issues são espelhados nos escopos `run:{runId}` e `issue:{issueId}`, de modo que o log da tarefa no Board e o Cockpit compartilham o mesmo fluxo normalizado.

### Harness — Isolamento de Workspace (E6)

```http
POST   /api/harness/worktrees
GET    /api/harness/worktrees/{runId}
GET    /api/harness/worktrees/{runId}/diff
DELETE /api/harness/worktrees/{runId}?force={true|false}
```

`POST` com body `{ runId, repositoryPath, baseBranch?, taskSlug, retainOnFailure? }` → `201` com `{ worktreeId, runId, path, branch, status, repositoryPath, baseBranch, commitSha?, retainOnFailure, createdAt, updatedAt, version }`. Cria um `git worktree` dedicado em `~/.taskboard/worktrees/{runId}` numa nova branch `feature/agent-{runId}-{slug}` (sufixo incremental em colisão); `baseBranch` usa `main` como padrão na API e `HEAD` quando o orquestrador isola um run. Idempotente por `runId` — sessão ativa é reutilizada.

`GET .../diff` → `200 { filesChanged, insertions, deletions, files: [{ path, status }], patch }` — `git status --porcelain` + `git diff <base>` (two-dot) cobrindo mudanças commitadas e pendentes. `DELETE` executa `git worktree remove` (`?force=true` adiciona `--force`), faz prune e remove o diretório em caso de lock, marcando a sessão `Removed` → `204`. `runId` desconhecido → `400`; caminhos são confinados ao root aprovado (traversal rejeitado).

Quando um run enfileirado carrega `RepoPath` apontando para um repositório git, o `AgentOrchestrationService` isola automaticamente: o run executa com cwd dentro do worktree e `RetainOnFailure` ligado — sucesso marca `Completed` (mantido para revisão de diff), falha/cancelamento marca `RetainedForInspection`. Falha no isolamento degrada para execução direta e é registrada em log.

### Harness — Contexto & Memória (E7)

```http
POST   /api/harness/context/compile
POST   /api/harness/memory
GET    /api/harness/memory?repositoryFullName={owner/name}&query={q}&take={n}
DELETE /api/harness/memory/{id}
```

`POST context/compile` com body `{ worktreePath, agentType, maxTokenBudget }` → `200 { systemPrompt, estimatedTokens, injectedFiles, memoriesInjectedCount }`. Descobre arquivos de instrução (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.github/copilot-instructions.md`) recursivamente, injeta metadados `<env>`, branch/commit/status do git e o bloco `<project_memory>` (escopo pelo remote `origin`). Secrets de ambiente nunca entram no prompt.

`POST memory` com body `{ repositoryFullName, topic, content, tags?, type? }` → `201` com o item persistido (`type`: `Fact` | `ArchitecturalDecision` | `LessonLearned`, padrão `Fact`). `GET` lista memórias por repositório — `query` ativa busca lexical ranqueada (tags > topic > content). `DELETE` remove por id → `204`. Registros de memória são apenas metadados — nunca armazene secrets.

### Harness — Security Gateway (E8)

```http
POST /api/harness/security/evaluate
```

Body `{ toolName, command, worktreePath, policy? }` (`policy`: `Strict` | `Standard` | `Autonomous`, padrão `Standard`) → `200 { allowed, riskLevel, requiresApproval, reason }`. `riskLevel`: `Safe` | `WorkspaceWrite` | `Dangerous`. File tools (`write_file`, `read_file`, …) são validadas contra o jail do `worktreePath` — escapes retornam `400` com código `Taskboard:00025` (`SECURITY_ACCESS_DENIED`). Comandos shell passam por um classificador lexer (segmentos `&&`/`||`/`;`/`|`, quotes, redirects, prefixos de env); alvos fora do jail são negados de forma permanente (`allowed: false`, `requiresApproval: false`) — demais comandos `Dangerous` exigem aprovação em Strict/Standard e são bloqueados em Autonomous. Binários desconhecidos classificam como Dangerous (fail-closed). `IPermissionGateway.ScrubSecrets` mascara padrões de credenciais (`ghp_…`, `github_pat_…`, `sk-…`, chaves AWS, tokens `Bearer`, chaves PEM) com `[REDACTED_SECRET]` para pipelines de log/stream.

### Harness — Verification Loop (E9)

```http
POST /api/harness/verification/run
```

Body `{ worktreePath, solutionFile, minCoverageThreshold, enforceFormat?, maxAttempts?, attempt? }` → `200 { isSuccess, status, compilationErrors, testSummary, coveragePercent, feedbackPrompt }`. `status`: `Passed` | `FormatFailed` | `BuildFailed` | `TestsFailed` | `CoverageRegression` | `TestTimeout` | `EscalatedToHuman`. Executa `dotnet format --verify-no-changes` (opt-in), `dotnet build -c Release -p:TreatWarningsAsErrors=true` e `dotnet test --no-build` com TRX + cobertura XPlat (timeout 120s). Cada run persiste uma linha de evidência `VerificationReport`. `feedbackPrompt` é o payload markdown de correção.

**Integração no loop do agente:** `AgentExecutionRequest` aceita os campos opt-in `verifySolutionFile` / `verifyMinCoverage` / `verifyMaxAttempts` (padrão 3 tentativas). Após um run bem-sucedido, `IVerificationLoop` verifica o worktree e re-invoca o agente com o `feedbackPrompt` em caso de falha — retries esgotados marcam o run `Failed` + `EscalatedToHuman` em vez de mover para revisão.

### Harness — CLI DB Reader (E10, interno)

Sem endpoints HTTP — camada interna de acesso consumida pelo `cli-metrics` (E11). `CliDatabaseMap` registra os SQLite que cada CLI gerenciado mantém em `$HOME`; `ICliDatabaseLocator` resolve paths/globs para `Available`/`Missing`; `ICliDatabaseReader` abre as fontes estritamente `Mode=ReadOnly` (arquivos WAL/ocupados são lidos de uma cópia temporária removida após o uso) com limites de linhas/timeout/tamanho, acesso somente a tabelas whitelisted, rejeição de tabelas negadas e exclusão de colunas com nome de segredo; `ICliDbExtractor`s por CLI emitem `CliSessionRecord`/`CliUsageRecord` normalizados com cursors watermark opacos `{arquivo}|{rowid}`. Fingerprints de schema (`user_version`, `application_id`, colunas whitelisted) gateiam a extração — drift reporta `SchemaDrifted` em vez de lançar exceção.

### Harness — CLI Metrics (E11)

Ingestão incremental dos extractors do E10 em `CliMetricSource` / `CliSessionMetric` / `CliDailyUsageAggregate` (dedupe por `(SourceId, ExternalId)`, skip por assinatura de arquivo, watermark por fonte, retenção de 90 dias para dados brutos — agregados mantidos). Auth obrigatória (cookie ou `X-Api-Key`).

| Método | Path | Notas |
|--------|------|-------|
| POST | `/api/local/cli-metrics/sync` | Sync manual — `200` com `{ state, inFlight, sourcesSynced, sessionsIngested }`; `inFlight: true` quando já existe um sync; `404` quando `Taskboard:CliMetrics:Enabled=false` |
| GET | `/api/local/cli-metrics/sources` | Status por fonte: `kind`, `sourceName`, `status` (Available/Missing/Error/SchemaDrifted), `schemaDrifted`, `lastSyncUtc`, `lastError`, `rowCount` |
| GET | `/api/local/cli-metrics/summary?period=` | `period` = `7d` (padrão do /agents) `30d` `90d` `all`; totais + buckets por kind + por dia com `lastActivityUtc` |
| GET | `/api/local/cli-metrics/sessions?kind=&from=&to=&take=` | Sessões, mais recentes primeiro; `take` limitado a 1–500 (padrão 100) |

Sync em background: `CliMetricsSyncService` (`PeriodicTimer`, intervalo `Taskboard:CliMetrics:SyncIntervalMinutes`, padrão 15, mín 1) roda uma passagem no startup + syncs periódicos via `CliMetricsSyncCoordinator` single-flight. Feed FinOps: `ICliUsageMetricsProvider.GetUsageAsync` expõe agregados de tokens+sessões por kind/modelo para o `ade-observability-finops` (E14).

### Harness — Pipelines Multi-Agente (E12)

```http
GET  /api/harness/pipelines/templates
POST /api/harness/pipelines/start
GET  /api/harness/pipelines/{id}
POST /api/harness/pipelines/{id}/stages/{stageKey}/approve
POST /api/harness/pipelines/{id}/stages/{stageKey}/retry
POST /api/harness/pipelines/{id}/cancel
```

`GET templates` → `200` com os quatro templates DAG embutidos: `standard-feature` (architect → approval-gate → builder → verifier → reviewer), `quick-patch` (builder → verifier), `test-driven` (architect → tester → builder → verifier) e `single-agent` (builder → verifier).

`POST start` body `{ templateId, repositoryFullName, repositoryPath, baseBranch, issueId?, initialPrompt, maxBudgetUsd?, agentOverride?, tierOverride?, skipVerification? }` → `201` com `{ pipelineExecutionId, templateId, status, worktreePath?, stages: [{ stageKey, name, kind, status, role?, agent?, attempts, handoffSummary?, lastError?, dependsOn }] }`. Template desconhecido ou grafo cíclico → `400 INVALID_PIPELINE_DAG`. `agentOverride`/`tierOverride`/`skipVerification` são exclusivos de `single-agent` — substituem o agente/tier do estágio AgentWork e removem o estágio Verification; enviá-los com outro template → `400`.

Todos os estágios de uma execução compartilham um único git worktree do `IWorkspaceIsolationService`; a saída de cada estágio é sintetizada em `handoffSummary` e injetada como contexto upstream nos dependentes. `kind`: `AgentWork` | `Approval` | `Verification`; `status`: `Pending` | `Running` | `WaitingApproval` | `Completed` | `Failed` | `Skipped`. `status` do pipeline: `Running` | `WaitingApproval` | `AwaitingRetry` | `Paused` | `Completed` | `Failed` | `Cancelled`.

`POST .../approve` body `{ comment? }` libera um gate `WaitingApproval` e despacha os dependentes → `200`. `POST .../retry` body `{ adjustedPrompt? }` reenfileira um estágio `Failed` sem reiniciar o pipeline → `202`. `POST .../cancel` encerra os processos em voo e transiciona o pipeline para `Cancelled` → `200`. Aprovar/retentar estágio em estado incompatível → `400`; pipeline desconhecido → `404`.

O despacho roda no timer do `PipelineEngineService` mais kicks síncronos após `start`/`approve`/`retry`; o `PipelineEngine` resolve o DAG por escopo de execução para que estágios paralelos nunca compartilhem um `DbContext`.

### Harness — Cockpit (HITL)

```http
GET  /api/harness/runs
POST /api/harness/runs
GET  /api/harness/runs/{id}
GET  /api/harness/runs/{id}/events
POST /api/harness/runs/{id}/steer
POST /api/harness/runs/{id}/pause
POST /api/harness/runs/{id}/resume
POST /api/harness/runs/{id}/approvals/{requestId}
POST /api/harness/runs/{id}/create-pr
```

"Runs" do cockpit são execuções de pipeline. `GET runs` → `200` com as execuções recentes (mais novas primeiro, mesmo shape de `GET /api/harness/pipelines/{id}`). `POST runs` body `{ templateId, repositoryFullName, baseBranch, issueId?, specPath?, prompt, maxBudgetUsd?, agentOverride?, tierOverride?, skipVerification? }` resolve `repositoryPath` no servidor via `IWorkspacePathResolver` (`~/repos/<name>`, confinado ao workspace root), anexa o conteúdo de `specPath` ao prompt quando legível e inicia o pipeline → `201`. Os overrides seguem a mesma regra `single-agent`-only do `POST pipelines/start`. Com `issueId` presente, um `IssueHistoryEvent` `pipeline-run` vincula a issue ao id do run (aba Histórico da issue → "Open in Cockpit"). `GET {id}` → `200 { execution, telemetry?, worktree? }` ou `404`.

`GET {id}/events` → `200` com os `CockpitEventDto[]` buffered em memória (`{ runId, timestampUtc, kind, title, payloadJson? }`; kinds: `stage`, `agent_output`, `verification`, `approval`, `steer`) para replay em reconexão/join tardio — os eventos também são transmitidos ao vivo pelo hub SignalR abaixo.

`POST {id}/steer` body `{ instruction }` → `202`; a instrução é enfileirada (`ISteerQueue`) e anexada ao prompt do próximo estágio despachado como "operator steer". Run desconhecido → `404`.

`POST {id}/pause` → `200` congela o DAG entre estágios: o estágio em voo completa, nenhum novo é despachado e o tick do engine ignora a execução (sem worktree attach, sem cancel por budget) até o resume. `POST {id}/resume` → `200` redespacha os estágios pendentes. Ambos publicam um evento `status` no cockpit ("Run paused"/"Run resumed"); transições inválidas → `409 InvalidPipelineState`, run inexistente → `404`. `status` do pipeline ganha `Paused` (badge warning). Execuções pausadas nunca são colhidas como stale — o reaper só cobre registros `AgentRun` legados.

`POST {id}/approvals/{requestId}` body `{ action, comment? }` — `requestId` usa a convenção `stage:<stageKey>` publicada pelo `RequireApproval`; `Allow` aprova o estágio `WaitingApproval`, `Deny` rejeita com o comentário como motivo → `200`. Run/request desconhecido → `404`.

`POST {id}/create-pr` body `{ title, body? }` → `201 { prUrl }` — exige run `Completed`; commita as mudanças pendentes do worktree, dá push na branch e abre o PR via Octokit. Quando o run está vinculado a uma issue do board, o card move para `in_review` e o link do PR é comentado na issue (best-effort — falhas no board não derrubam a request). Run não completado → `409`; desconhecido → `404`.

```http
GET /api/harness/worktrees/{runId}/files?path=
GET /api/harness/worktrees/{runId}/files/content?path=
```

Explorer do worktree (SPEC-20260921 RF-003): `files?path=` lista os filhos de um diretório — `{ path, entries: [{ name, path, directory, sizeBytes? }], truncated }`, diretórios primeiro, `.git` oculto, cap de 500 entradas. `files/content?path=` → `{ path, content?, sizeBytes, truncated, binary }` — texto limitado a 512 KB (`truncated`), binários detectados por NUL retornam `binary: true` sem conteúdo. Todos os paths ficam confinados ao worktree do run (`..`/absoluto/symlink que escapa → `400`); run sem worktree ou path inexistente → `404`. `WorkspaceDiffFileDto` também carrega `insertions`/`deletions` por arquivo (de `--numstat`), que a aba Diff do cockpit renderiza como cards colapsáveis por arquivo (RF-004).

Hub SignalR `/harness-cockpit-hub` (autenticado): client → server `JoinRunGroup(runId)` / `LeaveRunGroup(runId)`; server → client `ReceiveCockpitEvent(CockpitEventDto)` e `RequireApproval(ApprovalRequestDto)` (`{ runId, requestId, title, description, options }`). As páginas Blazor são `/cockpit` (lista + start) e `/cockpit/runs/{id}` (timeline ao vivo, aba Logs com follow-scroll + filtros de stage/stream + copiar/baixar, aba Arquivos explorer, aba Diff colapsável por arquivo, barra de steer, modal de aprovação, header FinOps).

### Living Specs (E13)

```http
GET  /api/specs?status={Draft|Approved|InImplementation|Done|Deprecated}&q={texto}
GET  /api/specs/{specId}
POST /api/specs/{specId}/status
GET  /api/specs/drift-report
```

`GET /api/specs` → `200` com as linhas do catálogo (`{ id, title, type, status, rawStatus, date, ticket, requirementsCount, acceptanceCriteriaCount, tasksTotal, tasksDone, warnings[] }`). `GET {id}` → `200` com o detalhe completo (requisitos, critérios BDD, tasks, arquivos referenciados, markdown bruto) ou `404`. `POST {id}/status` body `{ status }` reescreve apenas a célula `Status` da tabela de metadados no `.md` → `200`; status desconhecido → `400` (`Taskboard:00033`); spec desconhecida → `404`.

`GET drift-report` → `200 { totalSpecs, staleSpecsCount, driftItems: [{ specId, currentStatus, suggestedStatus, reason, missingFiles }] }`. Specs Draft/Approved/InImplementation cujos arquivos em "Files to create or modify" já existem em disco são sinalizadas → `Done`; specs `Done` referenciando arquivos removidos → `Deprecated`.

As specs são lidas de `Taskboard:SpecsDir` (padrão: `.specs/` mais próximo subindo a partir do diretório do app) — parseadas a cada request pelo `MarkdigSpecParser`, então o arquivo em disco é sempre a fonte da verdade. A página Blazor é `/specs`.

Os quatro endpoints aceitam `?repo=owner/name` (SPEC-20260920 RF-005): o diretório de specs resolve server-side para `<raiz do workspace>/<name>/.specs` — clone ausente ou sem `.specs/` → catálogo vazio (`404` para `{id}`); `repo` malformado → `400`. Sem `repo`, usa o diretório padrão configurado. `drift-report` com `repo` faz varredura sob demanda (sem cache).

### Observabilidade & FinOps (E14)

```http
GET /api/harness/finops/summary?period={last-7-days|last-30-days|all}
GET /api/harness/runs/{id}/telemetry
```

`GET summary` → `200 { totalCostUsd, totalTokens, runsCount, costByAgent{}, costByModel{}, dailyCosts: [{ date, costUsd, totalTokens }] }` agregado de `RunCostMetrics` (SQLite). `period` padrão: `last-30-days`.

`GET telemetry` → `200 { runId, totalTokens, inputTokens, outputTokens, cacheTokens, costUsd, durationSeconds?, budgetCapUsd?, metrics[] }` ou `404` quando o run não tem métricas. Aceita o id `Guid` com ou sem hífens.

Modelo de custo: linhas `RunCostMetric` são gravadas quando o agente reporta uso de tokens (linhas JSON `usage`/`token_count` no stdout parseadas pelo `TokenUsageParser`, ou `AgentExecutionResult.Usage`). O custo USD usa matemática `decimal` exata contra a tabela seedada `ModelPriceRates` — match por prefixo mais longo, fallback `"*"` (RF-002).

Teto de orçamento: `AgentExecutionRequest.maxBudgetUsd` (por run) e `PipelineStartRequest.maxBudgetUsd` (por pipeline) — quando o custo acumulado cruza o teto o run é cancelado e finaliza como `AgentRunState.BudgetExceeded` (mid-flight quando o CLI streama usage, pós-run caso contrário); pipelines acima do teto são canceladas antes do próximo dispatch de estágio e os estágios restantes são pulados (RF-003).

Telemetria: cada run/estágio/verificação emite spans `System.Diagnostics.Activity` da fonte `Taskboard.Harness` (`harness.run`, `harness.stage`, `harness.tool_call`, `harness.verification`) com tags `harness.run_id`, `agent.type`, `model.name`, `tokens.*`, `cost.usd` (RF-004). Os spans ficam em processo a menos que `Taskboard:Telemetry:OtlpEndpoint` esteja configurado no `appsettings.json` — nada é exportado sem configuração explícita (guardrail §8). O dashboard Blazor é `/finops`.

## SSE

### Eventos globais

```http
GET /api/events
Accept: text/event-stream
```

Stream reservado para eventos globais (nenhum produtor emite nele hoje — eventos de IA por thread usam o endpoint abaixo).

### Eventos por thread de IA

```http
GET /api/local/ai/threads/:id/events
Accept: text/event-stream
```

`GET .../events` é dual-mode: `Accept: application/json` retorna um snapshot único `200 { events: [{ id, threadId, role, content, createdAt }] }`; qualquer outro Accept abre stream SSE — o backlog persistido é reemitido como frames `ai_chat.event` seguidos de eventos ao vivo `ai_chat.event` (novas mensagens, incluindo deltas do assistente em streaming) e `ai_chat.run` (mudanças de status do run: `running`/`completed`/`failed`). `POST .../events` `{ role: "user|assistant|activity|error", content }` persiste e publica um evento; `POST .../runs` inicia um run em background executado pela CLI de agente vinculada à thread — one-shot, transcript como prompt (o assistente responde à última mensagem do usuário); `DELETE /api/local/ai/threads/:id` remove a thread com seus eventos e runs (204 | 404).

## Contrato de Erros

Todos os erros usam RFC 7807 `ProblemDetails` com `ErrorCode` opcional:

```json
{
  "type": "https://taskboard/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "Status deve ser um dos: todo, in_progress, in_review, done, blocked, canceled",
  "ErrorCode": "VALIDATION_ERROR"
}
```

Código especial `VERSION_CONFLICT` com HTTP 409 para falhas de concorrência otimista.

## Envelope JSON

As respostas são JSON puro. O CLI `taskctl` envelopa as respostas da API:

```json
{
  "ok": true,
  "data": { ... },
  "error": null
}
```

Veja `.specs/SPEC-002-rest-api.md` e `.specs/SPEC-003-cli.md` para contratos completos.
