# SPEC-20260917-skills-installer

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `skills-installer` |
| Type | `Feature` |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Blazor WASM` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260917-skills-installer` |
| Ticket | N/A |
| Status | `Done` |

## 1. User Story

**As a** Taskboard administrator,
**I want** an "Agent Skills" section in `/settings` that installs the configured skills collection (default `afonsoft/skills`) globally — via `npx skills add` plus the repo's own `install.sh` — reports whether it is installed, and syncs on demand,
**So that** every agent CLI on the machine is provisioned with skills, hooks and `AGENTS.md` in one click, without opening a terminal.

**Problem context:**

- `SPEC-20260915-skills-repo-sync` syncs `skills/*` from the repo into enabled CLI directories via git — but it never runs `npx skills add` (universal `~/.agents/skills` + per-agent global dirs) and never runs the repo's `install.sh` (which also installs hooks and `AGENTS.md`).
- `/settings` already exposes `Taskboard:Skills:Repository` and the sync endpoints exist (`GET /api/skills/sync/status`, `POST /api/skills/sync`), but there is no install action, no "is it installed?" verification, and no sync button in the UI.
- The `skills` CLI (`npx skills add <repo>`) supports `-g` (global), `--all` (all skills × all agents, no prompts) and `--copy` (copy files instead of symlinks).

## 2. Scope

**In scope:**

- New **"Agent Skills"** section in `Settings.razor` (below *Integrations*): install state badge, **Install**, **Verify** and **Sync** buttons, last-run summary.
- `ISkillsInstallerService` in `Taskboard.Integrations` orchestrating:
  1. `npx skills add {repo} -g --all --copy`
  2. refresh of the existing repo cache `{DataDir}/skills-cache` (reuse `GitRunner`/sync cache update)
  3. `bash {cache}/install.sh --all` when the file exists (skipped with warning otherwise)
- Install-state detection by filesystem scan + persisted manifest `{DataDir}/skills-install.json`.
- Endpoints `GET /api/skills/install/status`, `POST /api/skills/install`, `POST /api/skills/install/verify` (cookie-authenticated, same as `/api/settings`).
- Prerequisite detection: `npx`, `git`, `bash` on PATH.
- Sync button wired to the existing `POST /api/skills/sync` + `GET /api/skills/sync/status`.
- Unit + integration tests.

**Out of scope:**

- Per-skill or per-agent selection (`--all` is fixed).
- `npx skills update`, `npx skills remove`, `npx skills list` parsing.
- Windows support for `install.sh` (requires bash; step is skipped with a warning).
- Changes to the git-based sync engine (owned by `SPEC-20260915-skills-repo-sync`).
- Installing Node.js itself.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Integrations` — new `Skills/SkillsInstallerService` (process orchestration + manifest), `Skills/NpxRunner.cs` (Process wrapper, sanitized output, same pattern as `GitRunner`).
- `Taskboard.Application.Contracts` — `Skills/ISkillsInstallerService.cs`, `Skills/SkillsInstallStatus.cs`, `Skills/SkillsInstallStep.cs`.
- `Taskboard.Server` — `Program.cs`: DI registration + three endpoints under the `api` group.
- `Taskboard.Blazor` — `Settings.razor` new section; `Taskboard.Client` — new client methods.
- Repository resolution reuses `RuntimeConfigurationService` key `Taskboard:Skills:Repository` (`owner/repo` shorthand or absolute URL — both accepted by `npx skills add`).

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `.specs/SPEC-20260915-skills-repo-sync.md`
- `src/Taskboard.Integrations/Skills/SkillsSyncService.cs` (cache update, manifest pattern)
- `src/Taskboard.Integrations/Skills/GitRunner.cs` (Process wrapper, token-safe output)
- `src/Taskboard.Domain.Shared/Skills/AgentSkillDirectoryMap.cs`
- `src/Taskboard.Integrations/Agents/PathSearch.cs` (PATH lookup)
- `src/Taskboard.Server/Program.cs` (endpoint + DI patterns, `api` group)
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Client/TaskboardClient.cs`

**Files to create or modify:**

