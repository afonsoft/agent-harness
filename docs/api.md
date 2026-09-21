# API Documentation

## REST Endpoints

### Client Storage

```http
GET    /api/client-storage
PUT    /api/client-storage
```

### AI

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
POST   /api/mcp/remove
GET    /api/mcp/log
PUT    /api/mcp/rag
```

`PUT /api/mcp/rag` accepts `{ "name", "url", "apiKey" }` — `null` fields keep the stored value; an empty `url` is rejected (`400 rag-url-required`) since un-provisioning is the explicit `POST /api/mcp/remove` action. `POST /api/mcp/sync` reloads the DB overrides and provisions the saved config — returns `400 rag-not-configured` when no URL is stored (Sync never silently removes). `POST /api/mcp/remove` un-provisions the managed entry from every target regardless of the stored URL. Per-agent results report `configured` / `updated` / `removed` / `not-configured` / `skipped` / `repaired` / `failed` (`updated` = an existing entry with a different value was overwritten). Provisioning merges the managed `<name>` entry into `~/.config/devin/mcp_config.json`, `~/.claude.json`, `~/.codex/config.toml`, `~/.config/opencode/opencode.json`, `~/.openhands/mcp.json`, `~/.kimi-code/mcp.json`, `~/.grok/config.toml`, `~/.qwen/settings.json`, `~/.copilot/mcp-config.json` and `~/.kiro/settings/mcp.json`; Antigravity and Cline are provisioned through their CLIs (`agy mcp add/remove`, `cline mcp add/remove`); Continue receives a JSON file at `~/.continue/mcpServers/<name>.json` (atomic writes, `.bak` backups, `0600`). The API key is never returned by any endpoint. Configuration keys: `Taskboard:Rag:ServerName` / `Taskboard:Rag:Url` / `Taskboard:Rag:ApiKey` (env aliases `TASKBOARD_RAG_NAME` / `TASKBOARD_RAG_URL` / `TASKBOARD_RAG_API_KEY`), persisted as SQLite configuration overrides.

### MCP Server (Streamable HTTP)

```text
POST   /api/mcp
```

The same tools served by the `Taskboard.Mcp` stdio executable are also exposed over Streamable HTTP — **stateless by default** (MCP C# SDK v2, spec rev. 2026-07-28: no `initialize` handshake, no `Mcp-Session-Id`; legacy clients still work via automatic fallback). The endpoint inherits the `/api` group's authorization — cookie session or `X-Api-Key`; anonymous requests return `401`.

Clients must send `Accept: application/json, text/event-stream`. Request bodies are JSON-RPC 2.0 (`tools/list`, `tools/call`, ...).

Tools (identical to the stdio server): `get_issue_history`, `list_github_issue_comments`, `add_github_issue_comment`, `cloud_status`.

Tools call the local API through a loopback `ITaskboardApiClient` — the server authenticates itself with `Taskboard:ApiKey` (or `TASKBOARD_API_KEY`) when configured; without an API key the endpoint still answers `tools/list`, but tool calls that hit `/api` will fail with `401`.

Example:

```bash
curl -X POST http://127.0.0.1:47823/api/mcp \
  -H "X-Api-Key: <key>" \
  -H "Accept: application/json, text/event-stream" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### CLI Agents & Terminal

```http
GET    /api/agent-clis
POST   /api/agent-clis/{kind}/install
GET    /api/agent-clis/{kind}/install/status
POST   /terminal-hub/negotiate   (SignalR hub)
```

