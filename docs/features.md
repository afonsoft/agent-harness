# Features

## Core Board

- GitHub-backed Kanban: issues are the source of truth (columns via labels, priorities via `priority:*` labels)
- Board mutations persist `IssueHistoryEvent` records for the unified timeline
- GitHub issue comments as the agent handoff channel
- Markdown GFM + mermaid support (read-only)

## Global repository selector

- A single repository combobox lives in the sidebar above the Board item — `SelectedRepositoryService` (scoped WASM) lists repos via GitHub, holds the current `owner/repo`, persists to `localStorage["harness.selectedRepo"]` and raises `Changed` for every repo-aware page; free-text `owner/repo` still works when the list fetch fails or no token is configured
- Menu grouping: repo-scoped items on top — Board, Gantt, Workflow, Specs, VS Code, Terminal — then a `---` divider, then AI Chat, CLI Agents, FinOps, Settings, Skills, Prompts, and an external Issues link (opens `github.com/afonsoft/agent-harness/issues` in a new tab)
- Collapsed icon rail: the selector becomes a folder icon with the current repo as tooltip; clicking it expands the sidebar
- Consumers: Board/Gantt/Workflow drop their per-page combo and reload on change; `/specs` reads `~/repos/<name>/.specs` from the local clone (empty state when not cloned); new terminal tabs open in `~/repos/<name>`; `/editor` defaults to the clone workdir unless `?path`/`?repo` is given

## Real-time

- Global SSE stream: `/api/events`
- Per-thread AI chat SSE: `/api/local/ai/threads/:id/events`
- Polling fallback for cloud mode (`GET /api/meta`)

## Automation

- `/workflow` — read-only GitHub Actions monitor (workflows, runs, status, duration, direct links)
- Agent execution against GitHub issue cards

## CLI

`taskctl` — Spectre.Console.Cli console with commands:
- `context:current`, `ghissue:history`, `ghissue:comments`, `ghissue:comment`, `cloud:login`, `cloud:status`, `cloud:logout`
- JSON output via `--json`

## MCP Server

4 tools: `get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`, `cloud_status`.

## AI Chat

- Full chat UI at `/ai-chat`: thread sidebar (create via eligible agent CLI + model picker + sandbox picker, delete with confirm), message area with markdown rendering, composer, typing indicator and auto-scroll
- Every thread is bound to an eligible agent CLI — there is no direct LLM provider in the server (SPEC-20260921-ai-chat-cli-backend). The model catalog lists per-agent models: CLI-reported (`opencode models`, `devin models list`…) merged with the curated table and saved tier overrides; "CLI default" lets the agent pick its own model
- `assistant` threads run one-shot through the bound CLI (`IAgentAcpClient`) with the transcript as prompt — the run fails with a clear error if no agent is eligible; legacy threads without an agent auto-bind to the first eligible CLI on the next run; disabling an agent stops its threads at run time
- `agent` threads keep the interactive ACP session (`AgentSessionManager`); the picked model reaches the session via the CLI's model flag
- Agent-mode UX (SPEC-20260921-ai-code-chat-ux): structured tool-call renderers (edit/write → inline diff with path and +/- counts, execute/terminal → collapsible output with exit-code badge, read → truncated preview, unknown → generic card, consecutive file edits → aggregated `changes` card); FIFO prompt queue — `POST .../queue` persists a `queued` event (cancellable via `DELETE .../queue/{eventId}`) and the backend dispatches after each turn ends; context meter fed by ACP `usage_update` (`metric` events — warning ≥80%, danger ≥95%, hidden without data); fork at any event (`POST .../fork` → `<title> (source: fork)` copies events up to that point) and retry of the last user prompt (`POST .../retry`, cancels the active turn first); model/mode quick-switch via `session/set_config_option`/`session/set_mode` when the peer advertises them; payloads above 8k chars collapse behind "show more"
- Assistant responses stream over SSE (`ai_chat.event` deltas + `ai_chat.run` status); JSON snapshot via `Accept: application/json`
- **Run agent** sub-task: picker (repository + eligible agent CLI + model tier) enqueues `POST /api/agents/executions` with the thread context as instructions (last 20 messages, ~8k cap); queue and final status post back as thread events
- `MockLLMProvider` is dev/test only (`Taskboard:AiChat:MockProvider=true`) — never the production default

