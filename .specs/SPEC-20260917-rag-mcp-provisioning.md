# SPEC-20260917-rag-mcp-provisioning

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `rag-mcp-provisioning` |
| Type | `Feature` |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / Blazor WASM / JSON + TOML config merge` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/devin-20260917-rag-mcp-provisioning` |
| Ticket | N/A |
| Status | `Done` |

## 1. User Story

**As a** Taskboard administrator,
**I want** a "RAG / Knowledge MCP" section in `/settings` where I enter the MCP server name, URL and API key once — and on save/sync the server is written into the MCP configuration of every enabled agent CLI,
**So that** all my CLIs (Devin, Claude, Codex, OpenCode, OpenHands) can query the RAG endpoint (e.g. `https://rag.afonsoft.dev/mcp`) with `Authorization: Bearer <key>` without me hand-editing five different config files.

**Problem context:**

- Each CLI stores MCP servers in a different file and format — verified on the target machine:
  - **Devin CLI** → `~/.config/devin/mcp_config.json` → `mcpServers.<name>` = `{ "url", "transport": "http", "headers": { "Authorization": "Bearer <key>" } }`
  - **Claude Code** → `~/.claude.json` → root `mcpServers.<name>` = `{ "type": "http", "url", "headers" }` (equivalent to `claude mcp add -s user --transport http <name> <url> --header "Authorization: Bearer <key>"`)
  - **Codex** → `~/.codex/config.toml` → `[mcp_servers.<name>]` `url = "..."` + `[mcp_servers.<name>.headers]` `Authorization = "Bearer <key>"`
  - **OpenCode** → `~/.config/opencode/opencode.json` → `mcp.<name>` = `{ "type": "remote", "url", "headers", "enabled": true }`
  - **OpenHands** → `~/.openhands/mcp.json` (`OPENHANDS_PERSISTENCE_DIR/mcp.json`) → `mcpServers.<name>` = `{ "url", "headers": { "Authorization": "Bearer <key>" } }` — confirmed against installed `openhands_sdk 1.21` (`fastmcp.mcp_config.MCPConfig` / `RemoteMCPServer`); transport inferred from the URL
- There is no UI or service that provisions MCP servers today; secrets live only in env vars.

## 2. Scope

**In scope:**

- New **"RAG / Knowledge MCP"** section in `Settings.razor` (below *Integrations*): fields *Server name* (default `knowledge`), *URL* (`https://…` required), *API key* (password input with Show/Hide, masked in reads); **Save & Sync** button; per-agent status badges.
- Three new `RuntimeConfigurationService` catalog keys, editable, no restart:
  - `Taskboard:Rag:ServerName` (default `knowledge`, env alias `TASKBOARD_RAG_NAME`)
  - `Taskboard:Rag:Url` (env alias `TASKBOARD_RAG_URL`)
  - `Taskboard:Rag:ApiKey` (env alias `TASKBOARD_RAG_API_KEY`; auto-masked by `IsSecret`)
- `IMcpProvisioningService` in `Taskboard.Integrations` with a per-agent writer table (JSON/TOML merge into the config files listed above).
- Provisioning targets = `AgentType` values enabled in `AgentPreference` (config files are written regardless of the CLI binary being on PATH, same philosophy as `SPEC-20260915` RF-004).
- Triggers: (a) save of the section → background provision; (b) **Sync** button → `POST /api/mcp/sync`; (c) agent toggled ON in `SaveSettingsAsync` → provision for that agent (alongside the existing skills sync).
- Removal: clearing `Taskboard:Rag:Url` removes the managed `<name>` entry from all agent configs (other MCP entries untouched).
- `GET /api/mcp/status` — per-agent verification by re-reading the config files.
- Unit + integration tests.

**Out of scope:**