```text
src/Taskboard.Application.Contracts/Skills/ISkillsInstallerService.cs   # NEW
src/Taskboard.Application.Contracts/Skills/SkillsInstallStatus.cs       # NEW
src/Taskboard.Application.Contracts/Skills/SkillsInstallStep.cs         # NEW
src/Taskboard.Integrations/Skills/SkillsInstallerService.cs             # NEW
src/Taskboard.Integrations/Skills/NpxRunner.cs                          # NEW
src/Taskboard.Server/Program.cs                                         # MOD — DI + endpoints
src/Taskboard.Client/TaskboardClient.cs                                 # MOD — install/status/verify/sync calls
src/Taskboard.Blazor/Components/Pages/Settings.razor                    # MOD — Agent Skills section
src/Taskboard.Client/wwwroot/css/site.css                               # MOD — section styles if needed
tests/Taskboard.Tests.Unit/Skills/SkillsInstallerServiceTests.cs        # NEW
tests/Taskboard.Tests.Integration/SkillsInstallEndpointsTests.cs        # NEW
```

## 4. Requirements

### RF-001: Prerequisite detection

- **Description:** Detect `npx`, `git` and `bash` on PATH via `PathSearch.FindExecutable`. Status payload exposes `prerequisites: { npx, git, bash }` booleans; `POST /api/skills/install` fails fast with `PrerequisiteMissing` when `npx` or `git` is absent (missing `bash` only skips the install.sh step).
- **Input → Output:** PATH → per-tool availability map.

### RF-002: Install via `npx skills add`

- **Description:** Run `npx skills add {repo} -g --all --copy` with the repo taken from `Taskboard:Skills:Repository` (default `afonsoft/skills`). Process arguments are passed as an argument array (no shell interpolation); stdout/stderr are captured, truncated and sanitized (no tokens, no absolute home paths in user-facing messages).
- **Rules:** timeout 300s default; non-zero exit → `Failed` with sanitized stderr tail; concurrent installs coalesce (`SemaphoreSlim`, second request returns in-flight status).
- **Input → Output:** configured repo → step result `{ name: "npx-add", state, exitCode, durationMs, message }`.

### RF-003: Run repo `install.sh`

- **Description:** After the npx step, refresh the repo cache at `{DataDir}/skills-cache` using the same clone/fetch logic as `SkillsSyncService`. If `{cache}/install.sh` exists, run `bash install.sh --all` (cwd = cache dir, timeout 300s). If absent, record step as `Skipped` with message "no install.sh in repository".
- **Rules:** cache-refresh failure does not abort the whole install — npx result stands; step marked `Failed` independently.

### RF-004: Install manifest

- **Description:** Persist `{DataDir}/skills-install.json` after each run: `{ repository, installedAtUtc, durationMs, npxStep, installShStep, skillCount }`. Corrupt/missing manifest → treated as never installed.
- **Rules:** manifest never stores env vars, tokens or command output.

### RF-005: Installed-state verification (Verify)

- **Description:** `POST /api/skills/install/verify` rescans the global skills directories (`AgentSkillDirectoryMap` for all `AgentType` values + `~/.agents/skills`) counting directories with a valid `SKILL.md`; updates the manifest `skillCount` and returns the fresh status. `installed = skillCount > 0` in at least one global location.
- **Input → Output:** filesystem scan → `{ installed, skillCount, locations: [{ path, count }] }`.

### RF-006: Endpoints

- **Description:**
  - `GET /api/skills/install/status` → `200` `{ state, installed, skillCount, lastRunUtc, repository, prerequisites, steps[] }` — cheap, reads manifest + PATH (no process spawn).
  - `POST /api/skills/install` → `202` + current status; runs RF-002→RF-004 in background.
  - `POST /api/skills/install/verify` → `200` fresh status (RF-005).
- **Rules:** cookie auth same as `/api/settings`; no token in payloads.

### RF-007: Settings UI section

- **Description:** "Agent Skills" section in `/settings` showing: state badge (`Installed`/`Not installed`/`Failed`/`Running`), skill count, repo name, per-step results, prerequisite warnings; buttons **Install** (disabled while running or missing prereq, with tooltip), **Verify**, **Sync** (calls existing `POST /api/skills/sync`, shows `state`/`lastRunUtc` from `GET /api/skills/sync/status`).
- **Rules:** poll status while `state == Running`; toasts on completion; errors in `alert-danger` inline + toast.

### RF-008: Failure isolation

- **Description:** Install/verify never throw past the endpoint boundary; failures surface in the status payload (`state: "Failed"`, sanitized `error`).

**Business rules / invariants:**

- Install is additive; it never deletes skills, hooks or config the user already has.
- The repo cache (`{DataDir}/skills-cache`) is the only place `git` mutates.
- No token, env var or full stderr in logs/manifests/payloads.

## 5. API Contract

