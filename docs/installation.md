# Installation

This guide covers how to install, build, configure, and run `agent-harness` locally.

> **Default language:** English (en-us). See [installation.pt-br.md](installation.pt-br.md) for the Portuguese version.

## Overview

`agent-harness` is a local-first, AI-native taskboard written in **C# 14 / .NET 10**. It provides a SQLite-backed task system, REST API, Server-Sent Events (SSE), a `taskctl` CLI, an MCP server, and a Blazor WebAssembly web UI.

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
git clone https://github.com/afonsoft/agent-harness.git
cd agent-harness
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
| `HARNESS_PORT` | `47823` | HTTP port used by the server |
| `HARNESS_DATA_DIR` | `.data` under the server content root | Directory where `harness.sqlite` and `.admin-password` are stored |
| `GITHUB_TOKEN` | *(none)* | GitHub personal access token for the Kanban board (`/github-board`) |
| `HARNESS_URL` | `http://127.0.0.1:47823` | Base URL used by the `taskctl` CLI and MCP server |
| `HARNESS_API_KEY` | *(none)* | API key sent as `X-Api-Key` by `taskctl`/MCP to authenticate against the server's `/api` (all endpoints except `login`, `auth/me`, `meta` and health require cookie or API-key auth) |
| `ASPNETCORE_URLS` | *(none)* | Overrides `HARNESS_PORT` with a complete URL such as `http://0.0.0.0:47823` |
| `HARNESS_SKILLS_REPO` | `afonsoft/skills` | Skills repository installed/synced from `/settings` (`npx skills add <repo> -g --all --copy` + `install.sh --all`) |
| `HARNESS_RAG_NAME` | `knowledge` | Managed MCP server name provisioned into enabled agent CLIs |
| `HARNESS_RAG_URL` | *(none)* | RAG MCP URL provisioned into enabled agent CLIs (empty = entry removed) |
| `HARNESS_RAG_API_KEY` | *(none)* | Bearer key for the RAG MCP server (masked in reads; also editable in `/settings`) |
| `HARNESS_TERMINAL_ENABLED` | `true` | Enables the `/terminal` bash PTY page and `/agents` CLI status board (disable to remove the web shell surface) |

Set variables for the current shell session:

```bash
export HARNESS_PORT=47823
export HARNESS_DATA_DIR="$PWD/.data"
export GITHUB_TOKEN="ghp_your_token"
```

> **Migration:** Legacy `TASKBOARD_*` variables are still read as a deprecated fallback for one cycle (SPEC-20260922-harness-home-rename); `CODEX_TASKBOARD_*` variables are no longer read. Use `HARNESS_*` names.

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
HARNESS_PORT=8080 dotnet run --project src/Taskboard.Server
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

The CLI uses `HARNESS_URL` to find the server. To point it at a different URL:

```bash
HARNESS_URL=http://127.0.0.1:8080 dotnet run --project src/Taskboard.Cli -- project list
```

## Run the MCP server

The MCP server communicates over STDIO:

```bash
dotnet run --project src/Taskboard.Mcp
```

The same tools are also exposed over **Streamable HTTP** (stateless, MCP C# SDK v2) at `POST /api/mcp` inside the authenticated API group — register it in any agent CLI with a URL + `X-Api-Key` header instead of spawning the executable:

```json
{ "type": "http", "url": "http://127.0.0.1:47823/api/mcp", "headers": { "X-Api-Key": "<key>" } }
```

For client configuration, see [plugins.md](./plugins.md) and [SPEC-004](../.specs/SPEC-004-mcp.md).

## Use the web UI

Open `http://127.0.0.1:47823/github-board` in a browser. The default admin credentials are configured from `appsettings.json` or environment variables (`HARNESS_ADMIN_USERNAME`, `HARNESS_ADMIN_PASSWORD`).

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
HARNESS_PORT=8080 dotnet run --project src/Taskboard.Server
```

### Missing .NET 10 SDK

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and verify with `dotnet --version`.

### SQLite database is locked

Stop any running server instances. Only one server process may use the same `HARNESS_DATA_DIR` at a time.

### Missing `GITHUB_TOKEN`

The Kanban board requires `GITHUB_TOKEN`. Generate a token at [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens) with at least `repo` and `read:org` scopes.

### CLI cannot connect

Check that the server is running and that `HARNESS_URL` matches the server URL:

```bash
HARNESS_URL=http://127.0.0.1:47823 dotnet run --project src/Taskboard.Cli -- project list
```

### Permissions on Linux / macOS

If `install.sh` is used, ensure it is executable:

```bash
chmod +x install.sh
```

## Agent CLIs and the web terminal

The `/agents` page lists the supported agent CLIs (Claude Code, Codex, OpenCode, Devin CLI, Antigravity `agy`) with install/auth status, and `/terminal` opens an interactive bash session for running their login flows (`claude`, `codex login`, `devin auth login`, `agy`). The terminal is gated by `Taskboard:Terminal:Enabled` (`HARNESS_TERMINAL_ENABLED`).

- **Bare-metal/host install:** the server uses the real `$HOME`, so CLIs already installed on the host are detected as-is.
- **Docker image:** the runtime stage ships Node.js LTS plus all five CLIs pre-installed, and sets `HOME=/data/home` so CLI credentials land inside the `/data` volume and survive container recreation. Authenticate each CLI once from `/terminal`.

### Docker / docker-compose

```bash
docker compose up -d          # build + run on http://localhost:47823
```

Everything persistent lives in the `taskboard-data` named volume mounted at `/data`:

| Path | Contents |
|---|---|
| `/data/harness.sqlite` | database + configuration overrides |
| `/data/admin.json` | generated admin credentials |
| `/data/skills-cache/` | skills repository cache |
| `/data/home/` | container `$HOME` — CLI credentials (`.claude`, `.codex`, `.config/…`) |
| `/data/home/repos` | agent workspace — the container's `~/repos`, default workdir for agent runs, pipelines and the terminal |

The image sets `HARNESS_DATA_DIR=/data` (overriding `appsettings.Production.json`), declares `VOLUME /data` and a `HEALTHCHECK` on `/health`. Git safe.directory is preconfigured, so bind-mounting host-owned repos works:

```yaml
# docker-compose.yml — opt-in: expose the server's real repos to the agents
volumes:
  - taskboard-data:/data
  - ${HARNESS_REPOS_DIR}:/data/home/repos   # e.g. HARNESS_REPOS_DIR=~/repos
```

Files the agents create inside the bind mount belong to root on the host (the container runs as root); run `user: "$(id -u):$(id -g)"` only if you also chown the volume content.

## Next steps

- Read the [system documentation](./README.md).
- Explore the [API documentation](./api.md).
- Check the [architecture diagrams](./architecture/architecture.html).
- Review the [SPEC index](../.specs/README.md) for implementation details.
