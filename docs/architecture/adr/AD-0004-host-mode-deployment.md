# AD-0004 — Host-mode deployment (systemd user service); sandbox deferred

## Status

Accepted (owner decision recorded 2026-10-07 in `.claude/memory/gap-analysis-20261007.md`; retroactive record)

## Context

The server hosts AI agent CLIs that execute arbitrary commands (shell tools, file edits, git operations) inside run worktrees. A sandboxed runtime (per-run container/VM) would harden isolation, but the product's deployment reality is a developer's own machine or a single VPS, where agent CLIs must authenticate with the user's own credentials (OAuth tokens, `GITHUB_TOKEN`) that are painful to proxy into containers, and where per-run container images would slow the "install CLI from the UI" flow.

Options considered:

- Per-run container/VM sandbox — strongest isolation; heavy setup, breaks host-authenticated CLIs, complicates PTY/code-server/browser tooling.
- Host execution with layered soft controls — no setup friction, CLIs run where they are installed and authenticated.

## Decision

Run the server and all agent CLIs **directly on the host**, deployed as the systemd user service `harness-server.service` (home `~/.agent-harness`, database `harness.sqlite`, wrappers `harness-server`/`harness-mcp`). The security boundary today is layered at the application level: pre-dispatch command classification (Safe/WorkspaceWrite/Dangerous, fail-closed), path-jail + symlink-escape enforcement anchored to the run worktree, risk-tiered approval policies (`auto` = LOW auto-approve + MED notice + HIGH ask), and secret scrubbing on logged output. **A per-run sandbox (VM/container) is explicitly deferred to a later phase** — it is a planned evolution, not a rejected direction.

## Consequences

- Zero-friction deployment and CLI authentication; the install/migration runbook is a single script (`install.sh --migrate`).
- The blast radius of a malicious or buggy agent command is the host user account — mitigated, not eliminated, by the gateway + approvals + worktree jail; users should run the service under a dedicated account on shared machines.
- The deferred sandbox work must re-use the existing boundaries (worktree root as jail anchor, gateway classification as policy input) so the runtime can be swapped without redesigning the security model.
- Documentation, threat model and release notes must keep stating that sandboxing is host-mode + soft controls today.

## Operations: agent process isolation (SPEC-20261020)

Agent/PTY children spawn inside their own transient cgroup scopes via `systemd-run --user --scope --collect` (`IProcessIsolator` → `SystemdScopeProcessIsolator`); a child OOM kill stays inside its scope and no longer tears down `harness-server.service`. Non-Linux hosts, missing `systemd-run`, or a missing user manager (`XDG_RUNTIME_DIR/systemd`) degrade to direct spawn — isolation never breaks a session.

Required unit properties (already applied on the production unit):

```ini
OOMPolicy=continue   # don't stop the whole unit when a child scope is OOM-killed
MemoryLow=1.5G       # protected floor for the server itself
MemoryHigh=4.5G      # reclaim throttle before MemoryMax
MemoryMax=6G         # hard cap for the service cgroup
RestartSec=2s
Environment=MEMORY_PRESSURE_WATCH=%t/app.slice/harness-server.service/memory.pressure
```

`MEMORY_PRESSURE_WATCH` points the PSI gate at the service's `memory.pressure` file (auto-resolved from `/proc/self/cgroup` when unset). Tunables:

| Key | Default | Effect |
|---|---|---|
| `Harness:ProcessIsolation:Enabled` | `true` | master switch — `false` = direct spawn |
| `Harness:ProcessIsolation:MemoryMax` | `2G` | hard cap per agent scope |
| `Harness:ProcessIsolation:MemoryHigh` | `1.5G` | throttle threshold per scope |
| `Harness:ProcessIsolation:OomScoreAdjust` | `500` | children die before the server |
| `Harness:MemoryPressure:SomeAvg60Max` | `30.0` | PSI `some avg60` threshold — above it new terminal sessions are refused and delegation dispatch pauses |
| `Harness:MemoryPressure:PressureFile` | auto | explicit `memory.pressure` path override |

Ops surface: `GET /api/diagnostics/memory` returns the PSI sample, threshold, and `memory.current|high|max|events` counters.
