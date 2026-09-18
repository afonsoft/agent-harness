# Documentação da API

## Endpoints REST

### Projetos

```http
GET    /api/projects
POST   /api/projects
GET    /api/projects/:id
PUT    /api/projects/:id
POST   /api/projects/:id/archive
DELETE /api/projects/:id
```

### Tarefas

```http
GET    /api/projects/:id/tasks
POST   /api/projects/:id/tasks
GET    /api/tasks/:id
PUT    /api/tasks/:id
POST   /api/tasks/:id/archive
DELETE /api/tasks/:id
POST   /api/tasks/:id/comments
POST   /api/tasks/:id/attachments
POST   /api/tasks/:id/move
POST   /api/tasks/:id/relations
```

### Comentários e Anexos

```http
GET    /api/comments/:id
PUT    /api/comments/:id
DELETE /api/comments/:id
GET    /api/attachments/:id
PUT    /api/attachments/:id
DELETE /api/attachments/:id
```

### Labels

```http
GET    /api/projects/:id/labels
POST   /api/projects/:id/labels
DELETE /api/projects/:id/labels/:label
```

### Contexto e Storage

```http
GET    /api/context
PUT    /api/context
GET    /api/client-storage
PUT    /api/client-storage
```

### IA

```http
GET    /api/local/ai/threads
POST   /api/local/ai/threads
GET    /api/local/ai/threads/:id/events
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
PUT    /api/device-workspaces
```

### Jira

```http
GET    /api/local/jira-connection
POST   /api/local/jira-connection
POST   /api/local/jira-connection/sync
```

### Busca

```http
POST   /api/search/semantic
GET    /api/search/suggestions
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
PUT    /api/mcp/rag
```

`PUT /api/mcp/rag` aceita `{ "name", "url", "apiKey" }` — campos `null` preservam o valor gravado; `url` vazia remove a entrada gerenciada de todos os agentes habilitados. O provisionamento faz merge da entrada `<name>` em `~/.config/devin/mcp_config.json`, `~/.claude.json`, `~/.codex/config.toml`, `~/.config/opencode/opencode.json`, `~/.openhands/mcp.json`, `~/.kimi-code/mcp.json`, `~/.grok/config.toml`, `~/.qwen/settings.json`, `~/.copilot/mcp-config.json` e `~/.kiro/settings/mcp.json`; Antigravity e Cline são provisionados via CLI (`agy mcp add/remove`, `cline mcp add/remove`); Continue recebe um arquivo JSON em `~/.continue/mcpServers/<name>.json` (escrita atômica, backup `.bak`, `0600`). A API key nunca é retornada por nenhum endpoint. Chaves de configuração: `Taskboard:Rag:ServerName` / `Taskboard:Rag:Url` / `Taskboard:Rag:ApiKey` (aliases de env `TASKBOARD_RAG_NAME` / `TASKBOARD_RAG_URL` / `TASKBOARD_RAG_API_KEY`), persistidas como overrides em SQLite.

### CLI Agents e Terminal

```http
GET    /api/agent-clis
POST   /api/agent-clis/{kind}/install
GET    /api/agent-clis/{kind}/install/status
POST   /terminal-hub/negotiate   (hub SignalR)
```

`GET /api/agent-clis` retorna uma entrada por CLI de agente suportado (Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code, Kiro CLI): `agent`, `displayName`, `binary`, `installed`, `version`, `authStatus` (`0` desconhecido / `1` autenticado / `2` não autenticado — probe apenas de existência do arquivo de credencial, o conteúdo nunca é lido), `configDir`, `loginCommand`, `installHint`, `requiredTool`, `prerequisiteMet`. Baseado em `Taskboard:HomeDir` (padrão `$HOME`).

`POST /api/agent-clis/{kind}/install` executa o comando de instalação fixo e allowlisted do `{kind}` (nome do CLI, case-insensitive — `cline`, `qwen`, `kiro`, ...) em background: `202` enquanto executa, `200` quando um run anterior já terminou, `404` para kinds desconhecidos. `GET .../install/status` retorna `{ kind, state (0 idle / 1 running / 2 succeeded / 3 failed), startedAtUtc, exitCode, lines: [{ atUtc, stream, content }] }` — buffer em memória limitado (~500 linhas, sanitizado) que a UI consulta para renderizar o popup de instalação.