- Multiple/named MCP servers beyond the single RAG entry.
- OAuth flows (`devin mcp login`, `claude mcp` auth).
- Project-scope configs (`.devin/mcp_config.json`, `.mcp.json`) — only user/global scope.
- Cursor, Gemini, VS Code and other agents not present in `AgentType`.
- Health-checking the remote URL (no outbound HTTP call to the RAG endpoint).
- Editing/removing non-managed MCP entries in the config files.

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Application` — `RuntimeConfigurationService`: three catalog entries + validators (URL must be absolute http(s); name `^[a-z0-9][a-z0-9-]{0,63}$`; key empty-or-≥8 chars).
- **Persistence is already wired to SQLite** — `ConfigurationOverride` rows live in the `ConfigurationOverrides` table (`{DataDir}/taskboard.sqlite`, migration `20260915001304_AddConfigurationOverrides`) and `SqliteConfigurationProvider` (`Program.cs:62`) loads them into `IConfiguration` at startup (db > env > appsettings > default). The three new keys need **no new entity or migration** — they persist via `RuntimeConfigurationService.SetOverrideAsync` → `IRepository<ConfigurationOverride>`.
- `Taskboard.Domain.Shared` — new `Mcp/AgentMcpConfigMap.cs`: `AgentType` → config-file path + format kind (`Json | Toml`) + entry template.
- `Taskboard.Integrations` — new `Mcp/McpProvisioningService.cs`, `Mcp/JsonConfigMerger.cs`, `Mcp/TomlConfigMerger.cs`.
- `Taskboard.Application` — `SettingsService.SaveSettingsAsync`: provision newly enabled agents (same hook point as the skills sync).
- `Taskboard.Server` — `Program.cs`: `PUT /api/mcp/rag` (save the three values + schedule provision), `POST /api/mcp/sync`, `GET /api/mcp/status`.
- `Taskboard.Blazor` — `Settings.razor` new section; `Taskboard.Client` — new methods.
- TOML editing: prefer the `Tomlyn` NuGet package (MIT, maintained) over hand-rolled text splicing — must be justified in the PR description per repo soft rules.

**Files to read before implementing:**

- `CLAUDE.md` · `.claude/rules/global-rules.md`
- `.specs/SPEC-20260915-skills-repo-sync.md` (enable-trigger + status patterns)
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs`
- `src/Taskboard.Application/Settings/SettingsService.cs`
- `src/Taskboard.Domain.Shared/Skills/AgentSkillDirectoryMap.cs` (map-table pattern)
- `src/Taskboard.Domain.Shared/Agents/AgentType.cs`
- `src/Taskboard.Server/Program.cs` (`api` group + auth)
- `src/Taskboard.Blazor/Components/Pages/Settings.razor`
- `src/Taskboard.Client/TaskboardClient.cs`

**Files to create or modify:**

```text
src/Taskboard.Domain.Shared/Mcp/AgentMcpConfigMap.cs                    # NEW
src/Taskboard.Domain.Shared/Mcp/McpConfigFormat.cs                      # NEW — Json | Toml
src/Taskboard.Application.Contracts/Mcp/IMcpProvisioningService.cs      # NEW
src/Taskboard.Application.Contracts/Mcp/McpProvisionStatus.cs           # NEW
src/Taskboard.Application.Contracts/Mcp/McpAgentResult.cs               # NEW
src/Taskboard.Application.Contracts/Requests/SaveRagMcpRequest.cs       # NEW
src/Taskboard.Integrations/Mcp/McpProvisioningService.cs                # NEW
src/Taskboard.Integrations/Mcp/JsonConfigMerger.cs                      # NEW
src/Taskboard.Integrations/Mcp/TomlConfigMerger.cs                      # NEW
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs  # MOD — 3 catalog keys
src/Taskboard.Application/Settings/SettingsService.cs                   # MOD — provision on enable
src/Taskboard.Server/Program.cs                                         # MOD — DI + endpoints
src/Taskboard.Client/TaskboardClient.cs                                 # MOD
src/Taskboard.Blazor/Components/Pages/Settings.razor                    # MOD — RAG/MCP section
tests/Taskboard.Tests.Unit/Integrations/Mcp/JsonConfigMergerTests.cs    # NEW
tests/Taskboard.Tests.Unit/Integrations/Mcp/TomlConfigMergerTests.cs    # NEW
tests/Taskboard.Tests.Unit/Integrations/Mcp/McpProvisioningServiceTests.cs # NEW
tests/Taskboard.Tests.Integration/McpEndpointsTests.cs                  # NEW
Directory.Packages.props                                                # MOD — Tomlyn (if chosen)
```