`GET /api/agent-clis` returns one entry per supported agent CLI (Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`, Kimi Code, Grok, Aider, Cline, Continue, GitHub Copilot CLI, Qwen Code, Kiro CLI): `agent`, `displayName`, `binary`, `installed`, `version`, `authStatus` (`0` unknown / `1` authenticated / `2` not authenticated — credential-file existence probe only, contents are never read), `configDir`, `loginCommand`, `installHint`, `requiredTool`, `prerequisiteMet`. Backed by `Taskboard:HomeDir` (default `$HOME`).

`POST /api/agent-clis/{kind}/install` runs the fixed, server-side allowlisted install command for `{kind}` (case-insensitive CLI name — `cline`, `qwen`, `kiro`, ...) in the background: `202` while running, `200` when a previous run already finished, `404` for unknown kinds. `GET .../install/status` returns `{ kind, state (0 idle / 1 running / 2 succeeded / 3 failed), startedAtUtc, exitCode, lines: [{ atUtc, stream, content }] }` — a bounded in-memory buffer (~500 lines, sanitized) the UI polls to render the install popup.

`/terminal-hub` is a SignalR hub streaming interactive bash PTYs (`script -qfc`, `TERM=xterm-256color`) to the `/terminal` page — **multiple tabbed sessions** per authenticated user (max 8), each identified by a `sessionId` and multiplexed over a single connection; sessions close after 30 minutes idle or when their connection ends. Hub methods: `Open(string? repo) → string sessionId`, `Input(string sessionId, string data)`, `Resize(string sessionId, int cols, int rows)`, `Close(string sessionId)`; `repo` (`owner/name`) sets the new session's cwd to the clone workdir — resolved server-side and confined to the workspace root — while live sessions and reattach keep their original cwd; client callbacks: `output(string sessionId, string chunk)`, `closed(string sessionId, string reason)` (`exited` / `idle-timeout` / `closed`). Gated by `Taskboard:Terminal:Enabled` (default `true`, env `TASKBOARD_TERMINAL_ENABLED`, editable at runtime).

### VS Code Web (code-server)

```http
GET    /api/vscode/status
POST   /api/vscode/install
GET    /api/vscode/install/status
POST   /api/vscode/restart
GET    /api/vscode/workdir?repo=owner/name
GET    /api/vscode/open?repo=owner/name  → 302 → /vscode/?folder=<workdir> (direct-to-editor deep link)
GET    /vscode                  → 302 → /vscode/ (trailing slash required by code-server)
GET    /vscode/{**}             → YARP reverse proxy → http://127.0.0.1:8377
```

`GET /api/vscode/status` returns `{ installed, binaryPath?, version?, running, port, homeDirectory, workspaceRoot }`. `POST /api/vscode/install` runs the fixed allowlisted command `bash -c "curl -fsSL https://code-server.dev/install.sh | sh -s -- --method=standalone"` in the background (`202`/`200`, same `install/status` contract as agent-CLI installs — bounded ~500-line sanitized buffer, no client-supplied arguments). `GET /api/vscode/workdir?repo=owner/name` resolves the card workdir under the workspace root → `{ path, exists }` (falls back to the root when the repo dir does not exist yet; non-`owner/name` input → `404`). `POST /api/vscode/restart` kills and respawns the managed code-server — serialized kill → spawn → wait-for-listening inside the process manager — and returns `200` with the post-restart status; use it when the editor wedges and stops serving.

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
GET   /api/github/repos/{owner}/{repo}/timeline?days=90
GET   /api/github/repos/{owner}/{repo}/metrics?days=90
GET   /api/github/repos/{owner}/{repo}/workflows
GET   /api/github/repos/{owner}/{repo}/workflows/{workflowId}/runs?take=10
POST  /api/github/repos/{owner}/{repo}/pulls
```

Kanban state is label-backed: `PATCH .../issues/{n}` edits `{ title?, body }` (markdown body rendered sanitized in the UI); `PUT .../priority` `{ "priority": "none|urgent|high|medium|low" }` swaps the `priority:*` labels (`none` removes them); `POST .../close` `{ "resolution": "canceled|archived" }` closes the issue — `canceled` also applies the `canceled` label (Canceled column), `archived` closes without a column label (Archived). All return `200 { issue }`; invalid enum values → `400`, unknown issue → `404`, anonymous → `401`.

Every board-side mutation is also persisted as an `IssueHistoryEvent` (`column-moved` with `from`/`to`, `edited` with the changed fields, `closed` with the resolution, `pipeline-run` with the pipeline execution id in `detail`) keyed by the GitHub issue id — best-effort, never fails the mutation. `GET .../issues/{issueId}/history` merges those events with the issue's agent runs into `200 { items: [{ kind, occurredAt, agentType?, agentRunState?, finishedAt?, from?, to?, detail? }] }` newest-first — the data behind the `Histórico` tab in the issue dialog.

Issue comments live in GitHub (never persisted locally): `GET .../issues/{n}/comments` returns `200 { comments: [{ id, authorLogin, body, createdAt, updatedAt, htmlUrl }] }` chronological, `POST` `{ "body" }` creates one (`400 { "error": "empty-body" }` on blank, `404 { "error": "issue-not-found" }`). The `Comentários` tab lists/posts them, and the agent prompt renderer appends them automatically as a bounded `Comments:` section (~3k chars, omitted when empty, fetch failures never block the run).

The Gantt timeline and kanban metrics come from `GET .../repos/{owner}/{repo}/timeline` → `200 { issues: [{ id, number, title, column, priority, assigneeLogin, createdAt, closedAt, milestoneNumber, milestoneDueOn, transitions: [{ at, from, to }] }], milestones: [{ number, title, dueOn, state }] }` and `GET .../repos/{owner}/{repo}/metrics` → `200 { leadTimeAvgDays, leadTimeMedianDays, cycleTimeAvgDays, throughputPerWeek: [{ weekStart, closed }], wip, openMedianAgeDays }`. Both take `days` (default 90) — issues closed before the window are excluded; transitions replay the issue's `labeled`/`unlabeled` GitHub events merged with local `IssueHistoryEvent` records (deduplicated by `(at, from, to)`); per-issue timeline fetches run bounded (≤8 concurrent) and best-effort. Unknown repo → `404 { "error": "repo-not-found" }`.

The `/workflow` page is a read-only GitHub Actions monitor: `GET .../workflows` → `200 { workflows: [{ id, name, path, state, htmlUrl, lastRun }] }` (each workflow embeds its most recent run — fetched per-workflow, capped at 20 fetches and ≤8 concurrent, best-effort so an unlistable run history just leaves `lastRun` null) and `GET .../workflows/{id}/runs?take=10` → `200 { runs: [{ id, name, displayTitle, runNumber, event, status, conclusion, headBranch, headSha, actorLogin, createdAt, updatedAt, runStartedAt, htmlUrl }] }`. Unknown repo → `404 { "error": "repo-not-found" }`. The UI badges map `success`→green, `failure`/`timed_out`/`startup_failure`/`action_required`→red, `in_progress`/`queued`/`requested`/`waiting`/`pending`→pulsing amber (drives a 60s auto-refresh), everything else→gray.

`POST .../pulls` body `{ title, head, baseBranch, body? }` opens a pull request via Octokit → `201 { prUrl }`; used by the cockpit `create-pr` action after pushing the worktree branch.

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
GET    /api/agents/events?scopeKind={run|thread|issue}&scopeId={id}&after={seq}&take={n}
POST   /api/agents/executions/{issueId}/cancel
```

`GET /api/agents` lists only *eligible* agents — installed on PATH, authenticated CLI (credential probe) and enabled in Settings → Agents; running agents are reported as `Busy`. `POST /api/agents/executions` returns `400 { "error": "invalid-repository" }` when `repositoryFullName` is not `owner/name`, `202` when queued, or `422 { "error": "agent-not-eligible" }` for a disabled/unauthenticated/uninstalled agent.

The executions body accepts an optional `modelTier` (`"lite" | "normal" | "ultra"`, default `"normal"` — absent in old payloads): the server maps `(agentType, tier)` to a concrete model through the curated `AgentCliModels` table and injects the CLI's model flag into the argv (`claude --model sonnet`, `codex -m gpt-5.6-luna`, `devin --model swe`, `agy --dangerously-skip-permissions --model gemini-3.1-pro-low`, `opencode run --auto -m opencode/claude-sonnet-5`, …). CLIs without a headless model flag (Cline, Continue, Kiro, OpenHands) get no flag regardless of tier. `GET /api/agents/runs` items carry `modelTier` and the resolved `modelName` (null for old runs and CLI-managed agents).

`GET /api/agents/{agentType}/models` returns the effective per-tier mapping (`lite`/`normal`/`ultra`), its `source` (`override` or `default`), the curated `defaults` and the known-model `catalog` for pickers; `422 { "error": "model-selection-unsupported" }` for CLI-managed agents. `PUT` saves a per-CLI override (`{ "lite", "normal", "ultra" }` — null slots keep the curated default, names ≤128 chars) and `DELETE` removes it; overrides win over the curated table at execution time.

`GET /api/agents/{agentType}/models/available` returns `{ models: [...] }` — the model ids the installed CLI reports itself via its headless list command (`opencode models`, `devin models list`, `agy models`; bounded 10s probe, cached 5min — `?refresh=true` bypasses the cache, used by the dialog's Sync button). Empty array when the CLI has no documented probe (Claude, Codex, …), is not installed, or the probe fails; `422 model-selection-unsupported` for CLI-managed agents. The Models dialog merges these ids with the curated `catalog` in an editable autocomplete.

`GET /api/agents/runs?issueId=` returns the issue's latest runs (`{ id, issueId, agentType, state, startedAt, finishedAt }`, newest first; `state`: `0` Queued / `1` Running / `2` Succeeded / `3` Failed / `4` Canceled). `GET /api/agents/runs/active` returns the latest run per issue — used to render agent badges on the kanban cards.

`GET /api/agents/events` (SPEC-20260921-agent-execution-event-pipeline) replays the normalized agent execution event stream persisted per scope — `scopeKind` is `run` (cockpit pipeline run), `thread` (AI Chat agent session) or `issue` (board one-shot run). Events carry `sequence` (monotonic per scope), `kind` (`lifecycle|message|thought|plan|tool_call|tool_output|permission|output|diff|verification|metric|error|approval|steer|activity`), optional `stageId`/`sessionId`/`toolCallId`/`parentEventId` correlation, `payloadJson` (redacted + truncated) and `rawJson`. `after`/`take` paginate by sequence (`400` on invalid scope). Live events also stream over SignalR `/agent-log-hub` group `agent:{scopeKind}:{scopeId}` (`SubscribeToScope`, `ReceiveAgentEvent`). ACP sessions now perform the real protocol handshake (`initialize` → `session/new` with `sessionId` capture → `session/prompt` content blocks) and answer `session/request_permission` as a JSON-RPC response; agent→client requests for undeclared capabilities get `-32601`.

### Harness — Workspace Isolation (E6)

```http
POST   /api/harness/worktrees
GET    /api/harness/worktrees/{runId}
GET    /api/harness/worktrees/{runId}/diff
DELETE /api/harness/worktrees/{runId}?force={true|false}
```

`POST` body `{ runId, repositoryPath, baseBranch?, taskSlug, retainOnFailure? }` → `201` with `{ worktreeId, runId, path, branch, status, repositoryPath, baseBranch, commitSha?, retainOnFailure, createdAt, updatedAt, version }`. Creates a dedicated `git worktree` under `~/.taskboard/worktrees/{runId}` on a new `feature/agent-{runId}-{slug}` branch (incremental suffix on collision); `baseBranch` defaults to `main` in the API and to `HEAD` when the agent orchestrator isolates a run itself. Idempotent per `runId` — a live session is reused.

`GET .../diff` → `200 { filesChanged, insertions, deletions, files: [{ path, status }], patch }` — `git status --porcelain` + two-dot `git diff <base>` covering committed and pending changes. `DELETE` runs `git worktree remove` (`?force=true` adds `--force`), prunes and deletes the directory on lock, marks the session `Removed` → `204`. Unknown `runId` → `400`; worktree paths are confined to the approved root (traversal rejected).

When a queued agent run carries a `RepoPath` pointing at a git repository, `AgentOrchestrationService` isolates it automatically: the run executes with cwd inside the worktree and `RetainOnFailure` on — success marks the session `Completed` (kept for diff review), failure/cancel marks `RetainedForInspection`. Isolation failure degrades to direct execution and is logged.

### Harness — Context & Memory (E7)

```http
POST   /api/harness/context/compile
POST   /api/harness/memory
GET    /api/harness/memory?repositoryFullName={owner/name}&query={q}&take={n}
DELETE /api/harness/memory/{id}
```

`POST context/compile` body `{ worktreePath, agentType, maxTokenBudget }` → `200 { systemPrompt, estimatedTokens, injectedFiles, memoriesInjectedCount }`. Discovers instruction files (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.github/copilot-instructions.md`) recursively, injects `<env>` metadata, git branch/commit/status and the `<project_memory>` block (scoped by the `origin` remote). Environment secrets are never included in the prompt.

