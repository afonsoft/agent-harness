# SPEC-20260917-cli-agents-terminal

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cli-agents-terminal` |
| Type | `Feature` |
| Stack | `.NET 10 / ASP.NET Core SignalR / Blazor WASM / xterm.js / Dockerfile` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260917-cli-agents-terminal` |
| Ticket | N/A |
| Status | `Done` |

## 1. User Story

**As a** Taskboard administrator,
**I want** a "CLI Agents" page showing which agent CLIs are installed/authenticated, and a web "Terminal" page that opens a real interactive bash shell (PTY) on the host/container,
**So that** I can run `claude login`, `codex login`, `opencode auth login`, `devin`/`agy` auth flows manually from the browser — which is the only way to authenticate CLIs inside the Docker deployment — while on the VPS host deployment the same pages give direct access to the host tools.

**Problem context:**

- The Docker image (`agent-harness:latest`) has no Node.js and no agent CLIs, so the `/settings` → Agent Skills install (`npx skills add`) fails inside the container.
- CLI authentication is interactive (OAuth browser flow, TUI prompts). It cannot be scripted; it needs a real terminal.
- In the container, CLI auth state lives in `HOME=/root` which is ephemeral — a recreate wipes every login.
- On the VPS the app runs directly on the host, where the CLIs are already installed — the same UI must work there without changes.
- SignalR already exists (`AgentLogHub`, `Program.cs:1089`); no terminal/PTY code exists yet.

## 2. Scope

**In scope:**

- Dockerfile: Node.js LTS + `npm i -g` for `@anthropic-ai/claude-code`, `@openai/codex`, `opencode-ai`; official install scripts for `devin` CLI and `agy` (Google Antigravity CLI); `ENV HOME=/data/home` so all CLI state persists in the existing `/data` volume.
- `IAgentCliStatusService` in `Taskboard.Integrations`: per-CLI detection of `{ installed, version, authStatus, configDir, loginCommand }` — filesystem + `--version` probe, no interactive calls.
- `GET /api/agent-clis` endpoint (cookie auth).
- `TerminalHub` (SignalR) at `/terminal-hub`: spawns `bash` in a PTY via `script -qfc`, streams input/output, supports resize, one session per user, 30-min idle timeout, `Taskboard:Terminal:Enabled` flag.
- `/agents` Blazor page: per-CLI table (installed/version/auth badges, config dir) + "Open terminal" button → `/terminal?cmd=<login command>`.
- `/terminal` Blazor page: xterm.js + SignalR; `?cmd=` pretypes the suggested command without executing it.
- Unit + integration tests; Dockerfile smoke via `docker build` + `docker run` checks.

**Out of scope:**

- Auto-install of CLIs on the host mode (the terminal covers it manually).
- Running agents/jobs/chat from the UI — this is provisioning + a shell, not an agent runner.
- Multiple concurrent terminals per user; terminal sharing/collaboration.
- Logging command/output content (audit = session metadata only — secrets pass through this terminal).
- File upload/download through the terminal.
- Windows host support for the PTY.

## 3. Technical Context

**Where the change happens:**

