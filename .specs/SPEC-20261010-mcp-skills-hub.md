# SPEC-20261010-mcp-skills-hub: Settings — MCP/Skills tab + AI Code reads ~/.agents MCPs

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | MCP/Skills settings tab — manage MCPs on agent CLIs, install/search skills, AI Code consumes `~/.agents` MCPs |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Integrations (Mcp, Skills) + Application(.Contracts) + Server endpoints + Blazor WASM (`Settings.razor`) |
| Change type | Feature |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261010-mcp-skills-hub` |
| Depends on | SPEC-20261001-chat-mcp-client (`ChatMcpClientManager`), SPEC-20261004 `McpProvisioningService` + `AgentMcpConfigMap`, SPEC-20261010-settings-configuration-tab (tab order, `ManagedIn` for `Chat:Mcp:*`) |

## 1. Executive Summary

### Problem

Three gaps:

1. **AI Code ignores `~/.agents`** — `ChatMcpClientManager.LoadSpecs()` only
   reads `Taskboard:Chat:Mcp:Servers` + the RAG spec. MCPs the user already
   installed globally under `~/.agents` (the canonical agents-skills layout,
   e.g. `~/.agents/mcps/*.json` written by `install.sh`) never reach the chat.
2. **No MCP management UI** — the only provisioning surface is the RAG
   "sync into agents" flow. There's no list of which MCPs each CLI has, and
   no way to add a new MCP server to claude/codex/opencode/etc. without
   hand-editing their config files.
3. **Skills install is all-or-nothing** — `SkillsInstallerService` runs
   `npx skills add <repo> -g --all --copy` for the single configured
   repository. No search (skills.sh ecosystem), no per-skill or extra-repo
   installs.

### Solution

New Settings tab `("mcp-skills", "MCP/Skills")` with three sections, plus the
backend pieces below.

**A. Chat MCP (AI Code)** — `Taskboard:Chat:Mcp:Enabled` toggle, per-server
status table (name, transport, healthy, tool count, **origin badge**:
`config` / `~/.agents` / `rag`), `CallTimeoutSeconds`, and a new
`Taskboard:Chat:Mcp:IncludeGlobalAgents` toggle (default `true`).

`GlobalAgentsMcpLoader` (Integrations/Mcp) scans, in order:

- `~/.agents/mcp.json` and `~/.agents/mcp_config.json` — `{ "mcpServers": { name: {url|command,args,env,headers} } }`
- `~/.agents/mcps/**/*.json` — same `mcpServers` map per file (agents-skills convention)

Each entry becomes a `ChatMcpServerSpec` tagged `Origin = "agents-global"`.
`LoadSpecs()` merges config + global + RAG with precedence
**config > ~/.agents > rag** on name collision; the existing fingerprint
(`ComputeSpecsFingerprint`) extends to file contents+mtimes so hot reload
picks up edits — tools appear in chat as `mcp_<server>_<tool>` exactly like
config-defined servers.

**B. Agent CLI MCPs** — inventory + write:

- `GET /api/local/mcp/agents` → per-agent list of installed MCP entries
  (`AgentMcpInventoryService` reads each `AgentMcpConfigMap` target file —
  JSON/Toml per `Format`, `mcpServers`/`mcp.servers`/`ContainerKey` per
  `EntryStyle`; missing file → empty, never error).
- `POST /api/local/mcp/agents/install` `{ spec: ChatMcpServerSpec, agents: AgentType[] }`
  → generalized provisioner. Today's `McpProvisioningService` is RAG-shaped;
  extract its merge/write core into `McpConfigFileWriter` (idempotent merge,
  atomic temp+rename, `.bak`, `0600`) reused by both the RAG flow and the new
  `AgentMcpProvisioningService` (one entry, arbitrary spec, selected agents).
- `POST /api/local/mcp/agents/remove` `{ name, agents[] }` → removes the entry.
- Results reuse `McpOperationLog` + per-agent `McpAgentResult` rows.

**C. Skills** — installed list already exists (`GET /api/local/skills` via
`ISkillDiscoveryService`). Add:

- `POST /api/local/skills/install-repo` `{ repository? }` — same pipeline as
  today's install (npx add `-g --all --copy` + `install.sh --all`), optional
  body repo overriding `Taskboard:Skills:Repository` for that run only.
- `GET /api/local/skills/search?q=` → server-side `npx skills find <q>`
  (timeout 20s, output parsed, capped 20 results → `{ name, repository, description? }`).
- `POST /api/local/skills/install-one` `{ repository, skill? }` →
  `npx skills add <repo>[@<skill>] -g -y --copy` for a single skill.

### Scope

In scope: `GlobalAgentsMcpLoader` + merge/fingerprint, agent MCP
inventory/install/remove, skills search + granular install, the new tab UI,
tests. Out of scope: editing MCP args in-place (remove+re-add), per-agent
enable flags (agent CLIs read their config themselves), MCP OAuth flows,
skills.sh account features.

## 2. Requisitos

### RF-001 — `GlobalAgentsMcpLoader`

`src/Taskboard.Integrations/Mcp/GlobalAgentsMcpLoader.cs`:

- Roots: `$HOME/.agents` (env `HOME`/`USERPROFILE` — same resolution as
  `AgentMcpConfigMap.GetConfigPath`).
- Files: `mcp.json`, `mcp_config.json`, `mcps/**/*.json` (recursive, sorted —
  deterministic order).
- Parse lenient: malformed file → warn + skip; entry without `url` or
  `command` → skip; `env` values of form `env:VAR` pass through to the
  existing resolver.
- Output: `IReadOnlyList<ChatMcpServerSpec>` + per-spec `Origin` (extend the
  record with `string Origin` default `"config"`) and a combined fingerprint
  string (names + content hash + last-write ticks) for hot reload.

### RF-002 — `ChatMcpClientManager` merge

`LoadSpecs()` → `config servers ∪ global agents ∪ RAG` (dedup by `Name`,
first wins in that order). `GetServers()` entries gain `Origin`.
`Taskboard:Chat:Mcp:IncludeGlobalAgents` (default `true`) gates the global
load — still requires `Chat:Mcp:Enabled`. File-watch: fingerprint recomputed
on the existing poll path; no `FileSystemWatcher`.

### RF-003 — `McpConfigFileWriter` + `AgentMcpProvisioningService`

Extract from `McpProvisioningService`: `MergeEntry(file, containerKey,
entryStyle, format, name, entryJson)` + `WriteAtomic(path, content)` +
`0600`/`.bak`. RAG flow refactors onto it unchanged (tests must stay green).

`AgentMcpProvisioningService.ApplyAsync(spec, agents, remove, ct)`:
per agent → resolve `AgentMcpConfigMap.GetTarget` (skip `null` targets +
Antigravity/Cline/Continue keep their existing custom provision paths where
applicable — those stay RAG-only in P1; UI disables non-mapped agents),
merge or remove the entry, record `McpAgentResult`.

### RF-004 — Endpoints

```
GET  /api/local/mcp/agents                 → [{ agent, configPath, servers: [{name, transport, detail}] }]
POST /api/local/mcp/agents/install         → { name,url?|command,args?,env?,headers?, agents[] } → 202 + log
POST /api/local/mcp/agents/remove          → { name, agents[] } → 202
GET  /api/local/mcp/chat                   → ChatMcpServerStatus[] + origin (wraps IMcpClientManager)
POST /api/local/skills/install-repo        → { repository? } → 202
GET  /api/local/skills/search?q=           → [{ name, repository, description? }] (≤20)
POST /api/local/skills/install-one         → { repository, skill? } → 202
```

All under `api` group → `RequireAuthorization` as today. Status/progress via
existing `mcp/log` + `skills/log` + `install/status` polling pattern.

### RF-005 — `npx` runners

`SkillsSearchService` + granular add reuse `SkillsInstallerService`'s process
runner (timeout, output capture, `Sanitize`). `find` output parser tolerant —
on parse failure return raw lines as `name` rows. `install-one` validates
`repository` with `ValidateSkillsRepository` + `skill` as `[a-z0-9-_]{1,64}`.

### RF-006 — Settings `MCP/Skills` tab

`SettingsTabs += ("mcp-skills","MCP/Skills")` (order from
SPEC-20261010-settings-configuration-tab). Sections:

1. **AI Code MCP** — `Chat:Mcp:Enabled` + `IncludeGlobalAgents` switches,
   timeout input, servers table (name, transport, tools, origin badge,
   healthy icon + error tooltip), Refresh.
2. **Agent CLI MCPs** — per-agent cards/table (agent → server chips);
   "Add MCP" modal: name, transport (URL | command), url or
   `command`+`args`, optional headers/env key-value editor, agent
   multi-select (only agents with a config target), submit → 202 → poll log.
   Remove button per chip (confirm modal, same agent multi-scope).
3. **Skills** — repo install box (default `afonsoft/skills`), search input →
   results list with "Install" per row, installed skills list (name, source,
   files link → existing skills endpoints), Re-verify button.

Modals carry `Fullscreen="ModalFullscreen.SmallDown"` (razor-source rule).

## 3. Arquitetura

```
~/.agents/{mcp.json, mcp_config.json, mcps/**/*.json}
        └─▶ GlobalAgentsMcpLoader ──▶ ChatMcpServerSpec(Origin="agents-global")
                                         │
