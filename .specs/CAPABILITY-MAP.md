# CAPABILITY-MAP

Global capability map for the **agent-harness** .NET 10 implementation.

This module order guarantees that lower layers are designed (and built) before upper layers consume them.

## Module Matrix

| Build order | Module id | Responsibility | Depends on |
|---|---|---|---|
| 1 | `domain-model` | Entities, enums, state rules (status/priority), optimistic concurrency model | — |
| 2 | `persistence` | SQLite storage, EF Core migrations, indexes, referential integrity, disk attachments | domain-model |
| 3 | `rest-api` | HTTP server, manual `/api/*` routing, instance-token auth, CORS, SSE EventHub, concurrency 409 | domain-model, persistence |
| 4 | `cli` | `taskctl` command-line client that talks HTTP to the service | rest-api |
| 5 | `mcp` | MCP server exposing tools mapped to the API/CLI | cli, rest-api |
| 6 | `ai-chat` | AI chat subsystem: threads, runs, per-thread SSE events, Codex app-server spawn | rest-api, persistence |
| 7 | `cloud` | Cloud mode: companion loopback + Cloudflare D1/R2 proxy, Basic Auth, review polling | rest-api, persistence |
| 8 | `workflow-automation` | Workflow graph engine (control-flow) and auto-claim via Codex | domain-model, rest-api |
| 9 | `skill` | `manage-taskboard` skill (markdown + references) and Codex automation skill | rest-api, cli |
| 10 | `frontend` | Blazor/.NET MAUI desktop UI rewritten to consume REST API + SSE | rest-api |
| 11 | `integrations` | Jira connection/sync and DeepSeek harness adapter | rest-api, persistence |
| 12 | `harness-workspace-isolation` | Git Worktree sandbox per run, env sanitization, diff extraction | persistence, integrations |
| 13 | `harness-context-memory` | Prompt assembly, token budgeting, compaction, cross-session memory | persistence, harness-workspace-isolation |
| 14 | `harness-security-permission-gateway` | Dynamic command classification, path jail, secret scrubber | harness-workspace-isolation |
| 15 | `harness-verification-loop` | Automated build, test, coverage ratchet gate, red-green feedback | harness-workspace-isolation |
| 16 | `ade-multi-agent-orchestration` | Multi-agent DAG engine, role specialization, context handoffs | harness-workspace-isolation, harness-context-memory, harness-verification-loop |
| 17 | `ade-living-specs` | Living spec parsing, status tracking, drift detection, BDD links | persistence, ade-multi-agent-orchestration |
| 18 | `ade-observability-finops` | OpenTelemetry spans, token accounting, cost metrics, budget caps | ade-multi-agent-orchestration |
| 19 | `ade-cockpit-hitl` | Blazor WASM run cockpit, live event stream, diff viewer, HITL gates | ade-multi-agent-orchestration, ade-living-specs, ade-observability-finops, rest-api |
| 20 | `cli-db-reader` | Safe read-only access to agent CLIs' local SQLite DBs: discovery, WAL-copy, schema fingerprint, whitelisted extraction | domain-model |
| 21 | `cli-metrics` | Incremental ingestion of CLI session/usage metadata, aggregates, REST + `/agents` UI, FinOps feed | cli-db-reader, persistence, rest-api |

> Sub-map: `.specs/CAPABILITY-MAP-cli-metrics.md` details `cli-db-reader` → `cli-metrics` and the verified source inventory. `cli-metrics` feeds `ade-observability-finops` (external CLI usage data) without blocking it.

## Legend

- `domain-model` is the only layer with no upstream dependency.
- `persistence` is the only infrastructure layer allowed to know about disk paths and SQLite schema details.
- `rest-api` owns HTTP contracts and SSE semantics; all other modules consume it.
- `cli` and `mcp` are presentation/automation layers over `rest-api`.
- `ai-chat`, `cloud`, `workflow-automation`, `integrations` are vertical slices over `rest-api` + `persistence`.
- `frontend` and `skill` are user/agent-facing surfaces.