`/terminal-hub` é um hub SignalR que transmite bash PTYs interativos (`script -qfc`, `TERM=xterm-256color`) para a página `/terminal` — **múltiplas sessões em abas** por usuário autenticado (máx. 8), cada uma identificada por `sessionId` e multiplexada numa única conexão; sessões fecham após 30 minutos ociosas ou quando a conexão termina. Métodos do hub: `Open() → string sessionId`, `Input(string sessionId, string data)`, `Resize(string sessionId, int cols, int rows)`, `Close(string sessionId)`; callbacks do cliente: `output(string sessionId, string chunk)`, `closed(string sessionId, string reason)` (`exited` / `idle-timeout` / `closed`). Controlado por `Taskboard:Terminal:Enabled` (padrão `true`, env `TASKBOARD_TERMINAL_ENABLED`, editável em runtime).

### VS Code Web (code-server)

```http
GET    /api/vscode/status
POST   /api/vscode/install
GET    /api/vscode/install/status
GET    /api/vscode/workdir?repo=owner/name
GET    /api/vscode/open?repo=owner/name  → 302 → /vscode/?folder=<workdir> (link direto para o editor)
GET    /vscode                  → 302 → /vscode/ (barra final exigida pelo code-server)
GET    /vscode/{**}             → proxy reverso YARP → http://127.0.0.1:8377
```

`GET /api/vscode/status` retorna `{ installed, binaryPath?, version?, running, port, homeDirectory, workspaceRoot }`. `POST /api/vscode/install` executa o comando allowlisted fixo `bash -c "curl -fsSL https://code-server.dev/install.sh | sh -s -- --method=standalone"` em background (`202`/`200`, mesmo contrato de `install/status` dos CLIs de agente — buffer limitado de ~500 linhas sanitizadas, sem argumentos do cliente). `GET /api/vscode/workdir?repo=owner/name` resolve o workdir do card sob o workspace root → `{ path, exists }` (cai para o root quando o diretório do repo ainda não existe; entrada fora do padrão `owner/name` → `404`).

A rota `/vscode/{**}` é um proxy reverso YARP para o processo filho gerenciado `code-server` (`--bind-addr 127.0.0.1:<porta> --auth none --disable-telemetry`, spawn lazy e morto junto com o host): remove o prefixo `/vscode` (code-server é agnóstico de path — URLs relativas resolvem sob o `/vscode/` do browser), faz upgrade de WebSocket e exige a auth por cookie do app — a única porta de entrada, já que o code-server roda sem auth no loopback. `503` quando não instalado ou o processo falhou ao subir. Porta: `Taskboard:Vscode:Port` (padrão `8377`).

### Workspace root

`Taskboard:WorkspaceRoot` (padrão `~/repos`, criado sob demanda) é o diretório de trabalho default dos agent runs sem `RepoPath` explícito — clones caem em `<root>/<repo-name>` onde o nome é o último segmento de `owner/name` sanitizado para `[A-Za-z0-9._-]` (traversal não escapa do root). Os workdirs resolvidos alimentam o "Open in VS Code".

### Kanban GitHub

```http
GET   /api/github/repositories
GET   /api/github/repos/{owner}/{repo}/issues
POST  /api/github/repos/{owner}/{repo}/issues
PATCH /api/github/repos/{owner}/{repo}/issues/{number}
PUT   /api/github/repos/{owner}/{repo}/issues/{number}/column
PUT   /api/github/repos/{owner}/{repo}/issues/{number}/priority
POST  /api/github/repos/{owner}/{repo}/issues/{number}/labels
POST  /api/github/repos/{owner}/{repo}/issues/{number}/close
GET   /api/github/repos/{owner}/{repo}/issues/{number}/comments?take=50
POST  /api/github/repos/{owner}/{repo}/issues/{number}/comments
GET   /api/github/issues/{issueId}/history?take=50
```

