# SPEC-20261010-settings-configuration-tab: Settings — dedicated Configuration tab, categories, read-only keys, connection info

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Settings Configuration tab — categorized keys, read-only Port/BaseUrl, connection/provider display, dedupe of managed keys |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Application (`RuntimeConfigurationService` catalog) + Blazor WASM (`Settings.razor`) |
| Change type | Feature / UX |
| Repository | afonsoft/agent-harness |
| Technical owner | afonsoft |
| Suggested branch | `feat/devin-20261010-settings-configuration-tab` |
| Depends on | SPEC-20260930-settings-tabs; complements SPEC-20261010-agents-page-tabs (Prompt tab) and SPEC-20261010-mcp-skills-hub (MCP/Skill tab) |

## 1. Executive Summary

### Problem

The **General** tab mixes two unrelated blocks: the Features switches and a
flat **Configuration** table with ~30 keys in one undifferentiated list —
uneditable and editable keys side by side, no grouping, no indication that
several keys are already managed on other screens (editing them in two places
confuses the source of truth).

Specific asks:

- Dedicated **Configuration** tab with keys **grouped by category** and more
  visual, easier editing.
- `Taskboard:Port` and `Taskboard:BaseUrl` become **read-only** (today they're
  editable-but-restart — a footgun).
- A **connection block** that names the database provider (SQLite vs
  PostgreSQL) and the cache mode (in-memory vs Redis) and shows the keys.
- Keys that already have a dedicated UI elsewhere are **removed** from the
  list (`Taskboard:Agents:DefaultPrompt`, `Taskboard:Terminal:Enabled`,
  `Taskboard:WebCliAgent:Enabled`, …).

### Solution

New tab `("configuration", "Configuration")` between `general` and
`integrations`. The catalog (`RuntimeConfigurationService.Catalog`) gains two
metadata fields per entry:

- `Group` — `Connections | Server | Logging | Chat | Security` — drives the
  categorized sections.
- `ManagedIn` — deep link (e.g. `/agents?tab=prompt`, `/settings?tab=chat`)
  for keys with a dedicated UI. Entries with `ManagedIn` are **excluded** from
  the Configuration table; the tab shows a compact "Managed in other screens"
  note with the links instead (discoverability without duplicate editing).

The **Connections** section gets a read-only info header:

- **Database** — provider badge derived from config: `sqlite` when
  `UseSqlite`/`Data Source=*.db` connection string (today always SQLite), or
  `postgres` when an `Npgsql`/`Host=` string is configured; shows
  `Taskboard:Database:ConnectionStringName` + `ConnectionStrings:*` entries.
- **Cache** — badge `In-memory (L1)` vs `Redis (L1+L2)` from
  `Taskboard:Cache:Redis:ConnectionString` presence; shows the four
  `Taskboard:Cache:*` keys.

`Taskboard:Port` and `Taskboard:BaseUrl` flip to `Editable: false` +
`ReadOnlyReason: "Server binding — set via HARNESS_PORT / HARNESS_URL or appsettings.json."`

### Scope

In scope: catalog metadata (`Group`, `ManagedIn`), Port/BaseUrl read-only,
new tab + grouped UI + connections info block, dedupe map, tests.
Out of scope: editing flow itself (Edit/Reset/Save stays), new config keys,
the MCP/Skills tab (own spec).

## 2. Requisitos

### RF-001 — Catalog metadata: `Group` + `ManagedIn`

`CatalogEntry` gains `string Group` and `string? ManagedIn`.
`ConfigurationEntryDto` carries both through.

**Dedupe map** (`ManagedIn` set → hidden from the Configuration table):