`POST memory` body `{ repositoryFullName, topic, content, tags?, type? }` → `201` with the persisted item (`type`: `Fact` | `ArchitecturalDecision` | `LessonLearned`, default `Fact`). `GET` lists memories scoped to a repository — `query` switches to ranked lexical search (tags > topic > content). `DELETE` removes by id → `204`. Memory records are metadata only — never store secrets.

### Harness — Security Gateway (E8)

```http
POST /api/harness/security/evaluate
```

Body `{ toolName, command, worktreePath, policy? }` (`policy`: `Strict` | `Standard` | `Autonomous`, default `Standard`) → `200 { allowed, riskLevel, requiresApproval, reason }`. `riskLevel`: `Safe` | `WorkspaceWrite` | `Dangerous`. File tools (`write_file`, `read_file`, …) are jail-validated against `worktreePath` — escapes return `400` with code `Taskboard:00025` (`SECURITY_ACCESS_DENIED`). Shell commands go through a lexer classifier (per-segment `&&`/`||`/`;`/`|`, quotes, redirects, env prefixes); jail-escaping targets are hard-denied (`allowed: false`, `requiresApproval: false`) — other `Dangerous` commands require approval under Strict/Standard and are blocked under Autonomous. Unknown binaries classify Dangerous (fail-closed). `IPermissionGateway.ScrubSecrets` masks known credential patterns (`ghp_…`, `github_pat_…`, `sk-…`, AWS keys, `Bearer` tokens, PEM keys) with `[REDACTED_SECRET]` for log/stream pipelines.

