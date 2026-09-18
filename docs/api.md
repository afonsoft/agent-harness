# API Documentation

## REST Endpoints

### Projects

```http
GET    /api/projects
POST   /api/projects
GET    /api/projects/:id
PUT    /api/projects/:id
POST   /api/projects/:id/archive
DELETE /api/projects/:id
```

### Tasks

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

### Comments & Attachments

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

### Context & Storage

```http
GET    /api/context
PUT    /api/context
GET    /api/client-storage
PUT    /api/client-storage
```

### AI

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

### Search

```http
POST   /api/search/semantic
GET    /api/search/suggestions
```

### Settings & Configuration

```http
GET    /api/settings
PUT    /api/settings
GET    /api/configuration
PUT    /api/configuration/{key}
DELETE /api/configuration/{key}
```

### Agent Skills

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

`POST /api/skills/install` runs `npx skills add <repo> -g --all --copy` plus the repository's `install.sh --all` in the background (returns `202` + in-flight status; concurrent calls coalesce). `verify` re-scans the global skills directories without spawning processes.

### MCP Provisioning (RAG)

```http
GET    /api/mcp/status
POST   /api/mcp/sync
PUT    /api/mcp/rag
```

`PUT /api/mcp/rag` accepts `{ "name", "url", "apiKey" }` — `null` fields keep the stored value, an empty `url` removes the managed entry from every enabled agent's MCP config. Provisioning merges the managed `<name>` entry into `~/.config/devin/mcp_config.json`, `~/.claude.json`, `~/.codex/config.toml`, `~/.config/opencode/opencode.json`, `~/.openhands/mcp.json`, `~/.kimi-code/mcp.json`, `~/.grok/config.toml`, `~/.qwen/settings.json`, `~/.copilot/mcp-config.json` and `~/.kiro/settings/mcp.json`; Antigravity and Cline are provisioned through their CLIs (`agy mcp add/remove`, `cline mcp add/remove`); Continue receives a JSON file at `~/.continue/mcpServers/<name>.json` (atomic writes, `.bak` backups, `0600`). The API key is never returned by any endpoint. Configuration keys: `Taskboard:Rag:ServerName` / `Taskboard:Rag:Url` / `Taskboard:Rag:ApiKey` (env aliases `TASKBOARD_RAG_NAME` / `TASKBOARD_RAG_URL` / `TASKBOARD_RAG_API_KEY`), persisted as SQLite configuration overrides.

### CLI Agents & Terminal

```http
GET    /api/agent-clis
POST   /api/agent-clis/{kind}/install
GET    /api/agent-clis/{kind}/install/status
POST   /terminal-hub/negotiate   (SignalR hub)
```

`GET /api/agent-clis` returns one entry per supported agent CLI (Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code, Kiro CLI): `agent`, `displayName`, `binary`, `installed`, `version`, `authStatus` (`0` unknown / `1` authenticated / `2` not authenticated — credential-file existence probe only, contents are never read), `configDir`, `loginCommand`, `installHint`, `requiredTool`, `prerequisiteMet`. Backed by `Taskboard:HomeDir` (default `$HOME`).

`POST /api/agent-clis/{kind}/install` runs the fixed, server-side allowlisted install command for `{kind}` (case-insensitive CLI name — `cline`, `qwen`, `kiro`, ...) in the background: `202` while running, `200` when a previous run already finished, `404` for unknown kinds. `GET .../install/status` returns `{ kind, state (0 idle / 1 running / 2 succeeded / 3 failed), startedAtUtc, exitCode, lines: [{ atUtc, stream, content }] }` — a bounded in-memory buffer (~500 lines, sanitized) the UI polls to render the install popup.

`/terminal-hub` is a SignalR hub streaming interactive bash PTYs (`script -qfc`, `TERM=xterm-256color`) to the `/terminal` page — **multiple tabbed sessions** per authenticated user (max 8), each identified by a `sessionId` and multiplexed over a single connection; sessions close after 30 minutes idle or when their connection ends. Hub methods: `Open() → string sessionId`, `Input(string sessionId, string data)`, `Resize(string sessionId, int cols, int rows)`, `Close(string sessionId)`; client callbacks: `output(string sessionId, string chunk)`, `closed(string sessionId, string reason)` (`exited` / `idle-timeout` / `closed`). Gated by `Taskboard:Terminal:Enabled` (default `true`, env `TASKBOARD_TERMINAL_ENABLED`, editable at runtime).

### VS Code Web (code-server)

```http
GET    /api/vscode/status
POST   /api/vscode/install
GET    /api/vscode/install/status
GET    /api/vscode/workdir?repo=owner/name
GET    /api/vscode/open?repo=owner/name  → 302 → /vscode/?folder=<workdir> (direct-to-editor deep link)
GET    /vscode                  → 302 → /vscode/ (trailing slash required by code-server)
GET    /vscode/{**}             → YARP reverse proxy → http://127.0.0.1:8377
```

`GET /api/vscode/status` returns `{ installed, binaryPath?, version?, running, port, homeDirectory, workspaceRoot }`. `POST /api/vscode/install` runs the fixed allowlisted command `bash -c "curl -fsSL https://code-server.dev/install.sh | sh -s -- --method=standalone"` in the background (`202`/`200`, same `install/status` contract as agent-CLI installs — bounded ~500-line sanitized buffer, no client-supplied arguments). `GET /api/vscode/workdir?repo=owner/name` resolves the card workdir under the workspace root → `{ path, exists }` (falls back to the root when the repo dir does not exist yet; non-`owner/name` input → `404`).