| Key | Managed in |
|---|---|
| `Taskboard:Agents:DefaultPrompt` | `/agents` (Prompt tab) |
| `Taskboard:Terminal:Enabled` | `/settings?tab=general` (Features) |
| `Taskboard:WebCliAgent:Enabled` | `/settings?tab=general` (Features) |
| `Taskboard:Skills:Repository` | `/settings?tab=integrations` (Agent Skills) |
| `Taskboard:Rag:ServerName`, `Taskboard:Rag:Url`, `Taskboard:Rag:ApiKey` | `/settings?tab=integrations` (RAG / Knowledge MCP) |
| `Taskboard:Chat:SearchBackend`, `:SearchUrl`, `:SearchApiKey` | `/settings?tab=chat` (Web search) |
| `Taskboard:Chat:DefaultChatModel`, `:DefaultCodeModel`, `:DefaultImageModel` | `/settings?tab=chat` (Capability defaults) |
| `Taskboard:Chat:Capabilities:Disabled` | `/settings?tab=chat` (Capabilities) |
| `Taskboard:Chat:Mcp:Enabled`, `:Servers`, `:CallTimeoutSeconds` | `/settings?tab=mcp-skills` (SPEC-20261010-mcp-skills-hub) |

**Groups for the remaining entries:**

| Group | Keys |
|---|---|
| `Connections` | `Taskboard:DataDir`, `Taskboard:Database:ConnectionStringName`, `ConnectionStrings:Taskboard`, `Taskboard:Cache:*` (4) |
| `Server` | `Taskboard:Port`, `Taskboard:BaseUrl`, `AllowedHosts`, `Admin:Username` |
| `Logging` | `Logging:LogLevel:Default`, `Logging:LogLevel:Microsoft.AspNetCore` |
| `Chat` | `Taskboard:Chat:Tools:Enabled`, `:MaxToolIterations`, `:Skills:Enabled`, `:AgentDelegation:Enabled`, `Taskboard:AiChat:DefaultMode` |
| `Security` | `Taskboard:ApiKey` |

### RF-002 — Port/BaseUrl read-only

Catalog flips `Editable: true → false` on both keys with
`ReadOnlyReason: "Server binding — change via HARNESS_PORT/HARNESS_URL or appsettings.json."`
(RequiresRestart stays `true`; `EnvAlias` unchanged). Any stored DB override
keeps resolving — the key just can't be written from the UI/API anymore;
`SetOverrideAsync`/`PUT configuration/{key}` returns the existing
`CONFIG_NOT_EDITABLE` path for them.

### RF-003 — Connections info header

Read-only block at the top of the Configuration tab:

- **Database** row: provider badge (`SQLite` / `PostgreSQL`) detected from the
  effective `ConnectionStrings:<ConnectionStringName>` value
  (`Host=`/`Npgsql` → postgres; `Data Source=`/`*.db` → sqlite), the
  connection-name key and the (masked when secret) connection string.
- **Cache** row: badge `Redis (L1+L2)` when
  `Taskboard:Cache:Redis:ConnectionString` is set, else `In-memory (L1)`;
  `InstanceName` shown when Redis is on.
- Detection lives in a pure helper `ConnectionInfoResolver` (Application —
  unit-testable): `Resolve(entries) → { DbProvider, CacheMode }`.

### RF-004 — Configuration tab UI

- `SettingsTabs` becomes `general, configuration, integrations, agents, chat, mcp-skills, security` (the `mcp-skills` entry lands with
  SPEC-20261010-mcp-skills-hub; order recorded here for both specs).
- The Configuration pane renders one `form-section` per `Group` in the order
  above; each section has a title + 1-line description; the per-key rows keep
  the current Edit/Reset/read-only affordances (value, Source badge,
  `restart` badge) — grouped, not redesigned.
- General tab keeps only **Features** (the switches stay — they're the
  dedicated UI for the two toggle keys).
- Below the table: `<details>` "Managed in other screens" listing each
  `ManagedIn` key → deep link.
- `?tab=configuration` deep link works via the existing `OnAfterRender`
  mechanism.

## 3. Arquitetura

```
RuntimeConfigurationService.Catalog
   └─ CatalogEntry(Key, Default, Editable, RequiresRestart, ReadOnlyReason,
                   EnvAlias, Validate, Group, ManagedIn)     ← +2 campos
        └─ ConfigurationEntryDto (+Group, +ManagedIn)
              └─ GET /api/local/configuration
                     └─ Settings.razor "configuration" pane
                          ├─ Connections info header (ConnectionInfoResolver)
                          ├─ sections por Group (Edit/Reset/read-only)
                          └─ "Managed in other screens" (ManagedIn links)
```

