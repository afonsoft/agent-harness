# AD-0001 — Spectre.Console.Cli as the `taskctl` CLI framework

## Status

Accepted (retroactive record — decision applied during the .NET 10 migration, 2026-09)

## Context

The `taskctl` CLI consumes the REST API and needs subcommands, JSON output and testable command wiring. The first implementation used `System.CommandLine` (commit `cc0d746` — "feat: implementa CLI taskctl com System.CommandLine"). During the migration to the .NET 10 harness the framework choice was re-evaluated; `SPEC-003-cli.md` records the resolved question: "Usar `System.CommandLine` ou `CommandLineParser`? (Resolvido: **Spectre.Console.Cli**)".

Options considered:

- `System.CommandLine` — Microsoft-adjacent, minimal, but long in flux (pre-release API churn) and no built-in rich console or test harness.
- `CommandLineParser` — stable but unmaintained, attribute-based model that does not fit nested subcommands well.
- `Spectre.Console.Cli` — command/type-safe wiring, rich console rendering, and a dedicated `Spectre.Console.Cli.Testing` package with `CommandAppTester` for in-process command tests.

## Decision

Use **Spectre.Console.Cli** (pinned via `Directory.Packages.props`, currently 0.55.0) for the `taskctl` CLI. The implementation was migrated from `System.CommandLine` in commit `1fca7e4` and stabilized by `9cc27fe` (0.55.0 + CancellationToken adaptation).

## Consequences

- Command smoke tests run in-process via `CommandAppTester` (`tests/Taskboard.Tests.Unit/Cli/CliSmokeTests.cs`) — no process spawn, no shell quoting.
- A repo-specific convention applies: every `CommandArgument` uses `<>` (required) or `[]` (optional) placeholders — bare names break Spectre's `StyleParser` (SPEC-003 §convenção crítica); a reflection guard test enforces it.
- Rich console output is available for table/progress rendering; JSON output is produced manually via `--json` flags rather than a framework feature.
- `System.CommandLine` API churn risk is eliminated; the trade-off is a third-party dependency for a core surface, mitigated by Central Package Management pinning.