## 4. Requirements

### RF-001: Catalog keys and validation

- **Description:** Add `Taskboard:Rag:ServerName` (default `knowledge`), `Taskboard:Rag:Url`, `Taskboard:Rag:ApiKey` to the catalog: `Editable: true`, `RequiresRestart: false`, env aliases `TASKBOARD_RAG_NAME` / `TASKBOARD_RAG_URL` / `TASKBOARD_RAG_API_KEY`.
- **Rules:** URL must be an absolute `http`/`https` URI or empty; name must match `^[a-z0-9][a-z0-9-]{0,63}$`; key may be empty (unauthenticated server) or ≥8 chars; `ApiKey` is masked automatically by `IsSecret`.
- **Input → Output:** `PUT` value → `200`/`400` per existing configuration write path.

### RF-002: Per-agent config map

- **Description:** `AgentMcpConfigMap` maps each `AgentType` to `{ relativePath, format, buildEntry(name, url, key) }`:

  | Agent | Path (under `~`) | Format | Entry |
  | --- | --- | --- | --- |
  | Devin | `.config/devin/mcp_config.json` | JSON | `mcpServers.<n>` = `{ "url", "transport": "http", "headers": { "Authorization": "Bearer <key>" } }` |
  | Claude | `.claude.json` | JSON | root `mcpServers.<n>` = `{ "type": "http", "url", "headers": { "Authorization": "Bearer <key>" } }` |
  | Codex | `.codex/config.toml` | TOML | `[mcp_servers.<n>]` `url`; `[mcp_servers.<n>.headers]` `Authorization = "Bearer <key>"` |
  | OpenCode | `.config/opencode/opencode.json` | JSON | `mcp.<n>` = `{ "type": "remote", "url", "headers", "enabled": true }` |
  | OpenHands | `.openhands/mcp.json` | JSON | `mcpServers.<n>` = `{ "url", "headers": { "Authorization": "Bearer <key>" } }` (FastMCP `RemoteMCPServer`; transport inferred from URL) |

- **Rules:** when `ApiKey` is empty, emit no `headers`/`api_key`; unknown/future `AgentType` members are skipped with a warning in the result.

### RF-003: Idempotent merge

- **Description:** Read the existing file (tolerate missing file → start from empty document; tolerate corrupt file → back it up to `.corrupt-bak` and start empty, result marked `Repaired`), upsert only the managed `<name>` entry, preserve every other key/server, write back atomically (temp file + rename) with a `.bak` copy of the previous content and `0600` permissions.
- **Rules:** JSON merge via `System.Text.Json.Nodes` (no new dependency); TOML merge via `Tomlyn` (or a documented minimal section-merge if the dependency is rejected in review).

### RF-004: Provisioning triggers

- **Description:** `IMcpProvisioningService.RequestProvision(IReadOnlyCollection<AgentType>?)` runs in the background; `ProvisionAsync` is the blocking variant. Triggers: `PUT /api/mcp/rag` save, `POST /api/mcp/sync`, and `SettingsService.SaveSettingsAsync` for newly enabled agents. Concurrent runs coalesce via `SemaphoreSlim`.
- **Rules:** no provision when `Taskboard:Rag:Url` is empty — instead run the removal pass (RF-006).

### RF-005: Status / verify

