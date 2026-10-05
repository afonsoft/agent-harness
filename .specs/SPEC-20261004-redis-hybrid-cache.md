# SPEC-20261004-redis-hybrid-cache: HybridCache (L1 memory + L2 Redis)

## 0. SPEC Metadata

| Field | Value |
|---|---|
| Feature name | Redis HybridCache |
| Product / System | agent-harness (Harness) |
| Module / Bounded Context | Server infra (Taskboard.Server) + runtime config catalog |
| Change type | Infra / Feature |
| Repository | afonsoft/agent-harness |
| Suggested branch | `feat/devin-20261004-redis-hybrid-cache` |
| Technical owner | afonsoft |
| Status | Done |
| Date | 2026-10-04 |
| Target agent | Devin |
| Related SPECs | SPEC-008-frontend (§22 — Redis backplane como mitigação de SignalR scaling), SPEC-20260920-harness-maintenance-jobs (RF-003 — SpecDriftReportCache), SPEC-20260923-cockpit-run-hardening (padrão Taskboard:* options) |
| External refs | https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0 |

---

## 1. Executive Summary

### Problem

O Harness é single-node: restart do processo perde todo cache in-process — `SpecDriftReportCache` (singleton hand-rolled), snapshots de CLI probe, `UseOutputCache` in-memory. O endpoint `/api/specs/drift-report` precisa re-escanear ~100 arquivos de spec após cada boot. O #484 já introduziu `HybridCache` + `StackExchangeRedis` condicional (connstring flat `Taskboard:Cache:Redis`) para o catálogo de chat — esta spec completa a infraestrutura: schema de config completo, catálogo de runtime, wiring centralizado e um piloto (`SpecDriftReportCache`) que valida o caminho de ponta a ponta.

### Objective

Introduzir `Microsoft.Extensions.Caching.Hybrid` (`HybridCache`) como abstração única de cache — L1 in-process (MemoryCache) + L2 out-of-process (Redis via `IDistributedCache` quando `Taskboard:Cache:Redis:ConnectionString` configurada) — com stampede protection e invalidação por tags nativos. Entregar a infraestrutura mais um piloto real (`SpecDriftReportCache`) que valida o caminho de ponta a ponta.

**Decisões já tomadas (entrevista):**

- Consumidor: **HybridCache** (doc oficial Microsoft referenciada acima) — não `IDistributedCache` puro nem SignalR backplane.
- `Taskboard:Cache:Redis:ConnectionString` entra no catálogo do `RuntimeConfigurationService`: **Editable=true, RequiresRestart=true** (mascara automática por conter `ConnectionString` — `IsSecret`).
- Conjunto completo de chaves: `ConnectionString` + `InstanceName` + expirações default.
- Redis indisponível/mal configurado → **degrada para L1-only** (comportamento natural do HybridCache: falha de L2 vira miss + log, nunca crash).

**Delta pós-#484 (mergeado antes desta spec):**

