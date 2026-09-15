# SPEC-20260915-skills-repo-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `skills-repo-sync` |
| Type | `Feature` |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Blazor Server / EF Core SQLite` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260915-skills-repo-sync` |
| Ticket | N/A |
| Status | `Done (merged via PR #78)` |

## 1. User Story

**As a** Taskboard administrator,
**I want** the application to clone a skills repository (default `afonsoft/skills`) and install every skill into the skills directories of the enabled CLIs — on application startup and whenever a CLI is enabled in Settings — detecting updates per skill,
**So that** every agent CLI I enable is immediately provisioned with the latest version of the skills collection without running `install.sh` manually.

**Problem context:**

- Today skills only reach the agent directories via `install.sh`, which copies a single skill (`manage-taskboard`) from `.claude/skills/` — there is no runtime sync.
- `skills-lock.json` pins ~23 skills from `afonsoft/skills` with SHA-256 hashes, but nothing consumes it at runtime.
- `AgentPreference.Enabled` is persisted and toggled in `/settings` (`Settings.razor` → `PUT /api/settings` → `SettingsService.SaveSettingsAsync`), but enabling an agent does not provision its skills directory.
- `SkillDiscoveryService` already scans `~/.claude/skills`, `~/.devin/skills`, `~/.cursor/skills`, `~/.opencode/skills` — installed skills appear automatically on `/skills`.

## 2. Scope

**In scope:**

- `ISkillsSyncService` that clones/updates the skills repository into a local cache and installs all `skills/*` directories containing a valid `SKILL.md` into the skills directories of each enabled CLI.
- Per-sil change detection via content hash; only changed skills are copied.
- Background startup sync (`IHostedService`) that never blocks or fails application boot.
- Immediate sync of a specific CLI when its toggle is switched ON via `PUT /api/settings`.
- New catalog key `Taskboard:Skills:Repository` (editable, no restart) selecting the source repository; default `afonsoft/skills`; env alias `TASKBOARD_SKILLS_REPO`.
- CLI → skills-directory mapping covering all known `AgentType` values with their config-dir variants.
- Status surface: `GET /api/skills/sync/status` + `POST /api/skills/sync` (manual trigger), authenticated.
- Unit + integration tests.

**Out of scope:**