- `Dockerfile` — runtime stage: install Node.js LTS (NodeSource or official tarball; the VPS is `arm64` — use an arm64-capable method), `npm i -g` the three npm CLIs, run the devin/agy install scripts, `ENV HOME=/data/home`, ensure `bash` + `script` (bsdutils/util-linux) present.
- `Taskboard.Domain.Shared` — `AgentCliMap`: `AgentType`/cli id → `{ binary, configDir, credentialProbe, loginCommand, installHint }`.
- `Taskboard.Application.Contracts` — `AgentCliStatus.cs`, `IAgentCliStatusService.cs`.
- `Taskboard.Integrations` — `AgentCliStatusService` (PATH probe via `PathSearch`, `--version` parse, credential-file probe) and `Terminal/PtySession` (Process wrapper over `script -qfc "bash -l" /dev/null`, `TERM=xterm-256color`, stdin/stdout pump, resize via `stty cols rows`).
- `Taskboard.Server` — `Program.cs`: `GET /api/agent-clis`, `app.MapHub<TerminalHub>("/terminal-hub").RequireAuthorization()`; new `Hubs/TerminalHub.cs`; `Taskboard:Terminal:Enabled` flag checked in the hub.
- `Taskboard.Client` — `TaskboardClient.GetAgentClisAsync`; `wwwroot/js/terminal.js` JS interop wrapping xterm.js + SignalR client (`@microsoft/signalr` npm assets copied to wwwroot).
- `Taskboard.Blazor` — `Pages/Agents.razor`, `Pages/Terminal.razor`, nav entries.

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `Dockerfile` · `docs/installation.md` (env table)
- `src/Taskboard.Server/Hubs/AgentLogHub.cs` + `SignalRAgentLogBroadcaster` (hub + auth pattern)
- `src/Taskboard.Integrations/Agents/PathSearch.cs` (PATH lookup)
- `src/Taskboard.Domain.Shared/Agents/AgentType.cs` + `Mcp/AgentMcpConfigMap.cs` (map-table pattern)
- `src/Taskboard.Blazor/Components/Layout/NavMenu.razor` (nav entries)
- `src/Taskboard.Client/wwwroot/` (JS interop layout, boot.js)

**Files to create or modify:**

```text
Dockerfile                                                       # MOD — Node + CLIs + HOME
src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs                # NEW
src/Taskboard.Application.Contracts/Agents/AgentCliStatus.cs     # NEW
src/Taskboard.Application.Contracts/Agents/IAgentCliStatusService.cs # NEW
src/Taskboard.Integrations/Agents/AgentCliStatusService.cs       # NEW
src/Taskboard.Integrations/Terminal/PtySession.cs                # NEW
src/Taskboard.Server/Hubs/TerminalHub.cs                         # NEW
src/Taskboard.Server/Program.cs                                  # MOD — endpoint + hub + flag
src/Taskboard.Client/TaskboardClient.cs                          # MOD
src/Taskboard.Client/wwwroot/js/terminal.js                      # NEW
src/Taskboard.Client/wwwroot/lib/xterm/…                         # NEW — vendored xterm.js + fit addon + signalr.js
src/Taskboard.Blazor/Components/Pages/Agents.razor               # NEW
src/Taskboard.Blazor/Components/Pages/Terminal.razor             # NEW
src/Taskboard.Blazor/Components/Layout/NavMenu.razor             # MOD
tests/Taskboard.Tests.Unit/Integrations/AgentCliStatusServiceTests.cs # NEW
tests/Taskboard.Tests.Integration/AgentCliEndpointsTests.cs      # NEW
```

**Per-CLI map (verify exact paths/commands at implementation):**

| CLI | Binary | npm/install | Credential probe | Login command |
| --- | --- | --- | --- | --- |
| Claude | `claude` | `npm i -g @anthropic-ai/claude-code` | `~/.claude/.credentials.json` | `claude` (interactive) |
| Codex | `codex` | `npm i -g @openai/codex` | `~/.codex/auth.json` | `codex login` |
| OpenCode | `opencode` | `npm i -g opencode-ai` | `~/.local/share/opencode/auth.json` | `opencode auth login` |
| Devin | `devin` | official install script | `~/.config/devin/` credentials | `devin auth login` |
| Antigravity | `agy` | official install script | `~/.gemini/` credentials | `agy` (interactive) |

`[A DEFINIR]` exact credential-probe paths and install-script URLs for `devin` and `agy` — confirm against the installed binaries on the VPS during T1.

## 4. Requirements

### RF-001: Docker image tooling

- **Description:** Runtime stage gains Node.js LTS (arm64-compatible install), `bash`, `script`, `curl`, `ca-certificates`; `npm i -g` for claude/codex/opencode; official scripts for `devin` and `agy`. Versions pinned to a minimum range (no bare `latest` floating into broken majors).
- **Input → Output:** `docker build` → image where `node`, `npx`, `claude`, `codex`, `opencode`, `devin`, `agy` resolve on PATH.

### RF-002: Persistent HOME in container

