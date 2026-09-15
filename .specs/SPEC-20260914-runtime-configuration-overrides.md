# SPEC-20260914-runtime-configuration-overrides

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `runtime-configuration-overrides` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor Server / EF Core SQLite` |
| Repository | `afonsoft/taskboard-ai` |
| Branch | `feature/runtime-configuration-overrides` (aprovado pelo usuário) |
| Ticket | `user-request-2026-09-14` |
| Status | `Done` |

## 1. User Story

**As a** Taskboard operator/admin
**I want** every configuration value from `appsettings.json` and environment variables visible and editable on the Settings screen, with database-stored values taking precedence
**So that** I can tune the running system without editing files or restarting into a different environment — defaults come from appsettings/env, but once a value exists in the database it wins.

**Problem context:**
Today configuration is scattered: `TaskboardOptions`/`AdminOptions` bind `appsettings.json`, a handful of `TASKBOARD_*` env vars override specific keys (SPEC-20260914-env-var-precedence, Done), and only `Theme`/`GitHubToken` persist in `UserPreference`. There is no way to see the effective value of a key, where it came from, or override it at runtime. Requested precedence (confirmed by user): **DB override > env var > appsettings > built-in default**.

**Dependency:** mutating endpoints keep `.RequireAuthorization()`; the Blazor `TaskboardClient` must already forward the auth cookie — RF-003 of `SPEC-20260914-blazor-ui-critical-fixes`. If that SPEC lands later, the new UI section must degrade gracefully (show error, not crash).

## 2. Scope

**In scope:**
- Generic `ConfigurationOverride` persistence (new EF entity + migration) storing `Key`/`Value`/`UpdatedAt`.
- `SqliteConfigurationProvider` (`IConfigurationProvider`) registered **last** on `builder.Configuration` so DB overrides win over env vars and appsettings; reload fired after EF migrations and after every write.
- Endpoints `GET /api/configuration`, `PUT /api/configuration/{key}`, `DELETE /api/configuration/{key}` with per-key validation and secret masking.
- Settings screen: new "Configuration" section listing every known key with effective value, source badge (`db` / `env` / `appsettings` / `default`), inline edit, and reset-to-default.

**Out of scope:**
- Changing the authentication model or `admin.json` lifecycle.
- Overriding `Taskboard:DataDir`, `Taskboard:Database:ConnectionStringName`, `ConnectionStrings:*`, `Admin:*` via DB — chicken-and-egg (the DB cannot decide where it lives) or owned by the admin-seed flow. Shown read-only.
- Editing secrets like `GitHubToken` inside the grid (existing dedicated field stays); grid shows them masked.
- Per-user configuration — overrides are system-wide (single-user local-first app).
- `.github/workflows/**` changes.

## 3. Technical Context

**Where the change happens:** `src/Taskboard.Integrations/Configuration` (new provider), `src/Taskboard.Domain/Entities` (new entity), `src/Taskboard.EntityFrameworkCore` (config + migration), `src/Taskboard.Server/Program.cs` (provider registration, reload after `MigrateAsync`, endpoints), `src/Taskboard.Application*` (service + DTOs), `src/Taskboard.Blazor` (Settings UI + `TaskboardClient`).

**Key facts (evidence gathered):**
- `TaskboardOptions`: `Port` (default 47823), `DataDir` (default `.data`), `Database:ConnectionStringName`.
- `TaskboardEnvironment`: env-first for `TASKBOARD_PORT`/`TASKBOARD_DATA_DIR`/`ASPNETCORE_URLS`.
- `AdminUser`: `admin.json` > `TASKBOARD_ADMIN_*` > `Admin:*` — `Admin:*` keys are read-only here.
- Other consumed keys: `Taskboard:BaseUrl`/`TASKBOARD_URL` (MCP/CLI), `Logging:LogLevel:*`, `AllowedHosts`, `GITHUB_TOKEN` (GitHubService; already editable via `UserPreference.GitHubToken`).
- Provider must read the SQLite file **directly** (`Microsoft.Data.Sqlite`), not via `TaskboardDbContext` — configuration is built before the container exists; the file path is resolved only from env/appsettings (DataDir is never a DB-overridable key). Missing DB/table → provider yields nothing.
- Registered last on `builder.Configuration`, the provider loads during config build — **before `UseUrls`** — so a DB `Taskboard:Port` override applies on next startup.
- `Logging` honors `IConfiguration` change tokens natively → hot-reload for `Logging:*` keys when the provider calls `OnReload()`.

**Files to read before implementing:**
- `AGENTS.md` · `.specs/SPEC-20260910-system-configuration.md` · `.specs/SPEC-20260914-env-var-precedence.md` · `.specs/SPEC-20260914-blazor-ui-critical-fixes.md` (RF-003)
- `src/Taskboard.Server/Program.cs` (config build, `AddTaskboardEntityFrameworkCore`, migrations, settings endpoints)
- `src/Taskboard.Application.Contracts/Configuration/TaskboardOptions.cs` · `TaskboardEnvironment.cs` · `AdminOptions.cs`
- `src/Taskboard.Application/Settings/SettingsService.cs` · `src/Taskboard.Domain/Entities/UserPreference.cs`
- `src/Taskboard.EntityFrameworkCore` (DbContext + configurations + migrations layout)
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` · `src/Taskboard.Blazor/Services/TaskboardClient.cs`
- `tests/Taskboard.Tests.Unit/Configuration/` · `tests/Taskboard.Tests.Integration/ServerEndpointsTests.cs`

**Files to create or modify:**
```text
src/Taskboard.Domain/Entities/ConfigurationOverride.cs            # NOVO: Key, Value, UpdatedAt
src/Taskboard.EntityFrameworkCore/Configurations/ConfigurationOverrideConfiguration.cs  # NOVO: unique index on Key
src/Taskboard.EntityFrameworkCore/Migrations/*_ConfigurationOverrides.*  # NOVO (dotnet ef)
src/Taskboard.Integrations/Configuration/SqliteConfigurationProvider.cs  # NOVO: provider + source
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs   # NOVO: effective value + source + validate
src/Taskboard.Application.Contracts/Configuration/ConfigurationEntryDto.cs # NOVO
src/Taskboard.Server/Program.cs                                   # provider registration, reload, 3 endpoints
src/Taskboard.Blazor/Services/TaskboardClient.cs                  # + Get/Put/Delete configuration
src/Taskboard.Blazor/Components/Pages/Settings.razor              # seção Configuration (tabela + edit + reset)
tests/Taskboard.Tests.Unit/Integrations/SqliteConfigurationProviderTests.cs  # NOVO
tests/Taskboard.Tests.Unit/Application/RuntimeConfigurationServiceTests.cs   # NOVO
tests/Taskboard.Tests.Integration/ServerEndpointsTests.cs         # testes GET/PUT/DELETE configuration
```

## 4. Requirements

### RF-001: Entidade e tabela de overrides
- **Description:** nova entidade `ConfigurationOverride` (`Id` Guid, `Key` string única, `Value` string, `UpdatedAt` DateTime) mapeada para tabela `ConfigurationOverrides` com índice único em `Key`; migration EF criada via `dotnet ef migrations add`.
- **Input → Output:** `PUT` de `Logging:LogLevel:Default=Debug` → linha persistida.

### RF-002: SqliteConfigurationProvider com precedência DB > env > appsettings
- **Description:** `ConfigurationProvider` + `IConfigurationSource` que abre `taskboard.sqlite` em modo read, lê `ConfigurationOverrides` (tabela ausente → vazio) e popula o dicionário de configuração. Registrado como **último** provider em `builder.Configuration.Sources`, portanto vence `EnvironmentVariables` e `Json` (appsettings). Após `dbContext.Database.MigrateAsync()` o host chama `provider.Reload()` → `OnReload()` propaga change token.
- **Rules:** caminho do DB resolvido exclusivamente via `GetDataDir()` (env/appsettings) — `Taskboard:DataDir` jamais pode vir do próprio DB; falha de leitura (DB inexistente, lock) → provider vazio, nunca crash no boot; nenhum `Key`/`Value` em logs.
- **Input → Output:** DB tem `Taskboard:Port=5000` + `TASKBOARD_PORT=6000` + appsettings `47823` → `IConfiguration["Taskboard:Port"] == "5000"`.

### RF-003: Catálogo de chaves e editabilidade
- **Description:** `RuntimeConfigurationService` expõe o catálogo: `Taskboard:Port`, `Taskboard:BaseUrl`, `AllowedHosts`, `Logging:LogLevel:Default`, `Logging:LogLevel:Microsoft.AspNetCore` (editáveis); `Taskboard:DataDir`, `Taskboard:Database:ConnectionStringName`, `ConnectionStrings:Taskboard`, `Admin:Username` (read-only, com `reason`). Cada entrada retorna `key`, `effectiveValue`, `source` (`db|env|appsettings|default`), `editable`, `requiresRestart`, `masked`.
- **Rules:** `Logging:*` → `requiresRestart=false`; `Taskboard:Port`, `Taskboard:BaseUrl`, `AllowedHosts` → `requiresRestart=true`; chaves casadas com `*Password*|*Token*|*ConnectionString*` → `masked=true` e `effectiveValue` truncado (ex.: `••••` + últimos 4) — nunca valor pleno.
- **Input → Output:** `GET /api/configuration` → lista tipada acima.

### RF-004: Validação por chave no PUT
- **Description:** `PUT /api/configuration/{key}` com `{ "value": "..." }`: chave não-editável → `400` com `error.code=KEY_READ_ONLY`; `Taskboard:Port` não-int ou fora de 1-65535 → `400`; `Logging:*` fora de `Trace|Debug|Information|Warning|Error|Critical|None` → `400`; `Taskboard:BaseUrl` não-URI absoluto http(s) → `400`; `AllowedHosts` vazio → `400`. Sucesso → upsert + `provider.Reload()` → `204`.
- **Input → Output:** `PUT .../Taskboard:Port {value:"99999"}` → `400 VALIDATION`; `PUT .../Logging:LogLevel:Default {value:"Warning"}` → `204` e próximo `GET` mostra `source=db`.

### RF-005: Reset de override
- **Description:** `DELETE /api/configuration/{key}` remove a linha → `provider.Reload()` → `204`; chave inexistente → `404`; chave read-only → `400`.
- **Input → Output:** delete de override existente → `GET` volta a reportar `source=env|appsettings|default`.

### RF-006: Autenticação e masking nos endpoints
- **Description:** `GET`, `PUT`, `DELETE` de `/api/configuration*` com `.RequireAuthorization()` (mutações + valores efetivos são sensíveis). Respostas de erro no formato `{ error: { code, message } }`.
- **Input → Output:** request sem auth → `401`; com auth → fluxo normal.

### RF-007: UI — seção Configuration no Settings
- **Description:** nova seção na página Settings: tabela `key | effective value | source badge | requires restart | ações (edit/reset)`. Edição inline (input + save/cancel); `reset` chama `DELETE` após confirmação; valores `masked` exibidos mascarados e não editáveis se read-only. Erros de API → toast/mensagem inline (sem estourar `ErrorBoundary`). Após salvar chave `requiresRestart`, banner/toast "applies on next restart".
- **Input → Output:** editar `Logging:LogLevel:Default` para `Warning` → `PUT` → linha mostra `source=db`; reset → `source` volta.

**Business rules / invariants:**
- `Taskboard:DataDir`, `ConnectionStrings:*`, `Taskboard:Database:ConnectionStringName`, `Admin:*` nunca persistem override.
- Valor no DB sempre vence env/appsettings; ausência de linha = default.
- Nenhum segredo (conn string, tokens, senhas) aparece em claro na API ou na UI.
- Leitura do provider nunca lança — DB ausente/locked = sem overrides.

## 5. API Contract

**Endpoint:** `GET /api/configuration`
**Auth:** cookie admin (`.RequireAuthorization()`)

**Response (success):**
```json
{
  "entries": [
    {
      "key": "Taskboard:Port",
      "effectiveValue": "5000",
      "source": "db",
      "editable": true,
      "requiresRestart": true,
      "masked": false,
      "readOnlyReason": null
    },
    {
      "key": "ConnectionStrings:Taskboard",
      "effectiveValue": "••••lite",
      "source": "appsettings",
      "editable": false,
      "requiresRestart": true,
      "masked": true,
      "readOnlyReason": "Cannot be stored in the database it configures."
    }
  ]
}
```

**Endpoint:** `PUT /api/configuration/{key}` — body `{ "value": "Warning" }` → `204`
**Endpoint:** `DELETE /api/configuration/{key}` → `204` / `404`

**Expected errors:** `400 KEY_READ_ONLY | VALIDATION`, `401`, `404 OVERRIDE_NOT_FOUND` — formato `{ "error": { "code": "...", "message": "..." } }`.

## 6. Acceptance Criteria

- [ ] **Given** `TASKBOARD_PORT=6000` e linha `Taskboard:Port=5000` no DB **when** o host sobe **then** `GetPort()`/`IConfiguration["Taskboard:Port"]` resolve `5000` e o Kestrel binda 5000.
- [ ] **Given** DB vazio e `Taskboard:Port=47823` no appsettings **when** `GET /api/configuration` **then** a entrada mostra `source=appsettings`.
- [ ] **Given** `PUT .../Logging:LogLevel:Default {value:"Warning"}` autenticado **when** executado **then** `204`, `source=db` no próximo GET, e o nível de log muda sem restart.
- [ ] **Given** `PUT .../Taskboard:DataDir {value:"/x"}` **when** executado **then** `400 KEY_READ_ONLY` e nada persiste.
- [ ] **Given** `PUT .../Taskboard:Port {value:"abc"}` **when** executado **then** `400 VALIDATION`.
- [ ] **Given** override existente **when** `DELETE /api/configuration/{key}` **then** `204` e o GET volta a `source=env|appsettings`.
- [ ] **Given** request sem cookie **when** qualquer endpoint `/api/configuration*` **then** `401`.
- [ ] **Given** `ConnectionStrings:Taskboard` listada **when** GET **then** `masked=true` e valor truncado.
- [ ] **Given** DB inexistente/locked no boot **when** o provider carrega **then** aplicação sobe normalmente sem overrides.
- [ ] **Given** a tela Settings autenticada **when** carrega **then** a seção Configuration lista todas as chaves do catálogo com badges de fonte corretos; editar/reset funcionam sem `ErrorBoundary`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Key desconhecida (não no catálogo) | `PUT /api/configuration/Foo:Bar` | `400 VALIDATION` (catálogo é closed-set) |
| Override idêntico ao default | `PUT Port {value:"47823"}` | persiste mesmo assim (`source=db`) — ou rejeita; escolha do implementador, documentar |
| `admin.json` já existe + `Admin:Username` no DB | tentativa de override | read-only, `400 KEY_READ_ONLY` |
| Env var `Taskboard__Port` + DB override | ambos setados | DB vence |
| Settings sem cookie-forward (SPEC anterior não mergeada) | UI carrega | seção mostra erro inline, restante da página funciona |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** reler seção 3; confirmar ordem dos sources em `builder.Configuration` e ponto de `MigrateAsync`.
- [ ] **T2 — Domain/EF:** `ConfigurationOverride` + configuração + `dotnet ef migrations add`.
- [ ] **T3 — Provider:** `SqliteConfigurationProvider`/`Source` em `Taskboard.Integrations`; registrar por último; `Reload()` público; chamada após `MigrateAsync`.
- [ ] **T4 — Service:** `RuntimeConfigurationService` (catálogo, effective+source, validação por chave, masking).
- [ ] **T5 — Endpoints:** GET/PUT/DELETE `/api/configuration*` com `.RequireAuthorization()`; reload após write.
- [ ] **T6 — Client+UI:** `TaskboardClient` methods; seção Configuration no `Settings.razor` (tabela, edit inline, reset, toasts, banner restart).
- [ ] **T7 — Tests:** unit — provider (precedência, DB ausente, reload) e service (catálogo/validação/masking); integração — GET 401/200, PUT valid/invalid/read-only, DELETE, round-trip.
- [ ] **T8 — Validation:** `dotnet build`, `dotnet test`, smoke manual (subir com override de Port no DB → bind correto).
- [ ] **T9 — Done + PR:** DoD → `Status = Done` → PR em `feature/runtime-configuration-overrides`.

**7.1 Validation strategy**

Feature .NET: unit tests (provider, service) + integration tests (endpoints); cobertura não pode regredir abaixo do gate atual (45%); `dotnet build` com `TreatWarningsAsErrors` limpo; smoke test manual do boot com override de `Taskboard:Port`.

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** nunca `main`/`master`/`develop`; usar `feature/runtime-configuration-overrides`.
- **Workflows:** `.github/workflows/**` intocáveis.
- **Security:** valores `*Password*|*Token*|*ConnectionString*` sempre mascarados; nenhum secret em log; sem `.env` em commit; provider trata leitura como best-effort (sem credenciais em erros).
- **Scope:** closed-set de chaves editáveis — não expor override de `DataDir`/conn string/admin.
- **Architecture:** resolução de fonte/validação no service (Application); provider é infra pura; componentes Blazor só falam com `TaskboardClient`.
- **Specs:** novo contrato `GET/PUT/DELETE /api/configuration*` documentado aqui; refletir em `docs/` se `SPEC-002` listar a superfície de API.

## 9. Definition of Done

- [ ] RF-001…RF-007 implementados.
- [ ] Todos os ACs cobertos por teste ou evidência manual.
- [ ] Edge cases tratados.
- [ ] `dotnet build` limpo, `dotnet test` verde, cobertura sem regressão.
- [ ] Guardrails respeitados; secrets mascarados; erros no formato genérico.
- [ ] Boot com DB override de `Taskboard:Port` comprovado em smoke test.

**Next action after DoD is complete:** `Status = Done` e PR em `feature/runtime-configuration-overrides`.

## Open Questions / Pending Ambiguity

- Nenhuma — usuário confirmou: provider custom (precedência DB>env>appsettings), grupos editável/read-only conforme Q3, endpoints + seção na Settings, branch `feature/runtime-configuration-overrides`.
