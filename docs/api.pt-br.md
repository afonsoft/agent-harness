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

`PUT /api/mcp/rag` aceita `{ "name", "url", "apiKey" }` — campos `null` preservam o valor gravado; `url` vazia remove a entrada gerenciada de todos os agentes habilitados. O provisionamento faz merge da entrada `<name>` em `~/.config/devin/mcp_config.json`, `~/.claude.json`, `~/.codex/config.toml`, `~/.config/opencode/opencode.json` e `~/.openhands/mcp.json` (escrita atômica, backup `.bak`, `0600`). A API key nunca é retornada por nenhum endpoint. Chaves de configuração: `Taskboard:Rag:ServerName` / `Taskboard:Rag:Url` / `Taskboard:Rag:ApiKey` (aliases de env `TASKBOARD_RAG_NAME` / `TASKBOARD_RAG_URL` / `TASKBOARD_RAG_API_KEY`), persistidas como overrides em SQLite.

### CLI Agents e Terminal

```http
GET    /api/agent-clis
POST   /terminal-hub/negotiate   (hub SignalR)
```

`GET /api/agent-clis` retorna uma entrada por CLI de agente suportado (Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`): `agent`, `displayName`, `binary`, `installed`, `version`, `authStatus` (`0` desconhecido / `1` autenticado / `2` não autenticado — probe apenas de existência do arquivo de credencial, o conteúdo nunca é lido), `configDir`, `loginCommand`, `installHint`. Baseado em `Taskboard:HomeDir` (padrão `$HOME`).

`/terminal-hub` é um hub SignalR que transmite um bash PTY interativo (`script -qfc`, `TERM=xterm-256color`) para a página `/terminal` — uma sessão por usuário autenticado, reconexões substituem a sessão anterior, sessões ociosas fecham após 30 minutos. Métodos do hub: `Input(string)`, `Resize(int cols, int rows)`; callbacks do cliente: `output(string)`, `closed(string reason)`. Controlado por `Taskboard:Terminal:Enabled` (padrão `true`, env `TASKBOARD_TERMINAL_ENABLED`, editável em runtime).

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
```

O estado do kanban é baseado em labels: `PATCH .../issues/{n}` edita `{ title?, body }` (corpo markdown renderizado sanitizado na UI); `PUT .../priority` `{ "priority": "none|urgent|high|medium|low" }` troca as labels `priority:*` (`none` remove); `POST .../close` `{ "resolution": "canceled|archived" }` fecha a issue — `canceled` também aplica a label `canceled` (coluna Canceled), `archived` fecha sem label de coluna (Archived). Todos retornam `200 { issue }`; enum inválido → `400`, issue desconhecida → `404`, anônimo → `401`.

### Orquestração de Agentes

```http
GET    /api/agents
POST   /api/agents/executions
GET    /api/agents/runs?issueId={id}&take={n}
GET    /api/agents/runs/active
GET    /api/agents/logs/{issueId}
POST   /api/agents/executions/{issueId}/cancel
```

`GET /api/agents` lista apenas agentes *elegíveis* — instalados no PATH, com CLI autenticado (probe de credencial) e habilitados em Settings → Agents; agentes em execução aparecem como `Busy`. `POST /api/agents/executions` retorna `202` quando enfileirado ou `422 { "error": "agent-not-eligible" }` para agente desabilitado/não autenticado/não instalado.

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
