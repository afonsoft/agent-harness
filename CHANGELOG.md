# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Agent orchestration slices E1-E4: GitHub Actions refinement, `AgentLog` SQLite persistence, JSON-RPC ACP adapter, and `.devin`/`.agent` harness. (commit `0759c81`)
- `install-cli.sh` for lightweight `taskctl` CLI installation to `/usr/local/bin`.
- Environment variable rename from `CODEX_*` to `TASKBOARD_*` for clarity.
- System configuration: appsettings, `TaskboardOptions`, non-static `TaskboardEnvironment`, response compression, rate limiting, health checks, and localization.
- Settings/skills UI with theme and agent toggles.

### Changed
- Refined `.github/workflows/dotnet.yml` with NuGet cache, concurrency, minimal permissions, and updated action versions.
- Replaced in-memory agent logs with SQLite-backed `AgentLog` storage via EF Core.
- Updated `afonsoft/skills` lock and synchronized skills into `.claude/skills`.
- Improved `KanbanBoard` performance with memoization, virtualization, and `ShouldRender` optimization.

### Fixed
- Resolved DI lifetime issue between singleton `AgentOrchestrationService` and scoped `IAgentLogRepository` using `IServiceScopeFactory`.
- Resolved CodeQL alerts from earlier PRs.
- Used native inputs in the login form for better accessibility.

### Security
- Added CodeQL workflow for C# and GitHub Actions analysis.
- Enabled `permissions` scoping in CI workflows.

[Unreleased]: https://github.com/afonsoft/agent-harness/commits/main