### Harness — Verification Loop (E9)

```http
POST /api/harness/verification/run
```

Body `{ worktreePath, solutionFile, minCoverageThreshold, enforceFormat?, maxAttempts?, attempt? }` → `200 { isSuccess, status, compilationErrors, testSummary, coveragePercent, feedbackPrompt }`. `status`: `Passed` | `FormatFailed` | `BuildFailed` | `TestsFailed` | `CoverageRegression` | `TestTimeout` | `EscalatedToHuman`. Runs `dotnet format --verify-no-changes` (opt-in), `dotnet build -c Release -p:TreatWarningsAsErrors=true`, then `dotnet test --no-build` with TRX + XPlat coverage (120s timeout). Every run persists a `VerificationReport` evidence row. `feedbackPrompt` is the markdown correction payload.

**Agent-loop integration:** `AgentExecutionRequest` accepts opt-in `verifySolutionFile` / `verifyMinCoverage` / `verifyMaxAttempts` (default 3 attempts). After a successful agent run, `IVerificationLoop` verifies the worktree and re-invokes the agent with `feedbackPrompt` on failure — exhausted retries mark the run `Failed` + `EscalatedToHuman` instead of moving to review.

### Harness — CLI DB Reader (E10, internal)

No HTTP endpoints — internal data-access layer consumed by `cli-metrics` (E11). `CliDatabaseMap` registers each managed CLI's SQLite databases under `$HOME`; `ICliDatabaseLocator` resolves paths/globs to `Available`/`Missing`; `ICliDatabaseReader` opens sources strictly `Mode=ReadOnly` (WAL/busy files are read from a deleted-after-use temp copy) with row/timeout/size budgets, whitelist-only table access, denied-table rejection and secret-named column exclusion; per-CLI `ICliDbExtractor`s emit normalized `CliSessionRecord`/`CliUsageRecord` with opaque `{file}|{rowid}` watermark cursors. Schema fingerprints (`user_version`, `application_id`, whitelisted columns) gate extraction — drift reports `SchemaDrifted` instead of throwing.

