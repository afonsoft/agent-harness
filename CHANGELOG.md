# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Terminal mobile UX (epic E23): **focus mode** expands the shell to the full viewport — top bar, page title and description hidden, compact tab strip kept, floating restore control — and a **touch-only virtual keybar** (visible under `(pointer: coarse)`/`(hover: none)` in focus mode) provides Esc, Tab/Shift+Tab, arrows, Home/End/PgUp/PgDn, one-shot sticky Ctrl and a dedicated clipboard-paste button, all routed through the existing `TerminalHub.Input` path.
- ADE platform epics E6–E16: per-run Git worktree isolation, hierarchical context compilation + project memory, pre-dispatch security gateway (risk classification, path jail, secret scrubbing), deterministic verification loop (build/test/coverage with retry feedback), read-only CLI DB ingestion + persisted usage metrics, multi-agent DAG pipelines, living specs (`/specs` + drift report), FinOps metrics (`/finops`), and the HITL Cockpit (`/cockpit`) with live timeline, steer, approval gates, diff viewer and create-PR.
- Global repository selector in the sidebar (`harness.selectedRepo`) driving Board, Gantt, Workflow, Specs, Terminal and VS Code workdir.
- Sidebar external **Issues** link to the repository's GitHub issues.
- VS Code restart action (`POST /api/vscode/restart`) and PTY terminal improvements (TIOCSWINSZ resize, orphan reattach, sweeper).
- Agent orchestration slices E1-E4: GitHub Actions refinement, `AgentLog` SQLite persistence, JSON-RPC ACP adapter, and `.devin`/`.agent` harness. (commit `0759c81`)
- `install-cli.sh` for lightweight `taskctl` CLI installation to `/usr/local/bin`.
- Environment variable rename from `CODEX_*` to `TASKBOARD_*` for clarity.
- System configuration: appsettings, `TaskboardOptions`, non-static `TaskboardEnvironment`, response compression, rate limiting, health checks, and localization.
- Settings/skills UI with theme and agent toggles.

### Changed
- Repository renamed `afonsoft/taskboard-ai` → `afonsoft/agent-harness`; all docs, specs, installer and references updated (technical identifiers `Taskboard.*`, `taskctl`, `TASKBOARD_*` unchanged by design).
- `docs/architecture/` synced with the delivered ADE platform (rewritten `architecture.md`, regenerated archify `architecture.json`/`.html`).
- README (en + pt-br) synced with the ADE wave (cockpit, pipelines, repo selector, specs, FinOps).
- Refined `.github/workflows/dotnet.yml` with NuGet cache, concurrency, minimal permissions, and updated action versions.
- Replaced in-memory agent logs with SQLite-backed `AgentLog` storage via EF Core.
- Updated `afonsoft/skills` lock and synchronized skills into `.claude/skills`.
- Improved `KanbanBoard` performance with memoization, virtualization, and `ShouldRender` optimization.

### Fixed
- `SelectedRepositoryServiceTests` fixture ordering broken by the repository rename (alphabetical fallback changed).
- Resolved DI lifetime issue between singleton `AgentOrchestrationService` and scoped `IAgentLogRepository` using `IServiceScopeFactory`.
- Resolved CodeQL alerts from earlier PRs.
- Used native inputs in the login form for better accessibility.

### Security
- Added CodeQL workflow for C# and GitHub Actions analysis.
- Enabled `permissions` scoping in CI workflows.

[Unreleased]: https://github.com/afonsoft/agent-harness/commits/main
