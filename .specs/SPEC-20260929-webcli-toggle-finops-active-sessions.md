# SPEC-20260929-webcli-toggle-finops-active-sessions

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `webcli-toggle-finops-active-sessions` |
| Type | `Bugfix` (Frontend + API + Background jobs) |
| Stack | `.NET 10 / ASP.NET Core Minimal APIs / EF Core SQLite / Blazor WASM / C# 14` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260929-webcli-toggle-finops-active` |
| Ticket | [#390](https://github.com/afonsoft/agent-harness/issues/390) |
| Status | `Done` — delivered in PR [#391](https://github.com/afonsoft/agent-harness/pull/391) |

## 1. User Story

**As a** Harness operator
**I want** the Web CLI Agent feature enabled by default with a dedicated
toggle in Settings, and the FinOps "No active sessions" alert to reflect
real agent activity
**So that** the AI Code page works out of the box (with a visible off switch),
and the FinOps dashboard does not report false inactivity while my agent CLIs
(e.g. Devin) have live sessions.

**Problem context:**

1. **Web CLI Agent disabled by default with poor discoverability.** The
   interactive agent endpoints of the AI Code page (`/local/ai/threads/{id}/prompt`,
   `/queue`, `/retry`, `/cancel`, `/permissions/{id}/reply`) are gated by
   `Taskboard:WebCliAgent:Enabled`, whose catalog default is `"false"`
   (`RuntimeConfigurationService`). Any prompt returns
   `404 { code: "FEATURE_DISABLED", message: "Web CLI Agent feature is disabled." }`.
   The key is technically editable, but only as a raw free-text row in the
   generic Settings → Configuration table — no switch, no feature grouping,
   no `HARNESS_*` env alias (every other boolean feature key has one).
2. **FinOps false "No active sessions" alert.** `FinOpsService.BuildAlerts`
   counts a session as active only when
   `(EndedAtUtc ?? StartedAtUtc) >= now - ActiveWindowSeconds (1800s)`.
   An **open** session (`EndedAtUtc == null`) that started more than 30 minutes
   ago — the normal case for a live Devin session — falls back to the stale
   `StartedAtUtc` and is never counted. Harness runs currently executing
   (`AgentRun.State == Running`) are not counted either. The same stale
   fallback drives the `running`/`finished` badge in the recent-sessions table.
3. **Devin extractor never refreshes open sessions.** `DevinCliDbExtractor`
   scans `~/.local/share/devin/cli/sessions.db` incrementally by `rowid`
   (watermark cursor). After first ingest, an open session's
   `last_activity_at` (mapped to `EndedAtUtc`) is **never re-read**, so even a
   healthy sync loop cannot make a live session look active. The alert is
   therefore structurally false for long-running Devin sessions.

## 2. Scope

**In scope:**

- Flip `Taskboard:WebCliAgent:Enabled` catalog default to `"true"` and add the
  env alias `HARNESS_WEB_CLI_AGENT_ENABLED` (precedence unchanged:
  db > env alias > `Taskboard__*` env > appsettings > default).
- New **Features** section in `Settings.razor` with form-switch toggles for the
  boolean feature keys (`Taskboard:WebCliAgent:Enabled`,
  `Taskboard:Terminal:Enabled`), persisted through the existing
  `PUT /api/local/configuration/{key}` endpoint — no restart required
  (`RequiresRestart: false` for both).
- FinOps liveness rule (`BuildAlerts` + `BuildRecentSessions`): a session is
  active when `EndedAtUtc >= windowStart`, **or** it is open
  (`EndedAtUtc == null`) and (`StartedAtUtc >= windowStart` **or**
  `IngestedAtUtc >= windowStart`); a harness `AgentRun` with
  `State == Running` is always active. The recent-sessions `running` badge
  uses the same rule.
- `DevinCliDbExtractor` re-reads **recently-touched sessions** on every pass —
  rows with `last_activity_at IS NULL OR last_activity_at >= now - 24h` —
  in addition to the incremental `rowid` scan, so open sessions refresh
  `EndedAtUtc`/`IngestedAtUtc` on each sync tick.
- Unit + integration tests (pt-BR BDD names) for the alert rule, the session
  status rule, the extractor refresh query and the catalog default/env alias.

**Out of scope:**

- Changing the other gated endpoints' error contract (`FEATURE_DISABLED`
  stays; only the default value and the toggle surface change).
- New ingestion sources (claude-code JSONL, copilot, devin-desktop ACP, Devin
  API) — owned by the future `cli-file-reader` spec.
- Real-time/SSE updates on `/finops`; alert severity changes
  (`NoActiveSessions` stays `warn`).
- Making `Taskboard:Terminal:Enabled` default-off/on — only its toggle surface
  moves into the Features section (default stays `true`).
- Renaming internal `Taskboard:*` keys (branding rule: product surface only).

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Application` — `Configuration/RuntimeConfigurationService.cs`
  (catalog default + env alias); `Harness/FinOpsService.cs` (alert + session
  status liveness).