O estado do kanban é baseado em labels: `PATCH .../issues/{n}` edita `{ title?, body }` (corpo markdown renderizado sanitizado na UI); `PUT .../priority` `{ "priority": "none|urgent|high|medium|low" }` troca as labels `priority:*` (`none` remove); `POST .../close` `{ "resolution": "canceled|archived" }` fecha a issue — `canceled` também aplica a label `canceled` (coluna Canceled), `archived` fecha sem label de coluna (Archived). Todos retornam `200 { issue }`; enum inválido → `400`, issue desconhecida → `404`, anônimo → `401`.

Cada mutação do board também é persistida como `IssueHistoryEvent` (`column-moved` com `from`/`to`, `edited` com os campos alterados, `closed` com a resolução) indexada pelo id da issue do GitHub — best-effort, nunca falha a mutação. `GET .../issues/{issueId}/history` mescla esses eventos com os agent runs da issue em `200 { items: [{ kind, occurredAt, agentType?, agentRunState?, finishedAt?, from?, to?, detail? }] }` do mais recente ao mais antigo — os dados da aba `Histórico` no dialog da issue.

Comentários da issue vivem no GitHub (nunca persistidos localmente): `GET .../issues/{n}/comments` retorna `200 { comments: [{ id, authorLogin, body, createdAt, updatedAt, htmlUrl }] }` em ordem cronológica, `POST` `{ "body" }` cria um (`400 { "error": "empty-body" }` em corpo vazio, `404 { "error": "issue-not-found" }`). A aba `Comentários` lista/posta, e o renderizador do prompt do agente anexa os comentários automaticamente numa seção `Comments:` limitada (~3k chars, omitida quando vazia, falha no fetch nunca bloqueia a execução).

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
POST   /api/agents/executions/{issueId}/cancel
```

`GET /api/agents` lista apenas agentes *elegíveis* — instalados no PATH, com CLI autenticado (probe de credencial) e habilitados em Settings → Agents; agentes em execução aparecem como `Busy`. `POST /api/agents/executions` retorna `400 { "error": "invalid-repository" }` quando `repositoryFullName` não é `owner/name`, `202` quando enfileirado ou `422 { "error": "agent-not-eligible" }` para agente desabilitado/não autenticado/não instalado.

O body das execuções aceita `modelTier` opcional (`"lite" | "normal" | "ultra"`, padrão `"normal"` — ausente em payloads antigos): o servidor mapeia `(agentType, tier)` para um modelo concreto via a tabela curada `AgentCliModels` e injeta a flag de modelo do CLI no argv (`claude --model sonnet`, `codex -m gpt-5.1-codex`, `devin --model swe`, `agy --model gemini-3.1-pro-low`, `opencode -m opencode/claude-sonnet-5`, …). CLIs sem flag de modelo headless (Cline, Continue, Kiro, OpenHands) não recebem flag em nenhum tier. Itens de `GET /api/agents/runs` trazem `modelTier` e o `modelName` resolvido (null em runs antigas e agentes gerenciados pela CLI).

`GET /api/agents/{agentType}/models` retorna o mapeamento efetivo por tier (`lite`/`normal`/`ultra`), a `source` (`override` ou `default`), os `defaults` curados e o `catalog` de modelos conhecidos para pickers; `422 { "error": "model-selection-unsupported" }` para CLIs gerenciadas. `PUT` salva override por CLI (`{ "lite", "normal", "ultra" }` — slots nulos mantêm o curado, nomes ≤128 chars) e `DELETE` o remove; overrides vencem a tabela curada na execução.

`GET /api/agents/runs?issueId=` retorna os runs mais recentes da issue (`{ id, issueId, agentType, state, startedAt, finishedAt }`, mais novo primeiro; `state`: `0` Queued / `1` Running / `2` Succeeded / `3` Failed / `4` Canceled). `GET /api/agents/runs/active` retorna o run mais recente por issue — usado para os badges de agente nos cards do kanban.

## SSE

### Eventos globais

```http
GET /api/events
Accept: text/event-stream
```

Eventos: `task.created`, `task.updated`, `task.archived`, `comment.added`, `comment.deleted`, `attachment.added`, `attachment.deleted`.

### Eventos por thread de IA

```http
GET /api/local/ai/threads/:id/events
Accept: text/event-stream
```

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
