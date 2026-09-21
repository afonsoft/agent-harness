# System Documentation

## Overview

**Harness** (`agent-harness`) is a .NET 10 clone of the `dashi-taskboard` local-first taskboard. It is built with ABP N-Layer / Domain-Driven Design and exposes HTTP REST, SSE, CLI (`taskctl`), and MCP server interfaces.

## Architecture

| Layer | Responsibility | Projects |
|---|---|---|
| Domain | Business rules, aggregates, entities, value objects | `Taskboard.Domain` |
| Application.Contracts | DTOs, interfaces, commands/queries | `Taskboard.Application.Contracts` |
| Application | Use cases, MediatR handlers | `Taskboard.Application` |
| Infrastructure | EF Core, SQLite, repositories | `Taskboard.EntityFrameworkCore` |
| Presentation | Minimal APIs, SSE, static files | `Taskboard.Server` |
| Tools | CLI, MCP server | `Taskboard.Cli`, `Taskboard.Mcp` |
| Modules | AI chat, workflow, cloud, integrations | `Taskboard.*` |

## Directory Structure

```text
src/               .NET 10 projects
tests/             xUnit/Shouldly/NSubstitute tests
.specs/            SDD specifications
docs/              System documentation
.claude/           Claude Code / Devin CLI harness
.devin/            Devin CLI configuration
.agent/            Google Antigravity skills
```

## Início Rápido

See [`README.md`](../README.md) (en-us default) or [`README.pt-br.md`](../README.pt-br.md).

## References

- [installation.md](./installation.md) — Installation guide (en-us)
- [installation.pt-br.md](./installation.pt-br.md) — Guia de instalação (pt-br)
- [technologies.md](./technologies.md)
- [packages.md](./packages.md)
- [plugins.md](./plugins.md)
- [features.md](./features.md)
- [api.md](./api.md)