Files touched: `RuntimeConfigurationService` (+ `CatalogEntry`, dto),
`ConfigurationEntryDto`, `Settings.razor` (+ tab, pane, sections, managed
note), new `Application/Configuration/ConnectionInfoResolver.cs`,
`TaskboardClient` (dto shape unchanged client-side), tests.

## 4. Config

Nenhuma chave nova. Mudanças de comportamento do catálogo: `Taskboard:Port` e
`Taskboard:BaseUrl` passam a read-only (RF-002).

## 5. Segurança

- `Taskboard:Port`/`BaseUrl`/`Admin:Username`/`DataDir`/connection strings:
  read-only impede que um admin logado altere binding/caminhos via UI —
  endurece a superfície (hoje só env/appsettings).
- Connection string e `Taskboard:ApiKey` seguem mascarados (`IsSecret`) —
  o info header exibe provider/modo, nunca o valor em claro.
- `ManagedIn` são rotas internas constantes — sem redirect injetável.

## 6. Testes

Unit (pt-BR `Dado_Quando_Entao`):

- `RuntimeConfigurationServiceGroupTests`: `GetEntries` carrega `Group`/
  `ManagedIn` corretos; `SetOverrideAsync("Taskboard:Port")` → rejeitado
  (`CONFIG_NOT_EDITABLE`); entries com `ManagedIn` mapeiam a rota esperada.
- `ConnectionInfoResolverTests`: `Data Source=...db` → `sqlite`; `Host=...`
  → `postgres`; connstring Redis set → `Redis (L1+L2)`, vazia → `In-memory`.
- Razor-source guard (`SettingsConfigurationTabTests`): `data-tab="configuration"`,
  seções por grupo presentes, `Taskboard:Agents:DefaultPrompt` /
  `Terminal:Enabled` / `WebCliAgent:Enabled` / `Rag:*` / `Chat:Search*` /
  `DefaultChatModel` / `Chat:Mcp:*` **ausentes** da tabela, presentes no
  bloco "Managed in other screens"; Port/BaseUrl renderizam `read-only`.

Integration: `GET /api/local/configuration` → dto com `group`/`managedIn`;
`PUT configuration/Taskboard:Port` → 409/4xx.

## 7. Fases

- **P1**: RF-001 + RF-002 (catalog metadata + read-only) + RF-004 sem o info
  header — a aba já fica dedicada e deduplicada.
- **P2**: RF-003 (Connections info header + `ConnectionInfoResolver`).

## Acceptance criteria

- `/settings?tab=configuration` abre a aba dedicada; General mostra apenas
  Features.
- `Taskboard:Port` e `Taskboard:BaseUrl` exibem badge read-only e não têm
  Edit; `PUT` neles retorna erro de não-editável.
- O bloco Connections mostra `SQLite` (ou `PostgreSQL` se configurado) e
  `In-memory`/`Redis` corretamente, com as keys visíveis.
- Nenhuma das keys do mapa de deduplicação aparece na tabela; todas aparecem
  com link em "Managed in other screens".
- Cada seção agrupa só suas keys, na ordem Connections → Server → Logging →
  Chat → Security.

## Open questions

1. `ManagedIn` esconder vs. listar somente-leitura com link — proposto esconder
   + bloco de links; se achar confuso "sumir", P3 pode exibi-las collapsed.
2. Provider de DB hoje é sempre SQLite — o resolver já reconhece Postgres
   para quando o backend suportar; ok antecipar ou deixar só sqlite por hora?
   (proposto: antecipar — custo ~10 linhas.)
3. `Taskboard:Chat:Skills:Enabled`/`AgentDelegation:Enabled`/`Tools:Enabled`
   ficam na Configuration (sem UI dedicada) ou viram switches na aba Chat —
   proposto: ficam.
