# taskboard-ai

[![.NET Build and Test](https://github.com/afonsoft/taskboard-ai/actions/workflows/dotnet.yml/badge.svg)](https://github.com/afonsoft/taskboard-ai/actions/workflows/dotnet.yml)
[![Code Quality](https://github.com/afonsoft/taskboard-ai/actions/workflows/code-quality.yml/badge.svg)](https://github.com/afonsoft/taskboard-ai/actions/workflows/code-quality.yml)
[![CodeQL](https://github.com/afonsoft/taskboard-ai/actions/workflows/codeql.yml/badge.svg)](https://github.com/afonsoft/taskboard-ai/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Default language:** English (en-us). See [README.pt-br.md](README.pt-br.md) for the Portuguese version.

A local-first, AI-native taskboard inspired by `dashi-taskboard`, rewritten in **C# 14 / .NET 10**.

## Overview

`taskboard-ai` is a local-first issue board for developers and AI agents. It provides a SQLite-backed task system, REST API, Server-Sent Events (SSE), a `taskctl` CLI, an MCP server, AI chat integration, and a Blazor WebAssembly web UI — all implemented in .NET 10 with ABP N-Layer / DDD.

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Language | C# | 14 |
| Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core | 10.0 |
| DDD Framework | ABP N-Layer | 9.x |
| ORM | Entity Framework Core | 10.0.12 |
| Database | SQLite | bundled |
| CLI Parser | System.CommandLine | latest stable |
| MCP SDK | ModelContextProtocol | 2.2.0 |
| Tests | xUnit + Shouldly + NSubstitute | latest stable |
| Frontend | Blazor WebAssembly | .NET 10 |
| UI Components | Blazor.Bootstrap | 4.0.0 |
| Real-time | ASP.NET Core SignalR | 10.0 |
| GitHub API Client | Octokit | 14.0.0 |
| Mediator | MediatR | 12.4.1 |

## Architecture

```text
src/
  Taskboard.Domain/                 # Aggregates, entities, value objects, domain events
  Taskboard.Domain.Shared/          # Shared domain primitives
  Taskboard.Application.Contracts/  # DTOs, interfaces
  Taskboard.Application/            # Commands, queries, handlers (MediatR)
  Taskboard.EntityFrameworkCore/    # EF Core + SQLite + repositories
  Taskboard.Server/                 # ASP.NET Core Minimal APIs + SSE
  Taskboard.Cli/                    # taskctl CLI (System.CommandLine)
  Taskboard.Mcp/                    # MCP server (ModelContextProtocol SDK)
  Taskboard.AiChat/                 # AI chat threads/runs/events
  Taskboard.Workflow/               # Workflow workspaces + automation
  Taskboard.Cloud/                  # Cloud companion + sync
  Taskboard.Integrations/           # Jira, GitHub, agent orchestration, execution helpers
  Taskboard.Maui/                   # Optional desktop Blazor Hybrid
  Taskboard.Client/                 # Blazor WebAssembly host (WASM boot, loading UI)
  Taskboard.Blazor/                 # Shared Blazor UI components (RCL)
tests/
  Taskboard.Tests.Unit/             # 89 unit tests
  Taskboard.Tests.Integration/      # 9 integration tests
```

## Quick Start

```bash
git clone https://github.com/afonsoft/taskboard-ai.git
cd taskboard-ai
dotnet restore Taskboard.sln
dotnet build Taskboard.sln
dotnet test Taskboard.sln
dotnet run --project src/Taskboard.Server
```

See [`docs/installation.md`](docs/installation.md) for detailed setup, environment variables, and troubleshooting.

## CLI Installer

Install the `taskctl` CLI to `/usr/local/bin`:

```bash
./install-cli.sh
```

See [`install-cli.sh`](install-cli.sh) and [`docs/installation.md`](docs/installation.md) for details.

## Continuous Integration

GitHub Actions provide:

- Build and test in Release mode, format verification, a line-coverage gate (currently 45%, ratcheting up to the 80% target), and vulnerable-package checks.
- SonarCloud analysis when the `SONAR_TOKEN` secret is configured.
- CodeQL analysis for C# and GitHub Actions.
- Weekly NuGet and GitHub Actions updates through Dependabot.

## GitHub Kanban & AI Agents

Set `GITHUB_TOKEN` before starting the server:

```bash
export GITHUB_TOKEN=your-github-token
```

Open `/github-board` to view GitHub issues as a Kanban board. Drag an issue to **In Progress**, select an installed agent, and follow execution in the **Logs** tab. Supported CLIs include Devin, Claude, Codex, OpenCode, and OpenHands. Real-time agent logs are streamed through the SignalR hub at `/agent-log-hub`.

## Recent Highlights

- Agent orchestration with CLI detection and SignalR log streaming.
- SQLite persistence for `AgentLogMessage` via EF Core.
- JSON-RPC ACP adapter over stdin/stdout for agent communication.
- `.devin/` harness for Devin CLI and `~/.gemini/skills/` for Google Antigravity.
- Refined GitHub Actions with cache, concurrency, permissions, SonarCloud, and CodeQL.

## Build Order

See [`.specs/CAPABILITY-MAP.md`](.specs/CAPABILITY-MAP.md).

1. `domain-model`
2. `persistence`
3. `rest-api`
4. `cli`
5. `mcp`, `ai-chat`, `cloud`, `workflow-automation`
6. `skill`, `frontend`, `integrations`

## Documentation

- [`docs/README.md`](docs/README.md) — System documentation
- [`docs/technologies.md`](docs/technologies.md) — Technologies and versions
- [`docs/packages.md`](docs/packages.md) — NuGet and NPM packages
- [`docs/plugins.md`](docs/plugins.md) — Plugins and integrations
- [`docs/features.md`](docs/features.md) — Features
- [`docs/api.md`](docs/api.md) — REST API and SSE
- [`docs/architecture/`](docs/architecture/) — Architecture diagrams
- [`.specs/`](.specs/) — Specification-driven development (SDD) specs

## Agent Harness

- [`CLAUDE.md`](CLAUDE.md) — Agent single source of truth
- [`.claude/`](.claude/) — Claude Code / Devin CLI harness
- [`.devin/config.json`](.devin/config.json) — Devin CLI configuration
- `.claude/skills/` — Skills catalog (afonsoft/skills + manage-taskboard); Google Antigravity uses `~/.gemini/skills/` (global)
- [`.claude/memory/orchestrator_stats.md`](.claude/memory/orchestrator_stats.md) — Orchestrator session state

The agent harness uses skills from [`afonsoft/skills`](https://github.com/afonsoft/skills):

```bash
npx skills add afonsoft/skills
```

This installs skills into `.claude/skills/`; `skills-lock.json` records the locked skill sources.

## Contributing

- Create a feature branch from `main` or `develop`.
- Follow `.specs/` and the global rules in `.claude/rules/global-rules.md`.
- Ensure `dotnet build` and `dotnet test` pass.
- Open a Pull Request.

## License

MIT — see [`LICENSE`](LICENSE).