### Harness — CLI Metrics (E11)

Incremental ingestion of the E10 extractors into `CliMetricSource` / `CliSessionMetric` / `CliDailyUsageAggregate` (dedupe on `(SourceId, ExternalId)`, file-signature skip, per-source watermark, 90-day raw retention — aggregates kept). Auth required (cookie or `X-Api-Key`).

| Method | Path | Notes |
|--------|------|-------|
| POST | `/api/local/cli-metrics/sync` | Manual sync — `200` with `{ state, inFlight, sourcesSynced, sessionsIngested }`; `inFlight: true` when another sync is running; `404` when `Taskboard:CliMetrics:Enabled=false` |
| GET | `/api/local/cli-metrics/sources` | Per-source status: `kind`, `sourceName`, `status` (Available/Missing/Error/SchemaDrifted), `schemaDrifted`, `lastSyncUtc`, `lastError`, `rowCount` |
| GET | `/api/local/cli-metrics/summary?period=` | `period` = `7d` (default for /agents) `30d` `90d` `all`; returns totals + per-kind + per-day buckets with `lastActivityUtc` |
| GET | `/api/local/cli-metrics/sessions?kind=&from=&to=&take=` | Session rows, newest first; `take` clamped to 1–500 (default 100) |

Background sync: `CliMetricsSyncService` (`PeriodicTimer`, interval `Taskboard:CliMetrics:SyncIntervalMinutes`, default 15, min 1) runs one startup pass plus periodic syncs through the single-flight `CliMetricsSyncCoordinator`. FinOps feed: `ICliUsageMetricsProvider.GetUsageAsync` exposes per-kind/per-model token+session aggregates for `ade-observability-finops` (E14).