- Removing skills from CLI directories (sync is strictly additive — user-managed/custom skills are never touched; see RF-006).
- Installing the CLIs themselves, hooks, or `AGENTS.md`/`CLAUDE.md` bootstrap files.
- Per-skill opt-in/opt-out selection in the UI.
- The searchable repository dropdown itself — covered by `SPEC-20260915-repo-search-combobox`.
- Multi-tenant or per-user skill sets (installation is server-wide, like `AgentPreference`).

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Integrations` — new `Skills/SkillsSyncService` (git + file copy), `Skills/SkillInstallMap` (CLI → directories table).
- `Taskboard.Application.Contracts` — new `Skills/ISkillsSyncService.cs`, `Skills/SkillsSyncStatus.cs`, `Skills/SkillsSyncResult.cs`.
- `Taskboard.Application` — `SettingsService.SaveSettingsAsync` triggers a background sync for newly enabled agents.
- `Taskboard.Server` — `SkillsSyncHostedService` (BackgroundService, startup sync) and two new endpoints under the `api` route group.
- `Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — new catalog entry `Taskboard:Skills:Repository`.
- Git is invoked via `System.Diagnostics.Process` (same pattern as `AgentDiscoveryService.TryGetVersion`) — no new NuGet dependency.

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `src/Taskboard.Application/Settings/SettingsService.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `src/Taskboard.Integrations/Agents/AgentDiscoveryService.cs` (Process pattern, `PathSearch`)
- `src/Taskboard.Integrations/Skills/SkillDiscoveryService.cs` (skill dir conventions)
- `src/Taskboard.Domain.Shared/Agents/AgentType.cs`
- `src/Taskboard.Server/Program.cs` (endpoint + DI registration patterns)
- `src/Taskboard.Server/TaskboardEnvironment.cs` (`GetDataDir`)
- `skills-lock.json`, `install.sh` (install semantics)

**Files to create or modify:**

```text
src/Taskboard.Domain.Shared/Skills/AgentSkillDirectoryMap.cs        # NEW — AgentType → skills dirs
src/Taskboard.Application.Contracts/Skills/ISkillsSyncService.cs    # NEW
src/Taskboard.Application.Contracts/Skills/SkillsSyncStatus.cs      # NEW
src/Taskboard.Application.Contracts/Skills/SkillsSyncResult.cs      # NEW
src/Taskboard.Integrations/Skills/SkillsSyncService.cs              # NEW
src/Taskboard.Integrations/Skills/GitRunner.cs                      # NEW — Process wrapper, token-safe
src/Taskboard.Server/Services/SkillsSyncHostedService.cs            # NEW
src/Taskboard.Server/Program.cs                                    # MOD — DI + endpoints
src/Taskboard.Application/Settings/SettingsService.cs              # MOD — trigger on enable
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs # MOD — new catalog key
tests/Taskboard.*Tests/...                                          # NEW tests
```

## 4. Requirements

### RF-001: CLI → skills directory map

- **Description:** The system must map every `AgentType` to one or more skills directories under the user profile: `Devin` → `~/.devin/skills`, `~/.config/devin/skills`; `Claude` → `~/.claude/skills`; `Codex` → `~/.codex/skills`; `OpenCode` → `~/.opencode/skills`, `~/.config/opencode/skills`; `OpenHands` → `~/.openhands/skills`. Unknown/future enum members map to `~/.{executable-name}/skills`.
- **Rules:** paths resolved via `Environment.SpecialFolder.UserProfile`; the table lives in `Domain.Shared` and is extensible; directories are created if absent.
- **Input → Output:** `AgentType` → `IReadOnlyList<string>` absolute paths.

### RF-002: Repository cache (clone/update)

- **Description:** On every sync, update the local clone of the configured skills repository at `{DataDir}/skills-cache`. If absent: `git clone --depth 1 <url>`; if present: `git fetch --depth 1 origin` + checkout/reset to the fetched default branch head. Remote HEAD is resolved with `git remote show`/`symbolic-ref` fallback to `origin/HEAD`.
- **Rules:** a cached clone whose `origin` URL differs from the configured repo is deleted and re-cloned; git failures surface as `Failed` status with a sanitized message (never containing tokens).
- **Input → Output:** configured repo (`owner/repo` or https URL) → up-to-date working tree in the cache dir.

### RF-003: Configurable source repository

- **Description:** Add catalog entry `Taskboard:Skills:Repository` to `RuntimeConfigurationService`: `Editable: true`, `RequiresRestart: false`, `EnvAlias: "TASKBOARD_SKILLS_REPO"`, default `"afonsoft/skills"`. Validation accepts `owner/repo` shorthand or an absolute `https://` git URL; shorthand expands to `https://github.com/{owner}/{repo}.git`.
- **Input → Output:** `owner/repo` | `https://…git` → normalized clone URL.

### RF-004: Install all repo skills into enabled CLIs

- **Description:** For each enabled `AgentType`, enumerate `<cache>/skills/*/SKILL.md` (valid frontmatter `name` + `description`, reused via `FrontmatterReader`) and copy every skill directory into each mapped target directory. The sync installs for enabled agents even when the CLI executable is not on the PATH (target dirs are created).
- **Rules:** copy replaces the whole skill directory when its hash differs (delete the target skill dir, then copy) so removed files inside a managed skill do not linger; an existing same-named skill dir from a different origin is treated as managed once installed and tracked in the manifest.
- **Input → Output:** `AgentType` set + cache dir → per-target list of installed/updated/skipped skills.

### RF-005: Per-skill hash update detection

