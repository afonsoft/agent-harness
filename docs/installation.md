# Installation

This guide covers how to install, build, configure, and run `taskboard-ai` locally.

> **Default language:** English (en-us). See [installation.pt-br.md](installation.pt-br.md) for the Portuguese version.

## Overview

`taskboard-ai` is a local-first, AI-native taskboard written in **C# 14 / .NET 10**. It provides a SQLite-backed task system, REST API, Server-Sent Events (SSE), a `taskctl` CLI, an MCP server, and a Blazor WebAssembly web UI.

## Prerequisites

| Tool | Minimum version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | Required for build, test, and run |
| [Git](https://git-scm.com/) | 2.30 | For cloning the repository |
| `dotnet-ef` | 10.0 | Optional, for migrations |
| `jq` | 1.6 | Optional, for parsing CLI JSON output examples |

Verify the .NET SDK version:

```bash
dotnet --version
```

The output must start with `10.`.

## Clone

```bash
git clone https://github.com/afonsoft/taskboard-ai.git
cd taskboard-ai
```

## Build

Restore and build the solution:

```bash
dotnet restore Taskboard.sln
dotnet build Taskboard.sln
```

For a Release build:

```bash
dotnet build Taskboard.sln --configuration Release
```

The repository uses `TreatWarningsAsErrors`; the build must complete with zero warnings.

## Test

Run the full test suite:

```bash
dotnet test Taskboard.sln
```

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `TASKBOARD_PORT` | `47823` | HTTP port used by the server |
| `TASKBOARD_DATA_DIR` | `.data` under the server content root | Directory where `taskboard.sqlite` and `.admin-password` are stored |
| `GITHUB_TOKEN` | *(none)* | GitHub personal access token for the Kanban board (`/github-board`) |
| `TASKBOARD_URL` | `http://127.0.0.1:47823` | Base URL used by the `taskctl` CLI and MCP server |
| `TASKBOARD_API_KEY` | *(none)* | API key sent as `X-Api-Key` by `taskctl`/MCP to authenticate against the server's `/api` (all endpoints except `login`, `auth/me`, `meta` and health require cookie or API-key auth) |
| `ASPNETCORE_URLS` | *(none)* | Overrides `TASKBOARD_PORT` with a complete URL such as `http://0.0.0.0:47823` |

Set variables for the current shell session:

```bash
export TASKBOARD_PORT=47823
export TASKBOARD_DATA_DIR="$PWD/.data"
export GITHUB_TOKEN="ghp_your_token"
```

> **Breaking change:** Legacy `CODEX_TASKBOARD_PORT` and `CODEX_TASKBOARD_DATA_DIR` variables are no longer read. Use `TASKBOARD_PORT` and `TASKBOARD_DATA_DIR` instead.

## Run the server

```bash
dotnet run --project src/Taskboard.Server
```

The server will:
- Apply EF Core migrations automatically.
- Create the default `local` project if it does not exist.
- Listen on `http://127.0.0.1:47823` by default.

Verify the server is running:

```bash
curl http://127.0.0.1:47823/health
```

Expected response:

```json
{ "status": "ok", "timestamp": "..." }
```

To change the port:

```bash
TASKBOARD_PORT=8080 dotnet run --project src/Taskboard.Server
```

## Run the CLI

The CLI is invoked through the `taskctl` project:

```bash
dotnet run --project src/Taskboard.Cli -- --help
```

List projects:

```bash
dotnet run --project src/Taskboard.Cli -- project list
```

Create a project:

```bash
dotnet run --project src/Taskboard.Cli -- project create --id my-project --name "My Project" --workspace-path /abs/path/to/project
```

The CLI uses `TASKBOARD_URL` to find the server. To point it at a different URL:

```bash
TASKBOARD_URL=http://127.0.0.1:8080 dotnet run --project src/Taskboard.Cli -- project list
```

## Run the MCP server

The MCP server communicates over STDIO:

```bash
dotnet run --project src/Taskboard.Mcp
```

For client configuration, see [plugins.md](./plugins.md) and [SPEC-004](../.specs/SPEC-004-mcp.md).

## Use the web UI

Open `http://127.0.0.1:47823/github-board` in a browser. The default admin credentials are configured from `appsettings.json` or environment variables (`TASKBOARD_ADMIN_USERNAME`, `TASKBOARD_ADMIN_PASSWORD`).

## GitHub Kanban board

To enable the GitHub Kanban board, set `GITHUB_TOKEN` before starting the server:

```bash
export GITHUB_TOKEN="ghp_your_token"
dotnet run --project src/Taskboard.Server
```

The board is available at `/github-board`. Without a token, the board will show errors or an empty state.

## Verification checklist

After installation:

1. `dotnet build Taskboard.sln` completes with zero warnings.
2. `dotnet test Taskboard.sln` passes.
3. `curl http://127.0.0.1:47823/health` returns `{ "status": "ok" }`.
4. The web UI opens at `/github-board`.
5. `dotnet run --project src/Taskboard.Cli -- --help` prints the command list.

## Troubleshooting

### Port already in use

```bash
TASKBOARD_PORT=8080 dotnet run --project src/Taskboard.Server
```

### Missing .NET 10 SDK

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and verify with `dotnet --version`.

### SQLite database is locked

Stop any running server instances. Only one server process may use the same `TASKBOARD_DATA_DIR` at a time.

### Missing `GITHUB_TOKEN`

The Kanban board requires `GITHUB_TOKEN`. Generate a token at [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens) with at least `repo` and `read:org` scopes.

### CLI cannot connect

Check that the server is running and that `TASKBOARD_URL` matches the server URL:

```bash
TASKBOARD_URL=http://127.0.0.1:47823 dotnet run --project src/Taskboard.Cli -- project list
```

### Permissions on Linux / macOS

If `install.sh` is used, ensure it is executable:

```bash
chmod +x install.sh
```

## Next steps

- Read the [system documentation](./README.md).
- Explore the [API documentation](./api.md).
- Check the [architecture diagrams](./architecture/architecture.html).
- Review the [SPEC index](../.specs/README.md) for implementation details.