- `AddHybridCache()`, `AddStackExchangeRedisCache` condicional e os 2 pacotes (`Microsoft.Extensions.Caching.Hybrid` 10.10.0, `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.9) já estão em `Program.cs`/`Directory.Packages.props` — RF-003 parcial e RF-006 já satisfeitos.
- A chave flat `Taskboard:Cache:Redis` (connstring) é substituída pelo schema aninhado desta spec (`Redis:ConnectionString` + `Redis:InstanceName`). Sem breaking: a config só existe via env/appsettings, nada persistido consumia a chave flat.
- O catálogo de chat (providers/models/agent-defs) já consome HybridCache desde o #484 — a spec só adiciona o piloto `SpecDriftReportCache`.

## 2. Scope

**In scope:**
- Novas chaves `Taskboard:Cache:*` no `appsettings.json`, no catálogo do `RuntimeConfigurationService` e nos overrides via `HARNESS__CACHE__*` env vars (mapeamento já existente em `Program.cs:110-120`).
- Registro de `HybridCache` no DI (sempre ativo, L1-only por padrão) + `AddStackExchangeRedisCache` condicional quando `ConnectionString` não-vazia.
- `HybridCacheOptions.DefaultEntryOptions` a partir de `Taskboard:Cache:DefaultExpiration` / `LocalCacheExpiration`.
- Piloto: `SpecDriftReportCache` migrado para fachada sobre `HybridCache`; `SpecDriftScanService` e endpoint `/api/specs/drift-report` atualizados.
- Pacotes NuGet `Microsoft.Extensions.Caching.Hybrid` + `Microsoft.Extensions.Caching.StackExchangeRedis` — já pinados em `Directory.Packages.props` desde o #484 (10.10.0 / 10.0.9); nenhum pacote novo.
- Testes unitários (catálogo + fachada) e integração (boot L1-only, endpoint 200).
- Documentação `docs/installation.md` (+`.pt-br.md`) / `docs/technologies.md` (+`.pt-br.md`).

**Out of scope:**
- SignalR Redis backplane (`AddSignalR().AddStackExchangeRedis`) — mitigação SPEC-008 fica para SPEC própria quando houver multi-node.
- `IDistributedCache` como consumidor direto fora do HybridCache; output cache Redis (`UseOutputCache` permanece in-memory).
- Migração dos demais caches (`CliProbeSnapshotService`, `DockerCliDiscovery`, `SkillsRepository` cache) — specs futuras.
- Redis Sentinel/Cluster/TLS além do que a connection string já expressa.
- Health check dedicado de Redis (`/healthz` já existe; ping Redis pode entrar em spec de observability).

## 3. Technical Context

Config pipeline: `appsettings.json` → env `Taskboard__*`/`HARNESS__*` (remapeadas para `Taskboard:*` em `Program.cs:110`) → `SqliteConfigurationProvider` (DB override vence). Catálogo editável: `RuntimeConfigurationService.Catalog` (`src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs:26-134`). Options pattern: `GetSection("Taskboard:X").Get<T>()` registrado como singleton (`PipelineAutoRetryOptions`, `FinOpsOptions` — `Program.cs:721-733`).

Cache hoje: `SpecDriftReportCache` (`src/Taskboard.Server/Services/SpecDriftReportCache.cs`) — singleton com `lock` + campo `_last`; consumido pelo endpoint `specs.MapGet("drift-report")` (`Program.cs:1467-1487`) que faz `driftCache.Last ?? await detector.BuildReportAsync(null, ct)`; populado por `SpecDriftScanService.RunJobAsync` (lê `_cache.Last` para diff de novos drifts, depois `_cache.Update(report)`).

HybridCache (doc Microsoft): `AddHybridCache()` registra a classe abstrata `HybridCache` — L1 `MemoryCache` + stampede protection sempre; L2 usa o `IDistributedCache` registrado, se houver. `GetOrCreateAsync(key, factory, entryOptions, tags, ct)`; `SetAsync`; `RemoveByTagAsync`. Serializer default: `System.Text.Json` (suficiente — `SpecDriftReportDto` é POCO; server não é AOT). Falha de backend L2 é não-fatal por design (log + miss).

**Files to read before implementing:**
- `src/Taskboard.Server/Program.cs` (registrations ~137-165, cache/SpecDrift ~720-750, endpoint ~1467-1487)
- `src/Taskboard.Server/Services/SpecDriftReportCache.cs`, `SpecDriftScanService.cs`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` (Catalog, IsSecret, validators)
- `src/Taskboard.Application.Contracts/Harness/PipelineAutoRetryOptions.cs` (padrão de options)
- `tests/Taskboard.Tests.Unit/Application/RuntimeConfigurationServiceTests.cs`
- `tests/Taskboard.Tests.Unit/Specs/SpecDriftScanServiceTests.cs`
- `tests/Taskboard.Tests.Integration/TaskboardWebApplicationFactory.cs`
- `Directory.Packages.props`, `src/Taskboard.Server/Taskboard.Server.csproj`
- `src/Taskboard.Server/appsettings.json`

**Files to create or modify:**
```text
Directory.Packages.props                                        (+2 PackageVersion)
src/Taskboard.Server/Taskboard.Server.csproj                    (+2 PackageReference)
src/Taskboard.Server/Program.cs                                 (RegisterCaching + endpoint + usings)
src/Taskboard.Server/Services/SpecDriftReportCache.cs           (fachada HybridCache, async)
src/Taskboard.Server/Services/SpecDriftScanService.cs           (previous-report local + SetAsync)
src/Taskboard.Server/appsettings.json                           (bloco Taskboard:Cache)
src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs (+4 CatalogEntry + validators)
docs/installation.md, docs/installation.pt-br.md                (seção Taskboard:Cache:*)
docs/technologies.md, docs/technologies.pt-br.md                (HybridCache/Redis na stack)
tests/Taskboard.Tests.Unit/Application/RuntimeConfigurationServiceTests.cs (novos casos)
tests/Taskboard.Tests.Unit/Specs/SpecDriftScanServiceTests.cs   (fachada async)
tests/Taskboard.Tests.Unit/Specs/SpecDriftReportCacheTests.cs   (novo — fachada sobre HybridCache)
tests/Taskboard.Tests.Integration/…                             (boot L1-only + drift-report 200)
```

## 4. Requirements

### RF-001: Schema de configuração `Taskboard:Cache`
- **Description:** `appsettings.json` ganha o bloco abaixo; overrides aceitos via `Taskboard__*`/`HARNESS__*` env (`HARNESS__CACHE__REDIS__CONNECTIONSTRING` → `Taskboard:Cache:Redis:ConnectionString`) e via DB override do catálogo.
- **Schema:**
  ```jsonc
  "Taskboard": {
    "Cache": {
      "DefaultExpiration": "00:05:00",       // L2 (Redis) TTL default
      "LocalCacheExpiration": "00:01:00",    // L1 (in-process) TTL default
      "Redis": {
        "InstanceName": "harness:"           // prefixo de chave no Redis
        // "ConnectionString": "localhost:6379" — vazio/ausente = L1-only
      }
    }
  }
  ```
- **Rules:** formato `TimeSpan` (`hh:mm:ss`); `LocalCacheExpiration` deve ser ≤ `DefaultExpiration` — valor maior é clampado ao default com log `Warning` no boot; `ConnectionString` vazio/ausente desativa L2.

### RF-002: Catálogo de runtime (`RuntimeConfigurationService`)
- **Description:** 4 novas entradas, todas `Editable: true, RequiresRestart: true` (DI wiring só acontece no boot):
  - `Taskboard:Cache:Redis:ConnectionString` — default `null`; secret-mascada automática via `IsSecret` (contém `ConnectionString`); validação: vazio (desativa) ou não-branco.
  - `Taskboard:Cache:Redis:InstanceName` — default `harness:`; validação: não-vazio, ≤64 chars, sem whitespace.
  - `Taskboard:Cache:DefaultExpiration` — default `00:05:00`; `TimeSpan.TryParse` + `> TimeSpan.Zero`.
  - `Taskboard:Cache:LocalCacheExpiration` — default `00:01:00`; idem.
- **Rules:** `EnvAlias: null` nas 4 (o mapper genérico `HARNESS__*` já cobre — sem alias dedicado, igual `Taskboard:Chat:MaxToolIterations`).

### RF-003: Registro DI — HybridCache + Redis L2 condicional
- **Description:** novo `RegisterCaching()` em `Program.cs` (chamado antes de `builder.Build()`, junto ao bloco `RegisterCoreServices`):
  - `AddStackExchangeRedisCache` **somente** quando `Taskboard:Cache:Redis:ConnectionString` não-vazia (`options.Configuration` = connstring, `options.InstanceName` = prefixo).
  - `AddHybridCache` **sempre**, com `DefaultEntryOptions.Expiration`/`LocalCacheExpiration` lidos das chaves RF-001.
  - Connstring sem `abortConnect` recebe append `abortConnect=false` antes de registrar o RedisCache — sem isso, Redis indisponível segura a primeira operação de cache no timeout de connect (~5s) em vez de degradar instantâneo.
- Log `Information` no boot: `HybridCache: Redis L2 enabled (instance 'harness:')` ou `HybridCache: L1-only (no Redis configured)`. **Nunca** logar a connection string.
- **Rules:** quando L2 configurada, `IDistributedCache` resolve para `RedisCache`; quando não, `GetService<IDistributedCache>()` retorna `null` e HybridCache opera L1-only.

### RF-004: Degradação L1-only
- **Description:** connection string malformada, Redis indisponível no boot ou queda em runtime **não** derrubam o servidor: falhas de L2 viram cache miss + log `Warning`; respostas continuam corretas via L1/factory.
- **Rules:** nenhuma exceção de Redis propaga para endpoints; sem retry/circuit-breaker customizado nesta versão (o multiplexer do StackExchange.Redis já faz reconnect interno).

### RF-005: Piloto — `SpecDriftReportCache` sobre HybridCache
- **Description:** a classe vira fachada async sobre `HybridCache` (mesmo nome/namespace — minimiza diff):
  - `ValueTask<SpecDriftReportDto> GetOrCreateAsync(Func<CancellationToken, ValueTask<SpecDriftReportDto>> factory, CancellationToken ct)` → `HybridCache.GetOrCreateAsync` com key `"specs:drift-report"`, tag `"spec-drift"`, entry options explícitas: `Expiration = 6h` (sobrevive a restart via L2 e cobre vários ticks do job horário), `LocalCacheExpiration = 5min`.
  - `ValueTask SetAsync(SpecDriftReportDto report, CancellationToken ct)` → `HybridCache.SetAsync` mesma key/tag/options (push do relatório fresco a cada scan).
  - Propriedade `Last` e `Update(report)` síncronos são removidos.
- **Consumidores:**
  - `SpecDriftScanService.RunJobAsync`: passa a guardar o relatório anterior em campo próprio (`_previous`, process-local — o diff "novos drifts desde o último scan" só faz sentido dentro do processo) e chama `await _cache.SetAsync(report, ct)`.
  - Endpoint `drift-report` (sem `?repo=`): `Results.Ok(await driftCache.GetOrCreateAsync(c => detector.BuildReportAsync(null, c), ct))` — substitui `Last ?? BuildReportAsync`; `?repo=` continua bypassando o cache.
- **Rules:** comportamento observável idêntico (relatório servido sem rescan); com Redis ativo, restart do processo ainda serve o último relatório (diferença real entregue pela feature).

### RF-006: Pacotes NuGet — DONE no #484
- `Microsoft.Extensions.Caching.Hybrid` 10.10.0 e `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.9 já estão em `Directory.Packages.props` e referenciados no `Taskboard.Server.csproj`. Nenhum pacote novo; a fachada e o wiring continuam no Server.

### RF-007: Documentação
- **Description:** `docs/installation.md` + `.pt-br.md` documentam `Taskboard:Cache:*` (chaves, defaults, env `HARNESS__CACHE__REDIS__CONNECTIONSTRING`, exemplo `localhost:6379`, comportamento L1-only); `docs/technologies.md` + `.pt-br.md` listam HybridCache/StackExchange.Redis.

**Business rules / invariants:**
- `LocalCacheExpiration` ≤ `DefaultExpiration` (clamp + warning se violado).
- Connection string nunca aparece em log nem em resposta de API (catálogo mascara; endpoint retorna `••••xxxx`).
- Sem Redis configurado: zero dependência de rede adicionada — app idêntico a hoje.
- Nomenclatura de chave HybridCache: `"{domínio}:{recurso}"` com identificadores internos apenas (doc Microsoft: nunca input bruto de usuário na key).

## 5. API Contract

N/A — nenhum endpoint novo. `/api/configuration` passa a listar as 4 chaves novas automaticamente via catálogo (RF-002); `GET /api/specs/drift-report` mantém contrato.

## 6. Acceptance Criteria

- [ ] **Given** `Taskboard:Cache:Redis:ConnectionString` ausente **when** o servidor sobe **then** `HybridCache` resolve via DI, `IDistributedCache` não está registrado, e o log de boot diz L1-only.
- [ ] **Given** `ConnectionString` = `localhost:6379,abortConnect=false` **when** o servidor sobe (mesmo sem Redis rodando) **then** boot completo, `IDistributedCache` resolve, log informa L2 enabled com o `InstanceName`, endpoints respondem normalmente.
- [ ] **Given** connection string malformada **when** o servidor sobe **then** boot completo com log `Warning`; primeira operação de cache degrada para L1/factory sem exception ao caller.
- [ ] **Given** `/api/specs/drift-report` sem `?repo=` **when** chamado 2× seguidas **then** a 2ª resposta vem do cache (factory `BuildReportAsync` não executa de novo — observável via contador no teste).
- [ ] **Given** relatório cached e scan horário executado **when** `SetAsync` roda **then** o endpoint passa a servir o relatório novo.
- [ ] **Given** `PUT /api/configuration` com `Taskboard:Cache:DefaultExpiration` = `"abc"` **then** `400`/validation error; com `"00:10:00"` **then** override persistido e marcado `requiresRestart`.
- [ ] **Given** `GET /api/configuration` **when** `ConnectionString` tem valor **then** resposta exibe `••••` + últimos 4 chars.
- [ ] **Given** `LocalCacheExpiration` > `DefaultExpiration` **when** o servidor sobe **then** local é clampado ao default + `Warning` no log.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Connstring vazia/whitespace | `"  "` | L1-only, sem tentativa de conexão |
| Redis cai no meio do run | connstring válida, processo Redis parado | miss L2 + warning; L1/factory seguem servindo |
| InstanceName customizado | `"acme:"` | chaves Redis prefixadas `acme:` |
| DB override de connstring | via Settings UI | mascarado no GET, exige restart para efetivar |
| Redis compartilhado entre apps | mesma connstring | `InstanceName` evita colisão de chaves |
| Connstring sem `abortConnect` | `localhost:6379` | append automático `abortConnect=false` no boot; log não inclui a connstring |

## 7. Task Plan

- [ ] **T1 — Discovery:** reler arquivos da seção 3; confirmar versão estável >7d dos 2 pacotes no NuGet.
- [ ] **T2 — Packages + config:** `Directory.Packages.props`, csproj, `appsettings.json`, 4 `CatalogEntry` + validators em `RuntimeConfigurationService`.
- [ ] **T3 — Wiring:** `RegisterCaching()` (Redis condicional + `AddHybridCache` com defaults + logs de boot + clamp RF-001).
- [ ] **T4 — Piloto:** `SpecDriftReportCache` → fachada async; `SpecDriftScanService` campo `_previous` + `SetAsync`; endpoint `drift-report` via `GetOrCreateAsync`.
- [ ] **T5 — Tests:** unit (validators, fachada com HybridCache real L1-only via `ServiceCollection`) — nomes `Dado_Quando_Entao`; integração (`WebApplicationFactory`: boot L1-only, drift-report 200, factory-single-flight).
- [ ] **T6 — Docs + validation:** `docs/installation*` + `docs/technologies*`; `dotnet build` (warnings-as-errors), `dotnet test`, `dotnet format --verify-no-changes`; cobertura ≥80% line / ≥45% branch no código novo.
- [ ] **T7 — Done + PR:** DoD completo → `Status = Done`, branch `feat/devin-20261004-redis-hybrid-cache`, PR justificando os NuGets.

## 8. Organization Guardrails

- Padrão: feature branch; nunca `main`/`develop`; `.github/workflows` intocado.
- Novo NuGet → justificativa no PR (esta SPEC já documenta o porquê: HybridCache é a abstração recomendada pela Microsoft para cache em camadas com stampede protection nativa).
- Secrets: connstring pode conter senha — nunca em log/commit; mascarada no catálogo.
- Sem lógica de negócio em endpoints; mudança de contrato refletida aqui primeiro.

## 9. Definition of Done

- [ ] RF-001..RF-007 implementados.
- [ ] Todos os ACs cobertos por testes passando (seção 7.1 do template: .NET → unit + integração, ≥80%).
- [ ] Edge cases da seção 6 tratados.
- [ ] `dotnet build` (TreatWarningsAsErrors), `dotnet test`, format check verdes.
- [ ] Nenhum log contém connection string/senha.
- [ ] SPEC atualizada se qualquer decisão mudar durante a implementação.

## Open Questions / Pending Ambiguity

- Versões exatas dos pacotes `Microsoft.Extensions.Caching.*` — resolver na T1 (deve ser a 10.x estável mais recente com >7 dias de publicação).
- `Expiration = 6h` do piloto drift-report é o default da SPEC; ajuste fino permitido na implementação se houver evidência (manter ≥ intervalo do job de 1h).