- **Description:** Compute SHA-256 over the sorted `(relativePath, fileBytes)` of each source skill dir. Persist a manifest `.taskboard-skills.json` in each target skills dir mapping `skillName → { hash, syncedAtUtc, sourceRepo }`. Copy only when the hash differs or the skill is missing; update the manifest after each sync.
- **Rules:** manifest corruption → treated as empty (full reconcile of managed entries, still without deleting anything); manifest entries whose skills are absent from the repo remain installed but are kept in the manifest history (`removedFromSource: true`).

### RF-006: Never delete user skills

- **Description:** The sync must never delete or overwrite skill directories that are not being updated. Skills present in the target but absent from the source repo are preserved untouched. Overwrite happens only at the individual skill level when that skill exists in the repo and its hash changed.
- **Rules:** no recursive delete of the target skills root; only `rm -rf`-equivalent on a single managed skill dir that is about to be re-copied.

### RF-007: Startup sync in background

- **Description:** `SkillsSyncHostedService` (BackgroundService) fires one sync for all enabled agents on application start without blocking `Program.cs`; wrapped in try/catch with a configurable timeout (default 120s). Failure logs a warning and sets status `Failed` — the app continues to serve.
- **Rules:** enabled set = `AgentPreference` rows with `Enabled == true`; when the table is empty (first run), all known `AgentType` values are considered enabled.

### RF-008: Sync on agent enable

- **Description:** `SettingsService.SaveSettingsAsync` computes the newly enabled agent types (enabled now, previously not) and triggers `ISkillsSyncService.SyncAgentsAsync(types)` in the background; the HTTP response does not wait for git/copy completion.
- **Input → Output:** `PUT /api/settings` with an agent toggled ON → `204` + background sync scheduled for that agent.

### RF-009: Sync status and manual trigger endpoints

- **Description:** `GET /api/skills/sync/status` returns `SkillsSyncStatus` (`state`, `lastRunUtc`, `lastDurationMs`, `repository`, `agents[] { type, installed, updated, skipped, error }`); `POST /api/skills/sync` schedules a full sync for all enabled agents and returns `202` with the current status. Both endpoints require the cookie-authenticated session used by `/api/settings`.
- **Rules:** concurrent triggers coalesce — a second request while `Running` returns the in-flight status, no parallel git operations on the cache dir (guarded by `SemaphoreSlim`).

### RF-010: Git availability and private repos

- **Description:** If `git` is not on the PATH, sync short-circuits to `Failed`/`Skipped` with reason `GitUnavailable`. When `GITHUB_TOKEN` (or the token stored via Settings) exists and the repo URL is `https://github.com`, authentication uses `git -c http.https://github.com/.extraheader="AUTHORIZATION: basic <base64(x-access-token:token)>"` passed via process arguments (GitHub git-over-HTTP rejects the Bearer scheme) — the token never appears in logs, status messages, or the manifest.

**Business rules / invariants:**

- Sync is additive; deletion of target content is limited to a single managed skill dir being replaced.
- The cache clone is the only place `git` mutates; target dirs only receive file copies.
- No token in logs/manifests/status payloads.
- Sync never throws past the hosted service / endpoint boundary.

## 5. API Contract

**Endpoint:** `GET /api/skills/sync/status` · `POST /api/skills/sync`
**Auth:** Cookie session (same as `/api/settings`)

**Response (success) — status:**
```json
{
  "state": "Succeeded",
  "lastRunUtc": "2026-09-15T12:00:00Z",
  "lastDurationMs": 3120,
  "repository": "afonsoft/skills",
  "agents": [
    { "type": "Claude", "installed": 23, "updated": 2, "skipped": 21, "error": null }
  ]
}
```

**Expected errors:** `401` unauthenticated; `POST` while running → `202` with current status (coalesced); git failure → `200`/`202` status payload with `state: "Failed"` and sanitized `error` field.

## 6. Acceptance Criteria

