# AD-0002 — Local-first persistence with SQLite (single file, no external services)

## Status

Accepted (retroactive record — foundational decision of the product, reaffirmed in the .NET 10 rewrite)

## Context

Harness is a local-first, AI-native workbench: a developer runs it on their own machine (or a single VM) and it must work offline against local clones. The state to persist includes conversations, chat runs/events, delegation tasks, mailbox messages, push subscriptions, agent runs, pipeline state, FinOps metrics and configuration overrides. Deployment targets are developer workstations and a systemd user service — no database administrator, no container orchestration.

Options considered:

- PostgreSQL / SQL Server — operationally heavy for a local-first tool; requires a running server and credentials management.
- LiteDB / file-based JSON — loses relational integrity, LINQ translation and migration tooling.
- SQLite + EF Core — single file, embedded, mature EF Core provider, zero external dependency.

## Decision

Persist **all state in a single SQLite database** (`harness.sqlite`) under the install home `~/.agent-harness/data`, accessed through **EF Core** in the `Taskboard.EntityFrameworkCore` project. The only required outbound dependency is the GitHub API (Octokit); everything else degrades gracefully when offline.

## Consequences

- Zero-setup deployment: backing up the product is copying one directory; the `taskctl backup`/`restore` commands operate on it.
- Write concurrency is bounded by SQLite's single-writer model — acceptable for a single-user local tool (the hosted dispatcher caps concurrency at 4 runs); not suitable for multi-tenant hosting without a provider swap.
- EF Core keeps the provider swappable (the `Database__Provider` pattern exists for other products in the family), but no other provider is shipped or tested here.
- High-volume telemetry (CLI metrics) uses watermark-based incremental ingestion with daily aggregates to keep the single file small (90-day raw retention).
