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

- Detects Devin, Claude, Codex, OpenCode, and OpenHands CLIs on the server `PATH`
- Selects an agent when an issue moves to `In Progress` or `Backlog`
- Queues background execution and streams stdout/stderr/system logs
- SignalR hub: `/agent-log-hub`
- Successful execution moves the issue from `In Progress` to `Review`
- Issue modal: `Agent Config` tab (repo link, per-CLI argv preview, prompt editor) and `Logs do Agente` tab with persistent Clear (`DELETE /api/agents/logs/{issueId}`)
- Global default prompt template on `/agents` (`GET`/`PUT /api/agents/prompt-template`) with `{repoUrl}`/`{issueTitle}`/`{issueBody}` placeholders

## Agent CLIs & Terminal

- `/agents` page: install/auth status board for Claude Code, Codex, OpenCode, Devin CLI and Antigravity `agy` (`GET /api/agent-clis`)
- `/terminal` page: interactive bash PTY over SignalR (`/terminal-hub`) with xterm.js — one session per user, 30 min idle timeout, `Taskboard:Terminal:Enabled` flag
- Docker image ships Node.js LTS + the five CLIs with `HOME=/data/home` so credentials persist in the `/data` volume

## Settings: Skills & RAG MCP

- Agent Skills: global `npx skills add` + `install.sh --all` install, verify and per-agent sync with a live process log (`GET /api/skills/log`)
- RAG / Knowledge MCP: provisions the configured server into every enabled CLI config — JSON/TOML merge for Devin, Claude, Codex, OpenCode, OpenHands and `agy mcp add/remove` for Antigravity — with a live process log (`GET /api/mcp/log`); API keys never surface in status or logs

See `.specs/SPEC-*.md` for full requirements.