## Build order summary

```
domain-model
  → persistence
    → rest-api
      → cli
        → mcp
        → ai-chat
        → cloud
        → workflow-automation
      → skill
      → frontend
      → integrations
      → harness-workspace-isolation
        → harness-context-memory
        → harness-security-permission-gateway
        → harness-verification-loop
          → ade-multi-agent-orchestration
            → ade-living-specs
            → ade-observability-finops
            → ade-cockpit-hitl
  → cli-db-reader
    → cli-metrics (feeds ade-observability-finops)
```

## .NET project mapping

| Module | Project | Target |
|---|---|---|
| domain-model | `src/Taskboard.Domain.Shared` + `src/Taskboard.Domain` | `net10.0` |
| application | `src/Taskboard.Application.Contracts` + `src/Taskboard.Application` | `net10.0` |
| persistence | `src/Taskboard.EntityFrameworkCore` | `net10.0` |
| rest-api | `src/Taskboard.Server` | `net10.0` |
| cli | `src/Taskboard.Cli` | `net10.0` |
| mcp | `src/Taskboard.Mcp` | `net10.0` |
| ai-chat | `src/Taskboard.AiChat` | `net10.0` |
| cloud | `src/Taskboard.Cloud` | `net10.0` |
| workflow-automation | `src/Taskboard.Workflow` | `net10.0` |
| skill | `skills/manage-taskboard/` | Markdown |
| frontend | `src/Taskboard.Blazor` + `src/Taskboard.Maui` | `net10.0` |
| integrations | `src/Taskboard.Integrations` | `net10.0` |
| harness-workspace-isolation | `src/Taskboard.Integrations` + `Taskboard.Domain` | `net10.0` |
| harness-context-memory | `src/Taskboard.Integrations` + `Taskboard.Application.Contracts` | `net10.0` |
| harness-security-permission-gateway | `src/Taskboard.Integrations` + `Taskboard.Domain.Shared` | `net10.0` |
| harness-verification-loop | `src/Taskboard.Integrations` + `Taskboard.Application.Contracts` | `net10.0` |
| ade-multi-agent-orchestration | `src/Taskboard.Application` + `Taskboard.Domain` | `net10.0` |
| ade-living-specs | `src/Taskboard.Application` + `Taskboard.Blazor` | `net10.0` |
| ade-observability-finops | `src/Taskboard.Integrations` + `Taskboard.Blazor` | `net10.0` |
| ade-cockpit-hitl | `src/Taskboard.Blazor` + `src/Taskboard.Server` | `net10.0` |
| cli-db-reader | `src/Taskboard.Domain.Shared` + `src/Taskboard.Integrations` | `net10.0` |
| cli-metrics | `src/Taskboard.Domain` + `src/Taskboard.EntityFrameworkCore` + `src/Taskboard.Application` + `src/Taskboard.Server` + `src/Taskboard.Blazor` | `net10.0` |

## Test project mapping

| Module | Test project |
|---|---|
| Domain | `tests/Taskboard.Tests.Unit` |
| Application | `tests/Taskboard.Tests.Unit` |
| REST API | `tests/Taskboard.Tests.Integration` |
| CLI | `tests/Taskboard.Tests.Integration` / `tests/Taskboard.Tests.Unit` |
| MCP | `tests/Taskboard.Tests.Integration` |

## Acceptance gates

- [x] `Taskboard.Domain` compiles with `TreatWarningsAsErrors`.
- [x] `Taskboard.EntityFrameworkCore` migration runs and seeds the `local` project *(idempotent seed added in `Program.cs` after `MigrateAsync`)*.
- [x] `Taskboard.Server` starts and exposes `/health` *(verified by `tests/Taskboard.Tests.Integration`)*.
- [x] `taskctl project list --json` returns the `local` project.
- [x] MCP `list_projects` tool returns the same JSON.