- **Description:** `ENV HOME=/data/home` in the runtime stage (dir created, owned by the runtime user) so `~/.claude`, `~/.codex`, `~/.config/devin`, `~/.config/opencode`, `~/.gemini`, `~/.local/share/opencode` all land inside the existing `/data` volume mount. Host mode is unaffected (uses the real `$HOME`).

### RF-003: CLI status detection

- **Description:** `AgentCliStatusService` resolves per CLI: `installed` (binary on PATH via `PathSearch`), `version` (`<binary> --version`, 5 s timeout, output truncated), `authStatus` = `Authenticated | NotAuthenticated | Unknown` via credential-file probe under `homeDir` (`Taskboard:HomeDir` override honored for tests), `configDir`, `loginCommand`, `installHint`. Never blocks longer than the probe timeouts; never runs login commands.
- **Input → Output:** PATH + home dir → `IReadOnlyList<AgentCliStatus>`.

### RF-004: Endpoint

- **Description:** `GET /api/agent-clis` → `200` `AgentCliStatus[]`; cookie auth same as `/api/settings`; 401 unauthenticated.

### RF-005: TerminalHub (PTY over SignalR)

- **Description:** Hub methods `Resize(int cols, int rows)` and `Input(string data)`; on connect it spawns `script -qfc "bash -l" /dev/null` with `TERM=xterm-256color`, `HOME` = effective home dir, streams stdout to the caller as `output` messages; on disconnect or 30-min idle it kills the process. One active session per authenticated user — a second connect kills the first. When `Taskboard:Terminal:Enabled` is `false`, connect throws `HubException("Terminal disabled")`.
- **Rules:** PTY via `script` (no new NuGet PTY dependency); output chunked (≤64 KiB/message); process is always reaped on disconnect; spawn failures surface as a hub error, not a crash.

### RF-006: `/terminal` page

- **Description:** Full-width xterm.js canvas wired to `TerminalHub` through vendored JS interop (`terminal.js` + `lib/xterm`). `?cmd=<text>` types the text into the terminal without pressing Enter. Disconnect button + auto-reconnect banner on hub drop. Renders nothing until hub connects; shows inline error on failure.

### RF-007: `/agents` page

- **Description:** Nav entry "Agents" + page listing the five CLIs: name, installed badge + version, auth badge (`Authenticated`/`Not authenticated`/`Unknown`), config dir, login command (monospace), and an "Open terminal" button linking to `/terminal?cmd=<loginCommand>`. "Refresh" button re-queries `GET /api/agent-clis`.

### RF-008: Security and audit

- **Description:** Hub and both endpoints require the existing cookie authorization. Audit log = `LogInformation` on session start/end with user + duration — never command/output content. `Taskboard:Terminal:Enabled` catalog key (default `true`, `Editable`, `RequiresRestart: false`, env alias `TASKBOARD_TERMINAL_ENABLED`).

### RF-009: Failure isolation

- **Description:** A failing `--version` probe or missing binary yields `installed:false`/`version:null` — never a 500. Terminal spawn failure → hub error to that client only. Hub exceptions never leak stack traces or absolute paths to the client.

**Business rules / invariants:**

- The terminal is a plain user shell — no privilege elevation, no extra env vars injected.
- Credential probes read file existence only, never file contents.
- Host mode and container mode share the same code path; only `HOME` differs.

## 5. API Contract

**Endpoint:** `GET /api/agent-clis` · `Hub /terminal-hub`
**Auth:** Cookie session (same as `/api/settings`)

**Response — GET /api/agent-clis:**

```json
[
  {
    "agent": "Claude",
    "binary": "claude",
    "installed": true,
    "version": "2.1.274",
    "authStatus": "Authenticated",
    "configDir": "~/.claude",
    "loginCommand": "claude",
    "installHint": "npm i -g @anthropic-ai/claude-code"
  }
]
```

**Hub contract:** client → `input(string)`, `resize(int,int)`; server → `output(string)`, `closed(reason)`. Errors → `HubException` with a sanitized message.

**Expected errors:** `401` unauthenticated; hub connect → `Terminal disabled` when flag off; second session per user → first closed with reason `replaced`.

## 6. Acceptance Criteria

