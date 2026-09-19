# CAPABILITY-MAP — cli-metrics

Capability map for the **CLI SQLite metrics** initiative: reading the local
SQLite databases that managed agent CLIs keep on the host to feed Harness
metrics, FinOps and diagnostics.

Approved scope (design rounds, 2026-09-19):

- Sources: managed CLIs from `AgentCliMap` only (13 kinds); only those with a
  detected SQLite database ship an extractor in v1.
- Access: hybrid — incremental ingestion into the Harness DB plus on-demand
  read-only detail reads.
- Granularity: aggregates + session metadata; message/prompt content is never
  extracted.
- Surfaces: REST API, `/agents` page section, and the FinOps dashboard
  (SPEC-20260919-ade-observability-finops).
- Sync: background service with configurable interval + manual trigger.

## Module Matrix

| Build order | Module id | Responsibility | Depends on |
|---|---|---|---|
| 1 | `cli-db-reader` | DB discovery per `AgentCliKind`, safe read-only/WAL-copy access, schema fingerprint + drift detection, whitelisted per-CLI extraction into normalized records | `domain-model` (`AgentCliMap`) |
| 2 | `cli-metrics` | Incremental ingestion (watermark), persistence entities + migration, aggregate queries, REST endpoints, `/agents` UI section, FinOps feed | `cli-db-reader`, `persistence`, `rest-api` |

Build order: `cli-db-reader` → `cli-metrics`

## Module boundary contract

`cli-db-reader` owns everything that knows about **external file paths, SQLite
connection flags, WAL handling and vendor schemas**. `cli-metrics` never opens
an external database and never references a vendor table name — it consumes
`ICliDbExtractor` results (`CliSessionRecord` / `CliUsageRecord`) and owns
persistence, scheduling and HTTP contracts.

Upstream consumers (`ade-observability-finops`, `ade-cockpit-hitl`) depend on
the contracts owned by `cli-metrics`, not on the reader.

## Verified source inventory (host scan, 2026-09-19)

| AgentCliKind | Database path(s) | Mode | v1 extractor |
|---|---|---|---|
| Codex | `~/.codex/{goals,memories,thread_history,queue,logs}_N.sqlite` | WAL, multi-file | Yes |
| OpenCode | `~/.local/share/opencode/opencode.db` | single file | Yes |
| Devin | `~/.local/share/devin/cli/sessions.db` | single file | Yes |
| Antigravity | `~/.gemini/antigravity-cli/conversations/*.db` + `conversation_summaries.db` | glob, multi-file | Yes |
| Cline | `~/.cline/data/db/*.db` | multi-file dir | Yes |
| Claude | `~/.claude/context-mode/sessions/*.db` | plugin DBs | Experimental |
| Kimi, Grok, Aider, Continue, Copilot, Qwen, Kiro | none detected | — | `NoDatabase` until discovered |

> The inventory is a verified baseline, not a contract — new DBs are added to
> `CliDatabaseMap` as they are confirmed on real installations.
