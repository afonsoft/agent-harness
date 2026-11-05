# taskctl — agent manual (Harness)

`taskctl` is the operator CLI for the Harness (agent-harness) taskboard — a
local-first .NET 10 server whose board source of truth is GitHub. Use it to
inspect the bound context, read issue history and comments, post handoff
comments, manage the cloud session, back up the local database, run
preflight checks and dispatch agent runs.

The installed binary is the authority. `taskctl <command> --help` shows exact
arguments; `taskctl --skill` prints this manual; `taskctl --skill --json`
emits the machine-readable manifest `{version, commands[], errorKinds[]}`.

## Preflight rules

- Prefer `--json` when reading output programmatically.
- Errors go to stderr as `error: <message>`; stdout carries only results.
- Never print tokens, API keys or agent credential values.
- `taskctl --help` and `taskctl --skill` work offline — no server needed.

## Environment

| Variable | Purpose | Default |
|---|---|---|
| `HARNESS_URL` | Base URL of the Harness REST API | `http://127.0.0.1:47823` |
| `HARNESS_THREAD_ID` | Thread binding for agent context | unset |
| `HARNESS_DATA_DIR` | Local data dir (SQLite location) | `~/.agent-harness` |

> Run-context variables (`HARNESS_SESSION_ID`, `HARNESS_TASK_ID`,
> `HARNESS_DISPATCH_ID`, `HARNESS_SCOPE`, `HARNESS_AGENT_NAME`,
> `HARNESS_API_URL`, `HARNESS_AGENT_TOKEN`) are injected into managed agent
> sessions by SPEC-20261104-agent-context-templates — this section updates
> when that surface ships.

## Commands

### context

```bash
taskctl context:current [--json]
```

Current workspace context: resolved base URL, current project, current
workspace, cloud URL.

### GitHub board issues

Board cards live under `/api/github/...` identified by `owner/repo` + issue
number or GitHub issue id.

```bash
taskctl ghissue:history <issueId> [--take N] [--json]
taskctl ghissue:comments <owner/name> <issueNumber> [--take N] [--json]
taskctl ghissue:comment <owner/name> <issueNumber> "<body>" [--json]
```

- `ghissue:history` — unified timeline, newest first: column moves, edits,
  closes, agent runs.
- `ghissue:comments` — GitHub comments chronologically (author, body,
  timestamps, htmlUrl).
- `ghissue:comment` — posts a comment. Comments are the **agent handoff
  channel**: read history + comments before taking an issue; post a summary
  comment when finishing a stage so the next agent keeps the context.

### Cloud session

```bash
taskctl cloud:login [https-origin] [--json]
taskctl cloud:status [--json]
taskctl cloud:logout [--json]
```

`cloud:login` stores the upstream origin in local config (mode `0600`) and
marks the companion's cloud session connected; `cloud:logout` disconnects.
`<https-origin>` is positional; `--url` is the global option overriding the
local API base.

### Local backup/restore

```bash
taskctl backup <destDir> [--keep N] [--db PATH] [--json]
taskctl restore <backupFile> [--db PATH] [--force] [--json]
```

DB path resolves `--db` > `HARNESS_DATA_DIR` > `~/.agent-harness`. These
commands touch files directly — no server round-trip.

### Doctor and runs

```bash
taskctl doctor [--json]
taskctl run "<prompt>" --repo owner/name [--branch main] [--issue ID] \
    [--template ID] [--spec PATH] [--max-budget USD] [--agent NAME] [--tier T] \
    [--single-agent] [--skip-verification] [--dry-run] [--json]
```

- `doctor` — server-side preflight report `{verdict, checks[]}`; exit 0 on
  ready/warning, 2 on blocked.
- `run --dry-run` — resolves config + preflights server-side; nothing
  persists, nothing spawns. Without `--dry-run` it dispatches a real run.

### skill

```bash
taskctl --skill [--json]
taskctl skill [--json]
```

Prints this manual (embedded in the binary — always version-matched) or its
JSON manifest.

### agent:explain / agent:reload-manifests

```bash
taskctl agent:explain <sessionKey> [--json]
taskctl agent:reload-manifests [--json]
```

- `agent:explain` — returns the detection decision for a live agent session:
  matched rule id, per-rule `failedGate`, flags and snapshot hash. Use it to
  debug why attention state did (not) transition.
- `agent:reload-manifests` — hot-reloads the agent-detection catalog: bundled
  manifests plus overrides from `{dataDir}/agent-detection/*.toml`. Atomic
  swap; a broken override falls back to bundled rules with a warning, never
  an empty catalog.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | generic error |
| 2 | usage error / validation (HTTP 400/422) |
| 3 | network timeout or server unavailable |
| 4 | authentication/authorization (HTTP 401/403) |
| 5 | conflict (HTTP 409) |
| 130 | cancelled (Ctrl+C) |

## MCP server surface

The companion MCP server (`src/Taskboard.Mcp`, STDIO transport) exposes 12
tools against the same REST API: `get_issue_history`,
`list_github_issue_comments`, `add_github_issue_comment`, `cloud_status`,
`list_issues`, `get_issue`, `move_issue`, `list_specs`, `get_spec`,
`list_jobs`, `list_runs`, `get_run_status`.

Run it with `HARNESS_URL` pointed at the API:

```bash
cd src/Taskboard.Mcp && HARNESS_URL=http://127.0.0.1:47823 dotnet run
```

## Workflow contract

1. `taskctl context:current --json` — confirm bound workspace/project.
2. `taskctl ghissue:history <id> --json` + `taskctl ghissue:comments` —
   read before taking an issue.
3. Only pick `todo`/`backlog` columns; never move columns without the user.
4. Honor `HARNESS_THREAD_ID` — bind comments/context to that thread.
5. Report stages as issue comments (`ghissue:comment`) — that is the
   handoff channel between agents and steps.
6. Close/move to `done` only after explicit user acceptance.
