# agent-harness Specifications

This directory contains the **Spec-Driven Development (SDD)** specifications for `agent-harness`, a local-first, AI-friendly taskboard cloned from `dashi-taskboard` and rewritten in **C# 14 / .NET 10**.

The merged set consolidates content from the original `.specs/` and `.specs2/` directories. The most complete version of each specification is kept here, following the **SSD Engineering Template** with maximum detail for the C# rewrite.

> **Default language:** English (en-us). Portuguese (pt-br) translations live under `docs/`.

## Index

| Spec | Module | Description |
|---|---|---|
| [CAPABILITY-MAP.md](CAPABILITY-MAP.md) | Global | Module build order, dependencies, and capability matrix |
| [SPEC-000-overview.md](SPEC-000-overview.md) | Global | Vision, stack, commands, constraints, and repository structure |
| [SPEC-001-domain-model.md](SPEC-001-domain-model.md) | Domain | Entities, value objects, aggregates, domain events, invariants |
| [SPEC-002-rest-api.md](SPEC-002-rest-api.md) | REST API | Endpoints, routing, SSE, auth, error contracts, ProblemDetails |
| [SPEC-003-cli.md](SPEC-003-cli.md) | CLI | `taskctl` command map, output envelope, exit codes |
| [SPEC-004-mcp.md](SPEC-004-mcp.md) | MCP | MCP server tools, schemas, transport, integration points |
| [SPEC-005-ai-chat.md](SPEC-005-ai-chat.md) | AI Chat | Threads, runs, events, composer candidates, rebind |
| [SPEC-006-cloud.md](SPEC-006-cloud.md) | Cloud | Cloud companion, Cloudflare D1/R2 proxy, Basic Auth |
| [SPEC-007-workflow-automation.md](SPEC-007-workflow-automation.md) | Workflow | Workflow graph engine, Codex automation, device workspaces |
| [SPEC-008-frontend.md](SPEC-008-frontend.md) | Frontend | Blazor / .NET MAUI rewrite strategy |
| [SPEC-012-legacy-react.md](SPEC-012-legacy-react.md) | Frontend (ref) | Reference for the original React/Vite UI |
| [SPEC-009-skill.md](SPEC-009-skill.md) | Skills | `manage-taskboard` agent skill and references |
| [SPEC-010-integrations.md](SPEC-010-integrations.md) | Integrations | Jira sync, DeepSeek harness, shared execution modules |
| [SPEC-011-persistence.md](SPEC-011-persistence.md) | Persistence | EF Core + SQLite schema, migrations, indexes, repositories |
| [SPEC-015-agent-orchestration.md](SPEC-015-agent-orchestration.md) | Agents | CLI agent discovery, ACP adapter, background orchestration, SignalR log streaming |

## Conventions

- All specifications are written in Markdown.
- C# snippets use **C# 14 / .NET 10**.
- Domain language is preserved from the original `dashi-taskboard` where behavior must be cloned.
- `en-us` is the source language; `pt-br` translations are in `docs/` and `README.pt-br.md`.

## How to use these specs

1. Start with `SPEC-000-overview.md` and `CAPABILITY-MAP.md` for context.
2. Implement modules in the build order defined in `CAPABILITY-MAP.md`.
3. Each spec contains enough detail to generate domain, application, infrastructure, and API code.