Taskboard:Chat:Mcp:Servers (config) ─────┤ merge (config > global > rag)
RAG spec ────────────────────────────────┘
                                         ▼
                              ChatMcpClientManager.LoadSpecs
                                  (fingerprint incl. files)
                                         ▼
                         mcp_<server>_<tool> adapters → AI Code

Settings MCP/Skills tab
   ├─ chat MCP section ──▶ GET /api/local/mcp/chat
   ├─ agent MCPs ────────▶ GET/POST /api/local/mcp/agents[/install|/remove]
   │                          └─▶ AgentMcpInventoryService / AgentMcpProvisioningService
   │                                   └─▶ McpConfigFileWriter (merge+atomic+0600)
   └─ skills ────────────▶ skills/search, skills/install-repo, skills/install-one
                              └─▶ SkillsInstallerService pipeline (npx)
```

Files (new): `Integrations/Mcp/GlobalAgentsMcpLoader.cs`,
`Integrations/Mcp/McpConfigFileWriter.cs`,
`Integrations/Mcp/AgentMcpInventoryService.cs`,
`Integrations/Mcp/AgentMcpProvisioningService.cs`,
`Integrations/Skills/SkillsSearchService.cs`,
contracts `AgentMcpInventoryDto`, `AgentMcpInstallRequest`,
`SkillSearchResultDto`, `SkillInstallRequest`.
Touched: `ChatMcpClientManager` (merge + fingerprint + origin),
`ChatMcpServerSpec`/`ChatMcpServerStatus` (+`Origin`),
`McpProvisioningService` (extract writer), `Program.cs` (endpoints),
`RuntimeConfigurationService` (`IncludeGlobalAgents`, `ManagedIn` on the 3
`Chat:Mcp:*` keys), `Settings.razor`, `TaskboardClient`, tests.

## 4. Config

| Key | Default | Editable | Restart | ManagedIn |
|---|---|---|---|---|
| `Taskboard:Chat:Mcp:IncludeGlobalAgents` | `true` | ✓ | ✗ | — |
| `Taskboard:Chat:Mcp:Enabled` | `false` | ✓ | ✗ | `/settings?tab=mcp-skills` |
| `Taskboard:Chat:Mcp:Servers` | `[]` | ✓ | ✗ | `/settings?tab=mcp-skills` |
| `Taskboard:Chat:Mcp:CallTimeoutSeconds` | `30` | ✓ | ✗ | `/settings?tab=mcp-skills` |
| `Taskboard:Skills:Repository` | `afonsoft/skills` | ✓ | ✗ | `/settings?tab=mcp-skills` (atualiza o ManagedIn da SPEC-settings) |

## 5. Segurança

- `~/.agents` specs run **stdio commands** — trust boundary equals the user's
  own config files (same as claude/codex configs today); loaders reject
  entries whose `command` is absolute-path outside allowlist? No — keep
  parity with config-defined servers (identical execution path), but gate the
  whole feature behind `Chat:Mcp:Enabled` and document it.
- `env:VAR` resolution is server-side only — values never serialized to the
  client (existing contract).
- `npx skills add` arguments are array-form (no shell) — repo/skill validated
  before spawn; output `Sanitize`d into the log (no tokens).
- MCP write path preserves `0600` + `.bak`; no world-readable configs.
- `skills/search` passes `q` as an argv element — never through a shell.

## 6. Testes

Unit (pt-BR `Dado_Quando_Entao`):

- `GlobalAgentsMcpLoaderTests`: `mcp.json` parse → specs; `mcps/*.json`
  múltiplos mergeados; JSON inválido → skip + warn; entrada sem url/command →
  skip; `env:VAR` preservado; fingerprint muda ao editar arquivo.
- `ChatMcpClientManagerMergeTests`: config > global > rag em colisão de nome;
  `IncludeGlobalAgents=false` → global ausente; toggle off → specs vazios.
- `AgentMcpInventoryServiceTests`: lê `mcpServers` em JSON e TOML por
  `AgentMcpConfigMap`; arquivo ausente → lista vazia; corrompido → vazio+warn.
- `AgentMcpProvisioningServiceTests`: install cria/mergeia entry idempotente
  (rodar 2× → mesmo arquivo); remove apaga só a entry; `.bak` criado; agent
  sem target → resultado `Skipped`.
- `SkillsSearchServiceTests`: runner mock — parser de linhas; timeout;
  repositório inválido → 400.
- Razor-source guard (`SettingsMcpSkillsTabTests`): `data-tab="mcp-skills"`,
  seções AI Code MCP / Agent CLI MCPs / Skills, modals com
  `Fullscreen="ModalFullscreen.SmallDown"`, toggles bound.

Integration (`WebApplicationFactory`):

- `McpAgentsEndpointsTests`: GET inventory 200; POST install com spec url →
  arquivo do fake-home contém entry; POST remove remove; sem auth → 401.
- `SkillsEndpointsTests`: install-repo com repo override → runner recebeu
  `skills add <repo> -g --all --copy`; install-one → `skills add repo@skill -g -y --copy`;
  search q= → lista parseada.
- `ChatMcpGlobalAgentsTests`: fake `~/.agents/mcp.json` + `Chat:Mcp:Enabled` →
  `GetToolsAsync` inclui `mcp_<name>_*` (transport factory fake).

## 7. Fases

- **P1**: RF-001 + RF-002 + `GET /mcp/chat` + seção AI Code MCP da aba
  (shell do tab). Valor imediato: o chat já ganha os MCPs de `~/.agents`.
- **P2**: RF-003 + RF-004(mcp/agents) + seção Agent CLI MCPs (inventory +
  add/remove).
- **P3**: RF-005 + seção Skills (search + install granular).

## Acceptance criteria

- Com `Chat:Mcp:Enabled` e um `~/.agents/mcp.json` válido → AI Code lista e
  chama `mcp_<server>_<tool>` sem config adicional; desligar
  `IncludeGlobalAgents` remove as tools no próximo reload.
- Aba `?tab=mcp-skills` mostra as três seções; servidores listam origem
  (config / ~/.agents / rag) e saúde.
- "Add MCP" instalando `{"name":"ctx7","url":"https://mcp.ctx7.dev"}`
  em claude+codex → ambos os config files ganham a entry; re-rodar não
  duplica; remove apaga.
- Busca `playwright` em skills retorna resultados e instala o escolhido em
  `~/.agents/skills` (visível na lista).
- Editar `~/.agents/mcp.json` com a app aberta → novo server aparece no
  status sem restart (fingerprint reload).

## Open questions

1. Origem `~/.agents` pode trazer servidores pesados (stdio de boot lento) —
   manter timeout por server e isolamento de falha (já existe); ok?
2. `install-one` fora do repo padrão grava onde? `--copy` já centraliza em
   `~/.agents/skills` — mas o manifest `skills-install.json` só conta o repo
   configurado; proposto: manifest ganha lista `ExtraInstalls`.
3. TOML write para codex — parser manual simples (entry table) vs. dependência
   `Tomlyn`; proposto: `Tomlyn` (NuGet justificado no PR).
4. skills.sh search depende de `npx skills find` estável — fallback
   documentado: colar `owner/repo[@skill]` manualmente no campo de install.