The `/vscode/{**}` route is a YARP reverse proxy to the managed `code-server` child process (`--bind-addr 127.0.0.1:<port> --auth none --disable-telemetry`, spawned lazily and killed with the host): it strips the `/vscode` prefix (code-server is path-agnostic — relative asset URLs resolve under the browser's `/vscode/`), upgrades WebSockets, and requires the app's cookie auth — the only door in, since code-server itself runs without auth on loopback. `503` when not installed or the process failed to start. Port: `Taskboard:Vscode:Port` (default `8377`).

### Workspace root

`Taskboard:WorkspaceRoot` (default `~/repos`, created on demand) is the default working directory for agent runs without an explicit `RepoPath` — clones land in `<root>/<repo-name>` where the name is the last `owner/name` segment sanitized to `[A-Za-z0-9._-]` (traversal can't escape the root). Resolved card workdirs are used by "Open in VS Code".

### GitHub Kanban

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

Kanban state is label-backed: `PATCH .../issues/{n}` edits `{ title?, body }` (markdown body rendered sanitized in the UI); `PUT .../priority` `{ "priority": "none|urgent|high|medium|low" }` swaps the `priority:*` labels (`none` removes them); `POST .../close` `{ "resolution": "canceled|archived" }` closes the issue — `canceled` also applies the `canceled` label (Canceled column), `archived` closes without a column label (Archived). All return `200 { issue }`; invalid enum values → `400`, unknown issue → `404`, anonymous → `401`.

Every board-side mutation is also persisted as an `IssueHistoryEvent` (`column-moved` with `from`/`to`, `edited` with the changed fields, `closed` with the resolution) keyed by the GitHub issue id — best-effort, never fails the mutation. `GET .../issues/{issueId}/history` merges those events with the issue's agent runs into `200 { items: [{ kind, occurredAt, agentType?, agentRunState?, finishedAt?, from?, to?, detail? }] }` newest-first — the data behind the `Histórico` tab in the issue dialog.

Issue comments live in GitHub (never persisted locally): `GET .../issues/{n}/comments` returns `200 { comments: [{ id, authorLogin, body, createdAt, updatedAt, htmlUrl }] }` chronological, `POST` `{ "body" }` creates one (`400 { "error": "empty-body" }` on blank, `404 { "error": "issue-not-found" }`). The `Comentários` tab lists/posts them, and the agent prompt renderer appends them automatically as a bounded `Comments:` section (~3k chars, omitted when empty, fetch failures never block the run).

### Agent Orchestration

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

`GET /api/agents` lists only *eligible* agents — installed on PATH, authenticated CLI (credential probe) and enabled in Settings → Agents; running agents are reported as `Busy`. `POST /api/agents/executions` returns `400 { "error": "invalid-repository" }` when `repositoryFullName` is not `owner/name`, `202` when queued, or `422 { "error": "agent-not-eligible" }` for a disabled/unauthenticated/uninstalled agent.

The executions body accepts an optional `modelTier` (`"lite" | "normal" | "ultra"`, default `"normal"` — absent in old payloads): the server maps `(agentType, tier)` to a concrete model through the curated `AgentCliModels` table and injects the CLI's model flag into the argv (`claude --model sonnet`, `codex -m gpt-5.1-codex`, `devin --model swe`, `agy --dangerously-skip-permissions --model gemini-3.1-pro-low`, `opencode run --auto -m opencode/claude-sonnet-5`, …). CLIs without a headless model flag (Cline, Continue, Kiro, OpenHands) get no flag regardless of tier. `GET /api/agents/runs` items carry `modelTier` and the resolved `modelName` (null for old runs and CLI-managed agents).

`GET /api/agents/{agentType}/models` returns the effective per-tier mapping (`lite`/`normal`/`ultra`), its `source` (`override` or `default`), the curated `defaults` and the known-model `catalog` for pickers; `422 { "error": "model-selection-unsupported" }` for CLI-managed agents. `PUT` saves a per-CLI override (`{ "lite", "normal", "ultra" }` — null slots keep the curated default, names ≤128 chars) and `DELETE` removes it; overrides win over the curated table at execution time.

`GET /api/agents/runs?issueId=` returns the issue's latest runs (`{ id, issueId, agentType, state, startedAt, finishedAt }`, newest first; `state`: `0` Queued / `1` Running / `2` Succeeded / `3` Failed / `4` Canceled). `GET /api/agents/runs/active` returns the latest run per issue — used to render agent badges on the kanban cards.

## SSE

### Global events

```http
GET /api/events
Accept: text/event-stream
```

Events: `task.created`, `task.updated`, `task.archived`, `comment.added`, `comment.deleted`, `attachment.added`, `attachment.deleted`.

### Per-thread AI events

```http
GET /api/local/ai/threads/:id/events
Accept: text/event-stream
```

## Error Contract

All errors use RFC 7807 `ProblemDetails` with optional `ErrorCode`:

```json
{
  "type": "https://taskboard/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "Status must be one of: todo, in_progress, in_review, done, blocked, canceled",
  "ErrorCode": "VALIDATION_ERROR"
}
```

Special code `VERSION_CONFLICT` with HTTP 409 for optimistic concurrency failures.

## JSON Envelope

Responses are plain JSON. The CLI `taskctl` wraps API responses in:

```json
{
  "ok": true,
  "data": { ... },
  "error": null
}
```

See `.specs/SPEC-002-rest-api.md` and `.specs/SPEC-003-cli.md` for full contracts.