- **Description:** `GET /api/mcp/status` re-reads every agent config file and reports per-agent `{ agent, configured, path, transport, error }`; `configured = true` only when the managed entry exists **and** its URL equals the configured URL. Also returns `state`, `lastRunUtc`, `configuredUrl` (never the key).
- **Input → Output:** filesystem scan → `McpProvisionStatus`.

### RF-006: Removal

- **Description:** When `Taskboard:Rag:Url` is empty/cleared, the provision pass deletes the managed `<name>` entry from each agent config (same atomic-write rules). Other entries are never touched.
- **Input → Output:** cleared config → per-agent `removed` results.

### RF-007: Endpoints

- **Description:**
  - `PUT /api/mcp/rag` body `{ name, url, apiKey }` → validates all three via the catalog validators, persists them, schedules provision → `204`. `apiKey: null` = keep existing stored key; `""` = clear.
  - `GET /api/mcp/status` → `200` `McpProvisionStatus`.
  - `POST /api/mcp/sync` → `202` + current status (coalesced).
- **Auth:** cookie session, same as `/api/settings`. The API key is never returned by any endpoint.

### RF-008: Settings UI section

- **Description:** "RAG / Knowledge MCP" section: inputs for name/URL/key (key input shows masked placeholder `••••` and only overwrites when the user types a new value), **Save & Sync**, **Sync** and **Verify** buttons, per-agent badges (`configured` / `not configured` / `error` + tooltip with file path).
- **Rules:** poll `GET /api/mcp/status` while `state == Running`; validation errors inline (`alert-danger`) and toasts on completion.

### RF-009: Failure isolation and secrecy

- **Description:** A failing agent write does not fail the batch — per-agent `error` is captured; the API key is never logged, never in payloads, never in the `.bak`/manifest metadata beyond the config files themselves.
- **Rules:** backups inherit `0600`; file writes use `FileMode.Create` on a temp path then `File.Move` (atomic on POSIX).

**Business rules / invariants:**

- Only the managed `<name>` entry is ever created/modified/deleted.
- Provision targets = enabled `AgentPreference` set; when the table is empty (first run) all known `AgentType` values are targets (same rule as skills sync RF-007).
- Config writes are atomic and permissioned `0600`; a `.bak` is always left behind on overwrite.

## 5. API Contract

**Endpoint:** `PUT /api/mcp/rag` · `GET /api/mcp/status` · `POST /api/mcp/sync`
**Auth:** Cookie session

**Request — PUT:**

```json
{ "name": "knowledge", "url": "https://rag.afonsoft.dev/mcp", "apiKey": "aft_…" }
```

**Response — GET status:**

```json
{
  "state": "Succeeded",
  "lastRunUtc": "2026-09-17T12:00:00Z",
  "configuredUrl": "https://rag.afonsoft.dev/mcp",
  "agents": [
    { "agent": "Devin", "configured": true, "path": "~/.config/devin/mcp_config.json", "transport": "http", "error": null },
    { "agent": "Claude", "configured": true, "path": "~/.claude.json", "transport": "http", "error": null },
    { "agent": "Codex", "configured": true, "path": "~/.codex/config.toml", "transport": "http", "error": null },
    { "agent": "OpenCode", "configured": false, "path": "~/.config/opencode/opencode.json", "transport": null, "error": "config file corrupt — backed up" },
    { "agent": "OpenHands", "configured": true, "path": "~/.openhands/config.toml", "transport": "stdio", "error": null }
  ]
}
```

**Expected errors:** `400` validation (bad URL/name); `401` unauthenticated; `202` coalesced sync.

## 6. Acceptance Criteria