## Cloud

- Local companion loopback
- Cloudflare D1/R2 proxy
- Basic Auth

## Integrations

- Jira sync
- GitHub Kanban at `/github-board` through `IGitHubService`
- GitHub board labels: `backlog`, `in-progress`, `review`, `done`
- Gantt at `/gantt` backed by GitHub issues: bars `createdAt → closedAt` (open issues run to today with a distinct "in progress" style), milestone `due_on` diamonds, column-colored bars, and a kanban metrics panel — lead time (avg/median), cycle time (first backlog exit → done), WIP, median open-issue aging and weekly throughput — all computed server-side from `labeled`/`unlabeled` timeline events merged with local `IssueHistoryEvent` records
- `/workflow` page: read-only GitHub Actions monitor per repository — workflow cards with last-run conclusion badges (live runs pulse and drive a 60s auto-refresh), expand shows the 10 latest runs (status, branch, SHA, actor, duration, ↗ link to GitHub)
- GitHub authentication through `GITHUB_TOKEN`
- DeepSeek harness
- Execution helpers (`CodexExecutableResolver`, `ProcessTreeSignaler`, `ExecutableCommand`)

## Agent Orchestration

- Detects Devin, Claude, Codex, OpenCode, OpenHands, Antigravity, Kimi, Grok, Aider, Cline, Continue, Copilot, Qwen and Kiro CLIs on the server `PATH`
- Selects an agent when an issue moves to `In Progress` or `Backlog`
- Queues background execution and streams stdout/stderr/system logs
- SignalR hub: `/agent-log-hub`
- Successful execution moves the issue from `In Progress` to `Review`
- Issue modal (~80vw): `Agent Config` tab (repo link, agent CLI + **model tier** Lite/Normal/Ultra combo mapped to real models per CLI — disabled "managed by CLI" for CLIs without a model flag — per-CLI argv preview reflecting the tier, prompt editor), `Logs do Agente` tab with persistent Clear (`DELETE /api/agents/logs/{issueId}`), `Histórico` tab — unified timeline of board mutations (column moves, edits, closes persisted as `IssueHistoryEvent`) merged with agent runs (`GET /api/github/issues/{issueId}/history`), and `Comentários` tab — lists/posts GitHub issue comments; the rendered agent prompt automatically appends them as a bounded `Comments:` section (agent-to-agent handoff channel)
- Global default prompt template on `/agents` (`GET`/`PUT /api/agents/prompt-template`) with `{repoUrl}`/`{issueTitle}`/`{issueBody}` placeholders

## Agent CLIs & Terminal

- `/agents` page: install/auth status board for 13 CLIs — Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code and Kiro CLI (`GET /api/agent-clis`); a **Models** button on installed CLIs opens a dialog to override the Lite/Normal/Ultra model per tier with an editable searchable dropdown fed by the models the CLI reports itself (`GET /api/agents/{type}/models/available` — `opencode models`, `devin models list`, `agy models`) merged with the curated catalog (`GET/PUT/DELETE /api/agents/{type}/models` — overrides stored as JSON in `ConfigurationOverrides`, winning over the curated `AgentCliModels` table at execution time)
- Managed install from the UI: `POST /api/agent-clis/{kind}/install` runs the allowlisted install command (npm/pipx/curl) in the background with a live log console popup (`GET /api/agent-clis/{kind}/install/status`) — closes automatically on success; Login action appears once installed
- `/terminal` page: interactive bash PTYs over SignalR (`/terminal-hub`) with xterm.js — **multiple tabbed sessions** (up to 8 per user, `Open()` returns a `sessionId` routed over one connection), per-tab 30 min idle timeout, per-tab close/reopen, `?cmd=` pre-fills the first tab; new tabs open in the selected repository's workdir (`~/repos/<name>`, falling back to the workspace root); native terminal keys: Ctrl+C copies the selection (or sends SIGINT), Ctrl+V / Ctrl+Shift+V / Shift+Insert paste, `Taskboard:Terminal:Enabled` flag
- Docker image ships Node.js LTS + the five CLIs with `HOME=/data/home` so credentials persist in the `/data` volume