- [ ] **Given** the image is built **when** `docker run --rm agent-harness:latest bash -lc "node --version && claude --version && codex --version && opencode --version && devin --version && agy --version"` **then** all resolve.
- [ ] **Given** `HOME=/data/home` **when** `claude login` completes inside the container **then** credentials persist in the `/data` volume across `docker rm` + recreate.
- [ ] **Given** a CLI missing on PATH **when** `/agents` loads **then** its row shows `not installed` + `installHint`, no error.
- [ ] **Given** an authenticated session **when** `/terminal` opens **then** `echo`/`ls` work and `claude login` renders its interactive prompt.
- [ ] **Given** `?cmd=codex login` **when** the terminal connects **then** the text is typed but not executed.
- [ ] **Given** the terminal flag disabled **when** a client connects **then** `Terminal disabled` and no process spawns.
- [ ] **Given** a second connect from the same user **when** the first session is open **then** the first is closed (`replaced`) and reaped.
- [ ] **Given** 30 min without input **when** the session idles **then** the process is killed and `closed("idle-timeout")` is sent.
- [ ] **Given** no auth cookie **when** `GET /api/agent-clis` or hub connect **then** `401`/hub rejection.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `--version` hangs | >5 s | probe killed, `version: null` |
| Credential file corrupt | present but invalid | still `Authenticated` (existence-only probe) |
| Hub disconnect mid-output | network drop | process killed, no orphan `bash` |
| `script` binary missing | minimal image | hub error `terminal unavailable`, page shows inline error |
| Host mode | no `HOME` override | probes + terminal use real `$HOME` |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** confirm `devin`/`agy` install-script URLs and credential-probe paths against the VPS binaries; confirm `script` exists in the aspnet base image; read section-3 files.
- [ ] **T2 — Domain.Shared + Contracts:** `AgentCliMap`, `AgentCliStatus`, `IAgentCliStatusService`.
- [ ] **T3 — Integrations:** `AgentCliStatusService` (probes, timeouts) + `PtySession` — TDD with fake home/PATH.
- [ ] **T4 — Server:** `TerminalHub`, `GET /api/agent-clis`, `Taskboard:Terminal:Enabled` catalog key, DI.
- [ ] **T5 — Client/UI:** vendor xterm.js + signalr.js, `terminal.js` interop, `/terminal` and `/agents` pages, nav entries.
- [ ] **T6 — Dockerfile:** Node LTS + CLIs + `HOME=/data/home`; `docker build` smoke.
- [ ] **T7 — Tests:** unit (status probes, flag off, one-session rule, idle timeout with fake clock) + integration (endpoint auth, hub auth rejection).
- [ ] **T8 — Validation:** `dotnet build -c Release` + `dotnet test` + `docker build` + manual smoke `/agents` + `/terminal` in both modes.
- [ ] **T9 — Done + PR:** `Status = Done`, PR on `feature/devin-20260917-cli-agents-terminal`.

**7.1 Validation strategy:** unit tests for detection/hub rules, integration tests for auth; `docker build` + `docker run` version probes as the image AC; `TreatWarningsAsErrors`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`develop`; branch `feature/devin-20260917-cli-agents-terminal`.
- **Workflows:** do not modify `.github/workflows/`.
- **Security:** terminal behind cookie auth + feature flag; audit logs metadata only — never typed content or output (OAuth tokens transit here); no file-content reads for credential probes.
- **Scope:** provisioning + shell access only — no agent execution from the UI.
- **Architecture:** probes/PTY in `Integrations`; hub thin; UI via `TaskboardClient` + JS interop.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) covered by passing tests or documented manual checks.
- [ ] Edge cases handled.
- [ ] `dotnet build` clean (TreatWarningsAsErrors), `dotnet test` green, `docker build` green.
- [ ] Guardrails respected; no secrets in logs.
- [ ] `docs/` updated (api endpoints, env vars, agent/terminal pages).
- [ ] Container redeployed and `/agents` + `/terminal` smoke-tested.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` exact install-script URL + credential path for `devin` CLI and `agy` — verify on the VPS during T1 (both binaries already exist there).
- `[A DEFINIR]` opencode npm package name (`opencode-ai` vs scoped beta) — pin whatever resolves at build time.