- [x] **Given** name `knowledge`, URL `https://rag.afonsoft.dev/mcp` and key `aft_x` saved **when** provision runs **then** `~/.claude.json`, `~/.codex/config.toml`, `~/.config/devin/mcp_config.json`, `~/.config/opencode/opencode.json` and `~/.openhands/config.toml` each contain a `knowledge` entry with that URL and a Bearer header — and all pre-existing MCP entries are intact.
- [x] **Given** an existing config file with other servers **when** provision runs **then** only the managed entry is added/updated and the file round-trips (valid JSON/TOML, other keys preserved).
- [x] **Given** the URL is cleared **when** saved **then** the `knowledge` entry is removed from every agent file and no other entry changes.
- [x] **Given** a corrupt `opencode.json` **when** provision runs **then** it is backed up to `.corrupt-bak`, rewritten with only the managed entry, and the agent result reports `Repaired`.
- [x] **Given** a new agent toggled ON in Settings **when** `PUT /api/settings` returns **then** that agent's config file receives the managed entry in the background.
- [x] **Given** status requested **when** an agent file is missing **then** `configured: false` (not an error).
- [x] **Given** any read path **when** the API key is set **then** no endpoint, log line or status payload contains the key.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Invalid URL | `notaurl` | `400` with validation message; nothing persisted |
| Empty key | `apiKey: ""` | entry written without `headers`/`api_key` |
| `apiKey: null` on save | unchanged | stored key preserved |
| AgentType unknown | future enum member | skipped with warning in result |
| Concurrent saves | two PUTs | single provision run (coalesced) |
| File readonly | permission denied | per-agent `error`, others still written |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read section-3 files; confirm `IOverrideConfigurationProvider`, `AgentPreference` and endpoint-auth patterns. Verify OpenHands `shttp_servers` schema against the installed version; fall back to stdio `mcp-remote` if unsupported.
- [x] **T2 — Catalog:** three keys + validators in `RuntimeConfigurationService` (with tests).
- [x] **T3 — Domain.Shared:** `AgentMcpConfigMap` + `McpConfigFormat`.
- [x] **T4 — Integrations:** `JsonConfigMerger`, `TomlConfigMerger` (+`Tomlyn` dep, justified in PR), `McpProvisioningService` (provision/remove/status, atomic writes, semaphore).
- [x] **T5 — Application + Server:** `SettingsService` enable-hook; DI + 3 endpoints.
- [x] **T6 — Client/UI:** `TaskboardClient` methods + Settings section.
- [x] **T7 — Tests:** unit (mergers: upsert/remove/preserve/corrupt; validators; map) + integration (endpoints auth, temp-home provision against fixture files).
- [x] **T8 — Validation:** `dotnet build -c Release` + `dotnet test`; manual smoke: save RAG config → check all five files.
- [x] **T9 — Done + PR:** `Status = Done`, PR on `feature/devin-20260917-rag-mcp-provisioning`.

**7.1 Validation strategy:** .NET — unit tests for mergers/validators, integration tests for endpoints and provision flows; ≥80% coverage on new code; `TreatWarningsAsErrors`.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`develop`; branch `feature/devin-20260917-rag-mcp-provisioning`.
- **Workflows:** do not modify `.github/workflows/`.
- **Security:** API key write-only (masked in reads, never logged, never in status payloads); config files written `0600` with `.bak`; no secrets in tests/fixtures.
- **Scope:** one managed RAG server only; no per-project configs; no OAuth.
- **Architecture:** merge/provision logic in `Integrations`; endpoints thin; UI reads via `TaskboardClient`.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests; ≥80% coverage on new code.
- [x] Edge cases handled.
- [x] `dotnet build` clean (TreatWarningsAsErrors) and `dotnet test` green.
- [x] Guardrails respected; logs contain no keys/tokens.
- [x] `docs/` updated (settings/RAG docs) since behavior is user-visible.
- [x] `Tomlyn` (if added) justified in the PR description.

## Open Questions / Pending Ambiguity

- Resolved: OpenHands uses `~/.openhands/mcp.json` (`mcpServers` shape, FastMCP `RemoteMCPServer` — `url` + `headers` + optional `auth`) — verified against the installed `openhands_sdk 1.21.0` package; no `mcp-remote` fallback needed for the five known agents.