### Harness — Multi-Agent Pipelines (E12)

```http
GET  /api/harness/pipelines/templates
POST /api/harness/pipelines/start
GET  /api/harness/pipelines/{id}
POST /api/harness/pipelines/{id}/stages/{stageKey}/approve
POST /api/harness/pipelines/{id}/stages/{stageKey}/retry
POST /api/harness/pipelines/{id}/cancel
```

`GET templates` → `200` with the four built-in DAG templates: `standard-feature` (architect → approval-gate → builder → verifier → reviewer), `quick-patch` (builder → verifier), `test-driven` (architect → tester → builder → verifier) and `single-agent` (builder → verifier).

`POST start` body `{ templateId, repositoryFullName, repositoryPath, baseBranch, issueId?, initialPrompt, maxBudgetUsd?, agentOverride?, tierOverride?, skipVerification? }` → `201` with `{ pipelineExecutionId, templateId, status, worktreePath?, stages: [{ stageKey, name, kind, status, role?, agent?, attempts, handoffSummary?, lastError?, dependsOn }] }`. Unknown template or a cyclic stage graph → `400 INVALID_PIPELINE_DAG`. `agentOverride`/`tierOverride`/`skipVerification` are `single-agent`-only — they replace the AgentWork stage's agent/tier and drop the Verification stage; sending them with any other template → `400`.

All stages of an execution share one `IWorkspaceIsolationService` git worktree; each stage's completion output is synthesized into `handoffSummary` and injected as upstream context into its dependents. `kind`: `AgentWork` | `Approval` | `Verification`; `status`: `Pending` | `Running` | `WaitingApproval` | `Completed` | `Failed` | `Skipped`. Pipeline `status`: `Running` | `WaitingApproval` | `AwaitingRetry` | `Paused` | `Completed` | `Failed` | `Cancelled`.

