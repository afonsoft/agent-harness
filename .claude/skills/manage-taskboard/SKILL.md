---
name: manage-taskboard
description: Use the Harness (agent-harness) CLI `taskctl` and its MCP server to manage the GitHub-backed board — issue history/comments for handoff, cloud session, backup/restore, doctor preflight and agent runs. Prefer it over raw REST calls or ad-hoc scripts when the task touches the taskboard. Not for editing Harness source code — that is ordinary repo work.
---

# Manage Taskboard (Harness)

The Harness (agent-harness) is a local-first .NET 10 taskboard whose board
source of truth is GitHub. The installed `taskctl` binary is the authority
for its own surface — documentation lives inside it, always version-matched.

```bash
taskctl --skill        # full agent manual (commands, exit codes, contracts)
taskctl --skill --json # machine manifest {version, commands[], errorKinds[]}
```

Run `taskctl --skill` and follow it. Do not rely on memorized command
syntax; it changes with the binary.

If `taskctl` is missing from PATH, build it once:

```bash
dotnet build src/Taskboard.Cli/Taskboard.Cli.csproj
ln -sf "$(pwd)/src/Taskboard.Cli/bin/Debug/net10.0/taskctl" ~/.local/bin/taskctl
```

## Environment

| Variable | Purpose | Default |
|---|---|---|
| `HARNESS_URL` | Harness REST API base URL | `http://127.0.0.1:47823` |
| `HARNESS_THREAD_ID` | Thread binding for agent context | unset |

Never expose tokens or API keys in output or logs.

## MCP server

The companion MCP server (`src/Taskboard.Mcp`, STDIO) exposes the board
surface as tools (issue history/comments, move_issue, specs, jobs, runs).
The exact tool list is in `taskctl --skill` under "MCP server surface" — it
tracks the binary, this file does not duplicate it.

```bash
cd src/Taskboard.Mcp && HARNESS_URL=http://127.0.0.1:47823 dotnet run
```

## References

- `.specs/SPEC-003-cli.md` — CLI spec
- `.specs/SPEC-004-mcp.md` — MCP server spec
- `.specs/SPEC-20261104-cli-agent-manual.md` — this stub/manual design