- `Taskboard.Integrations` — `CliDb/Extractors/DevinCliDbExtractor.cs`
  (refresh-window re-read of open sessions).
- `Taskboard.Blazor` — `Components/Pages/Settings.razor` (Features section
  with switches, reusing the existing config load/save methods).
- `Taskboard.Server` — no endpoint changes; `Program.cs` gates keep reading
  `IConfiguration` per request (DB override provider already flows through).

**Files to read before implementing:**

- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` —
  catalog entries, `ResolveValue`/`ResolveSource` precedence.
- `src/Taskboard.Server/Program.cs` (~lines 1495-1620) —
  `Taskboard:WebCliAgent:Enabled` gates; ~line 1736 — configuration endpoints.
- `src/Taskboard.Application/Harness/FinOpsService.cs` — `BuildAlerts`
  (~line 427), `BuildRecentSessions` (~line 344).
- `src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs` and
  `CliDb/CliDbExtractorBase.cs` — cursor/watermark and rollup conventions.
- `src/Taskboard.Domain/Entities/CliMetrics/CliSessionMetric.cs` —
  `Update()` re-ingest path (refreshes `IngestedAtUtc`).
- `src/Taskboard.EntityFrameworkCore/CliMetrics/EfCoreCliMetricsRepository.cs`
  — `UpsertSessionsAsync` upsert semantics.
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — Agents toggles
  pattern (`_enabledState`, `ToggleAgent`) and Configuration table.
- `.specs/SPEC-20260919-web-cli-agent.md`,
  `.specs/SPEC-20260922-finops-dashboard-detail.md`,
  `.specs/SPEC-20260919-cli-metrics.md`.

**Files to create or modify:**

```text
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs        [mod]
src/Taskboard.Application/Harness/FinOpsService.cs                            [mod]
src/Taskboard.Integrations/CliDb/Extractors/DevinCliDbExtractor.cs            [mod]
src/Taskboard.Blazor/Components/Pages/Settings.razor                          [mod]
tests/Taskboard.Tests.Unit/Harness/FinOpsServiceTests.cs                      [mod]
tests/Taskboard.Tests.Unit/Configuration/RuntimeConfigurationServiceTests.cs  [mod — if exists, else new]
tests/Taskboard.Tests.Unit/CliDb/DevinCliDbExtractorTests.cs                  [mod — if exists, else new]
tests/Taskboard.Tests.Integration/FinOpsEndpointsTests.cs                     [mod]
```

## 4. Requirements

### RF-001: Web CLI Agent habilitado por padrão
- **Description:** The system must ship `Taskboard:WebCliAgent:Enabled` with
  catalog default `"true"` and env alias `HARNESS_WEB_CLI_AGENT_ENABLED`.
- **Rules:** precedence stays db > env alias > `Taskboard__*` env >
  appsettings > default; an existing DB override (value `false`) still wins —
  the default change only affects installs without an override.
- **Input → Output:** fresh install, no overrides → AiChat agent prompts are
  admitted (no `FEATURE_DISABLED`).

### RF-002: Seção "Features" no Settings
- **Description:** Settings must render a dedicated **Features** section with
  one form-switch per boolean feature key, placed before the Configuration
  table.
- **Rules:** keys covered: `Taskboard:WebCliAgent:Enabled`,
  `Taskboard:Terminal:Enabled`; each toggle shows the effective value, the
  source badge (`db`/`env`/`appsettings`/`default`) and persists via
  `SetConfigurationValueAsync` / `DeleteConfigurationValueAsync` (Reset);
  toggling a key whose source is `db` to its default value may either persist
  the value or reset the override — persist is acceptable; no restart required
  for either key; keys remain listed in the Configuration table (single source
  of truth, no duplication of values).

### RF-003: Regra de liveness do FinOps
- **Description:** `FinOpsService` must count as **active** (for the
  `NoActiveSessions` alert and the recent-sessions `running` badge):
  1. a `CliSessionMetric` with `EndedAtUtc >= windowStart`; or
  2. an open `CliSessionMetric` (`EndedAtUtc == null`) with
     `StartedAtUtc >= windowStart` **or** `IngestedAtUtc >= windowStart`; or
  3. a `RunCostMetric` with `RecordedAtUtc >= windowStart`; or
  4. an `AgentRun` with `State == AgentRunState.Running`.
- **Rules:** `windowStart = now - FinOpsOptions.ActiveWindowSeconds` (default
  1800s); the alert text keeps the configured window in minutes; closed
  sessions whose `EndedAtUtc` is older than the window are never active.
- **Input → Output:** open Devin session started 2h ago, re-ingested 5 min ago
  → counted active → no `NoActiveSessions` alert; badge `running`.

### RF-004: Refresh de sessões abertas no extrator Devin
- **Description:** `DevinCliDbExtractor` must, on every extraction pass,
  additionally re-read sessions with
  `last_activity_at IS NULL OR last_activity_at >= now - 24h`
  (refresh window constant), regardless of the `rowid` watermark.
- **Rules:** the incremental `rowid > @cursor` scan for history is unchanged;
  refreshed rows flow through the normal upsert (`row.Update`), which
  refreshes `EndedAtUtc` and `IngestedAtUtc`; the rollup estimation from
  `message_nodes` applies to refreshed rows as today; dedup by
  `(source, externalId)` prevents duplicates; the refresh query must stay
  scalar-only (no message content — privacy boundary).
- **Input → Output:** sync at T0 ingests session S (open, `last_activity_at`
  null); sync at T0+15min re-reads S with updated `last_activity_at` →
  `CliSessionMetric.EndedAtUtc` and `IngestedAtUtc` refreshed.

### RF-005: Testes
- **Description:** Every RF must be covered by tests named in pt-BR
  (`Dado_Quando_Entao`), unit level for the liveness rule and the extractor,
  integration level for the configuration write path driving the
  `FEATURE_DISABLED` gate.

## 5. API Contract

No new endpoints. Consumed (existing):

**Endpoint:** `GET /api/local/configuration` · `PUT /api/local/configuration/{key}` ·
`DELETE /api/local/configuration/{key}`
**Auth:** session cookie / API key (existing local API conventions).

`PUT` request: `{ "value": "true" }` → `200` on success; `400` validation
(non-boolean); `404` unknown key. The Features toggle only calls these —
no contract change.

Gated endpoints keep their current shape, e.g.:

```json
{ "error": { "code": "FEATURE_DISABLED", "message": "Web CLI Agent feature is disabled." } }
```

## 6. Acceptance Criteria

- [ ] **Dado** instalação nova sem overrides **quando** envio um prompt em modo agente no AI Code **então** a requisição é aceita (202) e nenhum `FEATURE_DISABLED` é retornado.
- [ ] **Dado** override `Taskboard:WebCliAgent:Enabled = false` no banco **quando** envio um prompt **então** retorna `404 FEATURE_DISABLED` (override vence o default).
- [ ] **Dado** a página Settings **quando** abro a seção Features **então** vejo switches para Web CLI Agent e Terminal com valor efetivo e badge de source.
- [ ] **Dado** o switch do Web CLI Agent **quando** desligo e salvo **então** um override `false` é persistido (`source = db`) e prompts passam a retornar `FEATURE_DISABLED` sem restart.
- [ ] **Dado** sessão Devin aberta iniciada há 2h e re-ingestada há 5min **quando** o summary do FinOps é calculado **então** nenhum alerta `NoActiveSessions` é emitido e a sessão aparece com badge `running`.
- [ ] **Dado** todas as sessões fechadas há mais de 30min e nenhum run em execução **quando** o summary é calculado **então** o alerta `NoActiveSessions` é emitido com o texto da janela configurada.
- [ ] **Dado** `AgentRun` com `State = Running` **quando** o summary é calculado **então** o alerta `NoActiveSessions` não é emitido.
- [ ] **Dado** sessão Devin aberta cujo `last_activity_at` mudou no vendor db **quando** o sync roda **então** `EndedAtUtc` e `IngestedAtUtc` são atualizados sem duplicar a sessão.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Override DB `false` + default `true` | config read | `false` wins (db precedence) |
| Sessão aberta nunca re-ingestada | `IngestedAtUtc` antigo, `StartedAtUtc` antigo | não é ativa (correto — sem sinal de atividade) |
| `last_activity_at` futuro (clock skew) | timestamp futuro no vendor db | clamp para now no ingest (regra existente) |
| Chave booleana inválida no PUT | `value = "yes"` | `400` validação (`ValidateBoolean`) |
| Refresh query em db sem `message_nodes` | rollup null | sessões com tokens null, sem quebrar o source |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler os arquivos da seção 3 e confirmar convenções (toggles de Agents em Settings.razor, cursor do extrator, testes existentes de FinOps).
- [ ] **T2 — RF-001:** catalog default `true` + `EnvAlias: "HARNESS_WEB_CLI_AGENT_ENABLED"` em `RuntimeConfigurationService`.
- [ ] **T3 — RF-002:** seção Features em `Settings.razor` (switches + save/reset reusando `LoadConfigurationAsync`/`SaveConfigAsync`/`ResetConfigAsync`).
- [ ] **T4 — RF-003:** regra de liveness em `BuildAlerts` e `BuildRecentSessions` (extrair helper compartilhado `IsSessionActive`).
- [ ] **T5 — RF-004:** refresh-window query no `DevinCliDbExtractor` (constante `RefreshWindowHours = 24`), mesclando resultados com o scan incremental antes do rollup.
- [ ] **T6 — RF-005:** testes unitários + integração (pt-BR), cobrindo todos os critérios da seção 6.
- [ ] **T7 — Verification:** `dotnet build` (TreatWarningsAsErrors) + `dotnet test` + `dotnet format --verify-no-changes`; cobrir cada critério de aceitação.
- [ ] **T8 — Done + PR:** DoD completo → `Status = Done` → PR na branch `feature/devin-20260929-webcli-toggle-finops-active`.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
|---|---|
| Bugfix / .NET | `dotnet build` sem warnings; `dotnet test` verde (unit + integration); novos testes cobrindo RF-001…RF-005; coverage gate ratchet respeitado (≥ 77%) |

## 8. Organization Guardrails

- Branch `feature/devin-20260929-webcli-toggle-finops-active`; **nunca** commit/push em `main`/`develop`.
- Não editar `.github/workflows/**`.
- Sem secrets no código/logs; `Taskboard:ApiKey` continua mascarada no Settings.
- Mudança de contrato: nenhuma rota nova; mudança de **default** de config é breaking-ish para quem dependia do default `false` — documentar no PR e em `docs/` (en-us + pt-br).
- Testes obrigatórios antes do merge (Hard Rule 5); nomes BDD em pt-BR.
- Privacidade: nenhuma query nova lê conteúdo de mensagens (apenas colunas escalares — `last_activity_at`, `id`, `title`).

## 9. Definition of Done

- [ ] RF-001…RF-005 implementados e cobertos por testes verdes.
- [ ] `dotnet build` limpo (TreatWarningsAsErrors) e `dotnet test` verde localmente.
- [ ] Critérios de aceitação (seção 6) todos verificados.
- [ ] `docs/features.md` + `docs/features.pt-br.md` atualizados (Features toggle + correção do alerta).
- [ ] SPEC `Status = Done` e PR aberto na branch da feature.
- [ ] Nenhum arquivo de workflow alterado; nenhum secret commitado.