- [x] **Given** a fresh install with no skills in `~/.claude/skills` and Claude enabled **when** the server starts **then** within the sync timeout all `skills/*` from the repo exist under `~/.claude/skills` and the manifest is written.
- [x] **Given** skills already installed and the repo unchanged **when** sync runs again **then** every skill reports `skipped` and no file is rewritten.
- [x] **Given** a skill updated upstream **when** sync runs **then** only that skill is re-copied and reported `updated`.
- [x] **Given** a custom skill dir in `~/.claude/skills/mine` absent from the repo **when** sync runs **then** `mine/` is untouched.
- [x] **Given** Codex toggled OFF→ON in Settings **when** `PUT /api/settings` returns **then** a background sync installs skills into `~/.codex/skills` even with no `codex` binary on PATH.
- [x] **Given** no network/git **when** the app starts **then** boot succeeds, status shows `Failed`, and `/skills` still lists previously installed skills.
- [x] **Given** `Taskboard:Skills:Repository` overridden to `other/skills-repo` **when** sync runs **then** the cache re-clones from the new origin.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `git` missing | sync triggered | status `Failed`, `GitUnavailable`, no exception to caller |
| Repo without `skills/` dir | valid clone | sync succeeds with 0 installed, status `Succeeded` |
| `SKILL.md` without valid frontmatter | malformed skill dir | skill dir skipped, counted in `skipped`, warning logged |
| Concurrent `POST /sync` | second request while running | `202` + in-flight status; single git operation |
| Token invalid for private repo | 401 from remote | `Failed` with sanitized message; token absent from logs |
| Config key invalid | `not a repo` | `PUT /api/configuration/Taskboard:Skills:Repository` → `400` validation |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read context files (section 3); confirm `FrontmatterReader`, `IOverrideConfigurationProvider`, `TaskboardEnvironment.GetDataDir` signatures.
- [x] **T2 — Domain/Contracts:** `AgentSkillDirectoryMap`, `ISkillsSyncService`, `SkillsSyncStatus`, `SkillsSyncResult`, catalog key in `RuntimeConfigurationService`.
- [x] **T3 — Integrations:** `GitRunner` (clone/fetch/reset, token extraheader, sanitized errors), `SkillsSyncService` (enumerate, hash, copy, manifest).
- [x] **T4 — Server:** `SkillsSyncHostedService`, endpoints, DI registration.
- [x] **T5 — Application:** `SettingsService` trigger for newly enabled agents.
- [x] **T6 — Tests:** unit (hash diff, dir map, URL normalization, manifest reconcile) + integration (temp-dir sync with a local git repo as source, enable-trigger, endpoint auth).
- [x] **T7 — Validation:** `dotnet build -c Release` (warnings as errors) + `dotnet test`; manual smoke: enable a CLI in Settings and verify files land in its skills dir.
- [ ] **T8 — Done + PR:** set `Status = Done`, open PR on `feature/devin-20260915-skills-repo-sync`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`develop`; use `feature/devin-20260915-skills-repo-sync`.
- **Workflows:** do not modify `.github/workflows/`.
- **Security:** tokens only via git `-c http.extraheader`; never logged, persisted, or returned; no `.env` commits.
- **Scope:** additive sync only — no deletion of unmanaged content, no UI for per-skill selection.
- **Architecture:** business logic in `Integrations`/`Application`; endpoints are thin; Domain holds only the directory map.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) covered by passing tests or equivalent validation evidence; ≥80% coverage on new code.
- [ ] Edge cases handled.
- [ ] `dotnet build` clean (TreatWarningsAsErrors) and `dotnet test` green.
- [ ] Guardrails in section 8 respected; logs contain no tokens.
- [ ] `docs/` updated if behavior is user-visible (settings/skills docs).

## Open Questions / Pending Ambiguity

- Resolved: sync is additive (never deletes); installs even when the CLI binary is absent; update detection = `git pull` + per-skill content hash; triggers = background startup + Settings enable; source repo configurable via `Taskboard:Skills:Repository` (UI field delivered by SPEC-20260915-repo-search-combobox).
- `[A DEFINIR]` exact `~/.codex/skills` and `~/.openhands/skills` conventions — implementation should verify against each CLI's documented skills path and adjust the map.