`POST .../approve` body `{ comment? }` releases a `WaitingApproval` gate and dispatches dependents → `200`. `POST .../retry` body `{ adjustedPrompt? }` re-queues a `Failed` stage without restarting the pipeline → `202`. `POST .../cancel` stops in-flight stage processes and transitions the pipeline to `Cancelled` → `200`. Approving/retrying a stage in a non-matching state → `400`; unknown pipeline → `404`.

Dispatch runs on the `PipelineEngineService` background timer plus synchronous kicks after `start`/`approve`/`retry`; `PipelineEngine` resolves the DAG per execution scope so parallel stages never share a `DbContext`.

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

Cockpit "runs" are pipeline executions. `GET runs` → `200` with recent executions (newest first, same shape as `GET /api/harness/pipelines/{id}`). `POST runs` body `{ templateId, repositoryFullName, baseBranch, issueId?, specPath?, prompt, maxBudgetUsd?, agentOverride?, tierOverride?, skipVerification? }` resolves `repositoryPath` server-side via `IWorkspacePathResolver` (`~/repos/<name>`, confined to the workspace root), appends `specPath` content to the prompt when readable, and starts the pipeline → `201`. The overrides follow the same `single-agent`-only rule as `POST pipelines/start`. When `issueId` is present a `pipeline-run` `IssueHistoryEvent` links the issue to the run id (issue History tab → "Open in Cockpit"). `GET {id}` → `200 { execution, telemetry?, worktree? }` or `404`.

`GET {id}/events` → `200` with the in-memory buffered `CockpitEventDto[]` (`{ runId, timestampUtc, kind, title, payloadJson? }`; kinds: `stage`, `agent_output`, `verification`, `approval`, `steer`) for late-join/reconnect replay — events are also streamed live over the SignalR hub below.

`POST {id}/steer` body `{ instruction }` → `202`; the instruction is enqueued (`ISteerQueue`) and appended to the next dispatched stage prompt as "operator steer". Unknown run → `404`.

`POST {id}/pause` → `200` freezes the DAG between stages: in-flight stages finish, nothing new is dispatched and the engine tick skips the execution (no worktree attach, no budget-cap cancel) until resume. `POST {id}/resume` → `200` re-dispatches pending stages. Both publish a `status` cockpit event ("Run paused"/"Run resumed"); invalid transitions → `409 InvalidPipelineState`, unknown run → `404`. Pipeline `status` gains `Paused` (badge warning). Paused executions are never stale-reaped — the reaper only covers legacy `AgentRun` rows.

`POST {id}/approvals/{requestId}` body `{ action, comment? }` — `requestId` uses the `stage:<stageKey>` convention published by `RequireApproval`; `Allow` approves the `WaitingApproval` stage, `Deny` rejects it with the comment as the reason → `200`. Unknown run/request → `404`.

`POST {id}/create-pr` body `{ title, body? }` → `201 { prUrl }` — requires a `Completed` run; commits pending worktree changes, pushes the worktree branch and opens the PR via Octokit. Non-completed run → `409`; unknown run → `404`.

SignalR hub `/harness-cockpit-hub` (authenticated): client → server `JoinRunGroup(runId)` / `LeaveRunGroup(runId)`; server → client `ReceiveCockpitEvent(CockpitEventDto)` and `RequireApproval(ApprovalRequestDto)` (`{ runId, requestId, title, description, options }`). The Blazor pages are `/cockpit` (list + start) and `/cockpit/runs/{id}` (live timeline, diff tab, steer bar, approval modal, FinOps header).

### Living Specs (E13)

```http
GET  /api/specs?status={Draft|Approved|InImplementation|Done|Deprecated}&q={text}
GET  /api/specs/{specId}
POST /api/specs/{specId}/status
GET  /api/specs/drift-report
```

`GET /api/specs` → `200` with the catalog rows (`{ id, title, type, status, rawStatus, date, ticket, requirementsCount, acceptanceCriteriaCount, tasksTotal, tasksDone, warnings[] }`). `GET {id}` → `200` with the full detail (requirements, BDD criteria, tasks, referenced files, raw markdown) or `404`. `POST {id}/status` body `{ status }` rewrites only the `Status` metadata cell in the `.md` file → `200`; unknown status → `400` (`Taskboard:00033`); unknown spec → `404`.

