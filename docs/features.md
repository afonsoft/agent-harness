# Features

## Core Board

- Projects and tasks with statuses, priorities, labels
- Kanban board sorted by `sort_order`
- Comments, attachments, task relations, activities
- Markdown GFM + mermaid support (read-only)
- Optimistic concurrency with `version` column

## Real-time

- Global SSE stream: `/api/events`
- Per-thread AI chat SSE: `/api/local/ai/threads/:id/events`
- Polling fallback for cloud mode (`GET /api/meta`)

## Automation

- Workflow workspaces (JSON board config)
- Control-flow engine
- Auto-claim `todo` → `in_progress` for Codex agents

## CLI

`taskctl` — System.CommandLine console with subcommands:
- `project`, `issue`, `comment`, `attachment`, `label`, `ai`, `context`, `search`
- JSON output via `--json`

## MCP Server

13 tools exposing project/issue/comment/attachment/label/search operations.

## AI Chat

- Threads, runs, events
- Model catalog
- Composer candidates / rebind
- Provider abstraction (OpenAI, Claude, Azure OpenAI)

## Cloud

- Local companion loopback
- Cloudflare D1/R2 proxy
- Basic Auth

## Integrations

- Jira sync
- GitHub Kanban at `/github-board` through `IGitHubService`
- GitHub board labels: `backlog`, `in-progress`, `review`, `done`
- GitHub authentication through `GITHUB_TOKEN`
- DeepSeek harness
- Execution helpers (`CodexExecutableResolver`, `ProcessTreeSignaler`, `ExecutableCommand`)

## Agent Orchestration

- Detects Devin, Claude, Codex, OpenCode, OpenHands, Antigravity, Kimi, Grok, Aider, Cline, Continue, Copilot, Qwen and Kiro CLIs on the server `PATH`
- Selects an agent when an issue moves to `In Progress` or `Backlog`
- Queues background execution and streams stdout/stderr/system logs
- SignalR hub: `/agent-log-hub`
- Successful execution moves the issue from `In Progress` to `Review`
- Issue modal: `Agent Config` tab (repo link, per-CLI argv preview, prompt editor), `Logs do Agente` tab with persistent Clear (`DELETE /api/agents/logs/{issueId}`), and `Histórico` tab — unified timeline of board mutations (column moves, edits, closes persisted as `IssueHistoryEvent`) merged with agent runs (`GET /api/github/issues/{issueId}/history`)
- Global default prompt template on `/agents` (`GET`/`PUT /api/agents/prompt-template`) with `{repoUrl}`/`{issueTitle}`/`{issueBody}` placeholders

## Agent CLIs & Terminal

- `/agents` page: install/auth status board for 13 CLIs — Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code and Kiro CLI (`GET /api/agent-clis`)
- Managed install from the UI: `POST /api/agent-clis/{kind}/install` runs the allowlisted install command (npm/pipx/curl) in the background with a live log console popup (`GET /api/agent-clis/{kind}/install/status`) — closes automatically on success; Login action appears once installed
- `/terminal` page: interactive bash PTYs over SignalR (`/terminal-hub`) with xterm.js — **multiple tabbed sessions** (up to 8 per user, `Open()` returns a `sessionId` routed over one connection), per-tab 30 min idle timeout, per-tab close/reopen, `?cmd=` pre-fills the first tab; native terminal keys: Ctrl+C copies the selection (or sends SIGINT), Ctrl+V / Ctrl+Shift+V / Shift+Insert paste, `Taskboard:Terminal:Enabled` flag
- Docker image ships Node.js LTS + the five CLIs with `HOME=/data/home` so credentials persist in the `/data` volume

## VS Code Web & Workspace

- `/editor` page (left menu "VS Code"): VS Code Web via managed `code-server` — embedded iframe, path toolbar, open-in-new-tab; defaults to `$HOME`, `?repo=owner/name` opens the card's repo workdir
- Managed install: `POST /api/vscode/install` runs the allowlisted standalone installer in the background with a live log console (`GET /api/vscode/install/status`)
- code-server runs as a lazy child process bound to `127.0.0.1` with `--auth none --disable-workspace-trust --app-name Taskboard`, reachable only through the authenticated YARP proxy at `/vscode/{**}` (WebSocket-enabled, prefix stripped); `VSCODE_PROXY_URI=/vscode/proxy/{{port}}` keeps port-forward links working under the subpath
- Workspace root `Taskboard:WorkspaceRoot` (default `~/repos`, auto-created): default cwd for agent runs and clones; card workdir resolves to `<root>/<repo>` (sanitized, traversal-proof) via `GET /api/vscode/workdir`
- "Open in VS Code" on the issue detail dialog opens the editor in a new browser tab via `GET /api/vscode/open?repo=<fullName>` → 302 to `/vscode/?folder=<card workdir>` — watch/edit the same files the agent is working on

## Settings: Skills & RAG MCP

- Agent Skills: global `npx skills add` + `install.sh --all` install, verify and per-agent sync with a live process log (`GET /api/skills/log`)
- RAG / Knowledge MCP: provisions the configured server into every enabled CLI config — JSON/TOML merge for Devin, Claude, Codex, OpenCode, OpenHands, Kimi, Grok, Qwen, Copilot and Kiro; `agy mcp add/remove` for Antigravity; `cline mcp add/remove` for Cline; JSON file drop into `~/.continue/mcpServers/` for Continue (Aider has no MCP support) — with a live process log (`GET /api/mcp/log`); API keys never surface in status or logs

See `.specs/SPEC-*.md` for full requirements.