## VS Code Web & Workspace

- `/editor` page (left menu "VS Code"): VS Code Web via managed `code-server` — embedded iframe, path toolbar, open-in-new-tab; defaults to the globally selected repo's workdir (`~/repos/<name>`; `?path=`/`?repo=owner/name` always win); **Restart** button kills and respawns code-server (`POST /api/vscode/restart`)
- Managed install: `POST /api/vscode/install` runs the allowlisted standalone installer in the background with a live log console (`GET /api/vscode/install/status`)
- code-server runs as a lazy child process bound to `127.0.0.1` with `--auth none --disable-workspace-trust --app-name Taskboard`, reachable only through the authenticated YARP proxy at `/vscode/{**}` (WebSocket-enabled, prefix stripped); `EnsureStartedAsync` waits for the port to accept connections (TCP probe, 30s timeout) so the first proxied request never races the listener into a 502; `VSCODE_PROXY_URI=/vscode/proxy/{{port}}` keeps port-forward links working under the subpath
- Workspace root `Taskboard:WorkspaceRoot` (default `~/repos`, auto-created): default cwd for agent runs and clones; card workdir resolves to `<root>/<repo>` (sanitized, traversal-proof) via `GET /api/vscode/workdir`
- "Open in VS Code" on the issue detail dialog opens the editor in a new browser tab via `GET /api/vscode/open?repo=<fullName>` → 302 to `/vscode/?folder=<card workdir>` — watch/edit the same files the agent is working on

## Settings: Skills & RAG MCP

- Agent Skills: global `npx skills add` + `install.sh --all` install, verify and per-agent sync with a live process log (`GET /api/skills/log`); the repository cache self-heals — an inaccessible `skills-cache` (e.g. left by another user) is moved aside to `skills-cache.inaccessible-<timestamp>` and re-cloned automatically, reported as a `cache-prepare` step
- RAG / Knowledge MCP: provisions the configured server into every enabled CLI config — JSON/TOML merge for Devin, Claude, Codex, OpenCode, OpenHands, Kimi, Grok, Qwen, Copilot and Kiro; `agy mcp add/remove` for Antigravity; `cline mcp add/remove` for Cline; JSON file drop into `~/.continue/mcpServers/` for Continue (Aider has no MCP support) — with a live process log (`GET /api/mcp/log`); API keys never surface in status or logs. **Sync** provisions the saved URL (blocked while none is saved — it never silently removes); **Remove** is the explicit un-provision action; per-agent badges distinguish `configured`/`updated`/`removed`/`failed`

## ADE Harness (E6–E11)