`GET drift-report` → `200 { totalSpecs, staleSpecsCount, driftItems: [{ specId, currentStatus, suggestedStatus, reason, missingFiles }] }`. Draft/Approved/InImplementation specs whose "Files to create or modify" all exist on disk are flagged stale → `Done`; `Done` specs referencing deleted files → `Deprecated`.

Specs are scanned from `Taskboard:SpecsDir` (default: nearest `.specs/` directory walking up from the app base) — parsed live on every request via `MarkdigSpecParser`, so the file on disk is always the source of truth. The Blazor page is `/specs`.

All four endpoints accept `?repo=owner/name` (SPEC-20260920 RF-005): the specs dir resolves server-side to `<workspace root>/<name>/.specs` — clone missing or no `.specs/` → empty catalog (`404` for `{id}`); malformed `repo` → `400`. Without `repo` the configured default dir is used. `drift-report` with `repo` runs an on-demand scan (no cache).

### Observability & FinOps (E14)

```http
GET /api/harness/finops/summary?period={last-7-days|last-30-days|all}
GET /api/harness/runs/{id}/telemetry
```

`GET summary` → `200 { totalCostUsd, totalTokens, runsCount, costByAgent{}, costByModel{}, dailyCosts: [{ date, costUsd, totalTokens }] }` aggregated from `RunCostMetrics` (SQLite). `period` defaults to `last-30-days`.

`GET telemetry` → `200 { runId, totalTokens, inputTokens, outputTokens, cacheTokens, costUsd, durationSeconds?, budgetCapUsd?, metrics[] }` or `404` when the run has no recorded metrics. Accepts the `Guid` id with or without dashes.

Cost model: `RunCostMetric` rows are recorded whenever an agent reports token usage (stdout `usage`/`token_count` JSON lines parsed by `TokenUsageParser`, or `AgentExecutionResult.Usage`). USD cost is exact `decimal` math against the seeded `ModelPriceRates` table — longest-prefix model match, `"*"` fallback (RF-002).

Budget caps: `AgentExecutionRequest.maxBudgetUsd` (per run) and `PipelineStartRequest.maxBudgetUsd` (per pipeline) — when cumulative cost crosses the cap the run is cancelled and finished with `AgentRunState.BudgetExceeded` (mid-flight when the CLI streams usage, post-run otherwise); over-cap pipelines are cancelled before the next stage dispatch and their stages are skipped (RF-003).

Telemetry: every run/stage/verification emits `System.Diagnostics.Activity` spans from the `Taskboard.Harness` activity source (`harness.run`, `harness.stage`, `harness.tool_call`, `harness.verification`) tagged with `harness.run_id`, `agent.type`, `model.name`, `tokens.*`, `cost.usd` (RF-004). Spans are in-process only unless `Taskboard:Telemetry:OtlpEndpoint` is set in `appsettings.json` — nothing is exported without explicit configuration (guardrail §8). The Blazor dashboard is `/finops`.

## SSE

### Global events

```http
GET /api/events
Accept: text/event-stream
```

Reserved stream for global events (no producers currently emit on it — per-thread AI events use the endpoint below).

### Per-thread AI events

```http
GET /api/local/ai/threads/:id/events
Accept: text/event-stream
```

`GET .../events` is dual-mode: `Accept: application/json` returns a one-shot `200 { events: [{ id, threadId, role, content, createdAt }] }` snapshot; any other Accept streams SSE — the stored backlog replayed as `ai_chat.event` frames followed by live `ai_chat.event` (new messages, including streamed assistant deltas) and `ai_chat.run` (run status changes: `running`/`completed`/`failed`) frames. `POST .../events` `{ role: "user|assistant|activity|error", content }` persists and publishes an event; `POST .../runs` starts a background run executed by the thread's bound agent CLI — one-shot, transcript as prompt (the assistant answers the latest user message); `DELETE /api/local/ai/threads/:id` removes the thread with its events and runs (204 | 404).

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