**Endpoint:** `GET /api/skills/install/status` · `POST /api/skills/install` · `POST /api/skills/install/verify`
**Auth:** Cookie session (same as `/api/settings`)

**Response (status):**

```json
{
  "state": "Succeeded",
  "installed": true,
  "skillCount": 23,
  "lastRunUtc": "2026-09-17T12:00:00Z",
  "repository": "afonsoft/skills",
  "prerequisites": { "npx": true, "git": true, "bash": true },
  "steps": [
    { "name": "npx-add", "state": "Succeeded", "exitCode": 0, "durationMs": 4210, "message": null },
    { "name": "install-sh", "state": "Succeeded", "exitCode": 0, "durationMs": 1830, "message": null }
  ],
  "error": null
}
```

**Expected errors:** `401` unauthenticated; `POST` while running → `202` in-flight status; missing prereq → `200` status with `state: "Failed"`, `error: "PrerequisiteMissing: npx"`.

## 6. Acceptance Criteria

- [x] **Given** no skills installed and all prerequisites present **when** the admin clicks Install **then** `npx skills add afonsoft/skills -g --all --copy` runs, `install.sh --all` runs from the cache, the manifest is written and the badge shows `Installed`.
- [x] **Given** `npx` missing from PATH **when** status is loaded **then** the section shows a prerequisite warning and Install is disabled.
- [x] **Given** the repo has no `install.sh` **when** install runs **then** `install-sh` step is `Skipped` and overall state is still `Succeeded` (if npx succeeded).
- [x] **Given** skills were deleted manually **when** Verify is clicked **then** status flips to `Not installed` and `skillCount` becomes 0.
- [x] **Given** an install in flight **when** a second `POST /api/skills/install` arrives **then** it returns `202` with the in-flight status and no second process is spawned.
- [x] **Given** Sync clicked **when** `POST /api/skills/sync` completes **then** the section shows the sync `state` and per-agent counts.
- [x] **Given** `Taskboard:Skills:Repository` overridden **when** install runs **then** npx and the cache use the configured repo.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `bash` missing | install runs | `install-sh` = `Skipped`, warning, overall per npx result |
| npx exit ≠ 0 | network failure | `Failed`, sanitized message, no partial manifest fields |
| Cache dir corrupt | git fetch fails | `install-sh` = `Failed`; npx result preserved |
| Manifest corrupt | invalid JSON | treated as never installed; no exception |
| Windows host | no bash | same as `bash` missing |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read section-3 files; confirm `PathSearch`, `GitRunner`, `AgentSkillDirectoryMap` signatures and the `api` group auth.
- [x] **T2 — Contracts:** `ISkillsInstallerService`, `SkillsInstallStatus`, `SkillsInstallStep`.
- [x] **T3 — Integrations:** `NpxRunner`, `SkillsInstallerService` (steps, manifest, semaphore, prereqs).
- [x] **T4 — Server:** DI + 3 endpoints.
- [x] **T5 — Client/UI:** `TaskboardClient` methods + `Settings.razor` "Agent Skills" section.
- [x] **T6 — Tests:** unit (prereq detection, manifest reconcile, arg building, verify scan) + integration (endpoints auth, coalescing, fake process runner).
- [x] **T7 — Validation:** `dotnet build -c Release` + `dotnet test`; manual smoke on `/settings`.
- [x] **T8 — Done + PR:** `Status = Done`, PR on `feature/devin-20260917-skills-installer`.

**7.1 Validation strategy:** .NET — unit tests for business rules, integration tests for endpoints; ≥80% coverage on new code; build with `TreatWarningsAsErrors`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`develop`; branch `feature/devin-20260917-skills-installer`.
- **Workflows:** do not modify `.github/workflows/`.
- **Security:** no tokens/env/stderr with secrets in logs, manifests or payloads; process args passed as arrays (no shell).
- **Scope:** additive install only — no removal, no per-skill selection.
- **Architecture:** orchestration in `Integrations`; endpoints thin; no business logic in Razor.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests; ≥80% coverage on new code.
- [x] Edge cases handled.
- [x] `dotnet build` clean (TreatWarningsAsErrors) and `dotnet test` green.
- [x] Guardrails respected; logs contain no tokens.
- [x] `docs/` updated (settings/skills docs) if user-visible.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` exact `npx skills` minimum version — implementation should record `npx skills --version` output in the manifest when available.
- OpenHands global skills path is a fallback guess (`~/.openhands/skills`); verify scan covers whatever `AgentSkillDirectoryMap` returns.