- **Workspace isolation**: every agent run gets a dedicated Git worktree under `{worktreeRoot}/{runId}` — `Taskboard:WorktreeRoot`, default `~/repos` so run worktrees sit next to the clones in the workspace — the agent's cwd is the worktree, never the live checkout. Lifecycle endpoints `POST /api/harness/worktrees`, `GET …/{runId}`, `GET …/{runId}/diff` (structured per-file diff), `DELETE …/{runId}` (teardown + `git worktree prune`).
- **Context & memory**: `POST /api/harness/context/compile` merges instruction files (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, copilot-instructions — recursive), an `<env>`+git block and `<project_memory>` items scoped by remote `origin`; `IContextCompactor` summarizes the middle at 80% token budget keeping system prompt + last 5 turns. Memory CRUD under `POST|GET|DELETE /api/harness/memory`.
- **Security gateway**: `POST /api/harness/security/evaluate` classifies pre-dispatch commands (Safe/WorkspaceWrite/Dangerous, fail-closed for unknown binaries/substitutions/globs) with path-jail + symlink-escape enforcement (escape is a hard deny) and regex secret scrubbing (`ghp_`, `sk-`, AWS, Bearer, PEM).
- **Verification loop**: `POST /api/harness/verification/run` runs `dotnet format → build → test` with TRX + coverage parsing and persists a `VerificationReport`; opt-in per run via `verifySolutionFile`/`verifyMinCoverage`/`verifyMaxAttempts` on `AgentExecutionRequest` — failures re-invoke the agent with a `feedbackPrompt`, exhausted retries mark `Failed` + `EscalatedToHuman`.
- **CLI metrics**: incremental read-only ingestion of the agent CLIs' SQLite stores (E10 extractor layer, never writes to external DBs, never reads message content). `POST /api/local/cli-metrics/sync` (single-flight, 404 when `Taskboard:CliMetrics:Enabled=false`), `GET sources|summary?period|sessions`; periodic sync every `SyncIntervalMinutes` (default 15). `/agents` shows sessions-7d, tokens, last activity and a per-source health badge; raw rows purge after `RetentionDays` (default 90), daily aggregates persist. `ICliUsageMetricsProvider` exposes per-kind/model token aggregates for the FinOps feed. Extractors whose vendor schema lacks usage columns estimate tokens from scalars only (Codex `tokens_used`, Devin `SUM(length(chat_message))` over `message_nodes`, Claude context-mode `event_count`, Antigravity title/preview/raw_summary lengths, Cline `length(envelope_json)`) via `CliTokenEstimator` (`EstimatedCharsPerToken` 4, `EstimatedTokensPerEvent` 1000); an `ExtractorDataVersion` bump on `CliMetricSource` clears the watermark and forces one idempotent full re-extract (SPEC-20260922-finops-cli-usage-breakdown).
- **FinOps CLI breakdown**: `/finops` "External CLI usage" keeps the totals strip on top and renders a per-CLI table (`CLI | Sessions | Tokens in | Tokens out | Tokens cached | Cost`) ordered by Sessions desc, with `~` badges for estimated shares and a `no usage data` badge for vendors that report no token usage.

- **Cockpit (HITL)**: `/cockpit` lists pipeline runs and starts new ones (template, repo — prefilled from the global selector — branch, budget cap). With the `single-agent` template the dialog adds an Agent CLI combo (available agents only), a model-tier select and a "run verification stage" checkbox. `/cockpit/runs/{id}` streams a structured live timeline (stage / agent_output / verification / approval / steer / tool_call cards) over the `/harness-cockpit-hub` SignalR group with REST replay on reconnect, a read-only xterm.js **Logs** tab that renders the run's `agent_output` stream in real time with follow-scroll ("↓ novos logs" pill), stage/stream filters, copy/download/clear controls and a live badge, an **Arquivos** explorer tab that browses the run's worktree (`GET …/worktrees/{runId}/files` + `…/files/content` — lazy tree, read-only, `.git` hidden, path-jailed, 512 KB cap), a **Diff** tab that lists changed files as collapsible per-file cards (`GET …/worktrees/{runId}/diff`, `+/-` per file via `--numstat`, truncated above 2 MB), a steer bar (`POST …/runs/{id}/steer` — queued into the next stage prompt), pause/resume controls (`POST …/runs/{id}/pause`/`resume` — freezes the DAG between stages, in-flight stage completes), approval gates (`POST …/approvals/{requestId}`, `stage:<key>` convention), a stop button and a one-click `create-pr` that commits + pushes the worktree, opens the GitHub PR and moves the bound issue to `in_review` with the PR link commented. The header shows live FinOps metrics (tokens in/out, USD cost, elapsed time).
- **Board → Cockpit unified runs**: moving an issue to Backlog/In Progress and picking an agent now starts a real `single-agent` pipeline execution (worktree + events + FinOps), not a legacy `AgentRun`. A banner links straight to `/cockpit/runs/{id}` and the issue's History tab records a `pipeline-run` entry. Legacy `AgentRun` records remain read-only in the timeline.

See `.specs/SPEC-*.md` for full requirements.
