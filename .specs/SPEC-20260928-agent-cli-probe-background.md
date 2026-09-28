# SPEC-20260928-agent-cli-probe-background

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-cli-probe-background` |
| Type | `Feature` (performance) |
| Stack | `.NET 10 / Blazor WASM / BackgroundService` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260928-agent-cli-probe-background` |
| Ticket | [#348](https://github.com/afonsoft/agent-harness/issues/348) |
| Status | `Done` |
| Related | `SPEC-20260917-cli-agents-terminal`, `SPEC-20260918-cli-agents-expansion`, `SPEC-20260921-ai-chat-cli-backend` |

## 1. User Story

**As a** usuário do Harness nas telas `/agents` e `/ai-chat`
**I want** que a validação de CLIs instaladas (`--version`, listas de modelos) rode em background contra um snapshot persistido
**So that** a tela renderize instantaneamente com o último estado conhecido e atualize os dados quando a sonda terminar — em vez de esperar dezenas de segundos em subprocessos sequenciais.

**Problem context (evidência de código):**

1. `GET /api/agent-clis` → `AgentCliStatusService.GetStatusAsync` faz **loop sequencial** sobre `AgentCliMap.All` (**14 CLIs**), e para cada instalada executa `<cli> --version` via subprocesso com timeout de **5s** (`AgentCliStatusService.cs`). Pior caso ≈ **70s**; caso comum (4-6 CLIs instaladas) = **5–15s de request bloqueado**.
2. A mesma sonda lenta é reutilizada por `AgentEligibilityService.GetEligibleTypesAsync` — que alimenta `AiChatCatalogService`, `AiChatService`, `SettingsService` (`SettingsService.cs:157`), pipeline e orchestration → **a tela AI Code também bloqueia** ao montar o picker de agentes/modelos.
3. `GET /api/agents/{type}/models/available` → `AgentModelCatalogService.ListAvailableAsync` executa `<cli> models` com timeout de **10s**; cache só em memória (5min) — a cada restart do server, o primeiro open do dialog de modelos paga 10s por CLI.
4. Insight chave: só o subprocesso é lento. `PATH` lookup (`PathSearch.FindExecutable`) e checagem de credencial (`File.Exists`) são operações de filesystem (~ms) — podem rodar **inline** a cada request sem custo, mantendo `installed`/`auth` sempre frescos; apenas `version` e `models` precisam vir de snapshot.

## 2. Scope

**In scope:**

1. **`CliProbeSnapshotService` (singleton):** fonte única de snapshot — mantém em memória os resultados de `--version` por CLI e as listas de modelos por `AgentType`, persistidos em `agent-probe-snapshot.json` sob `Taskboard:DataDir` (load no startup; write-behind após cada refresh).
2. **Fast path no `GetStatusAsync`:** resposta composta por probes baratos inline (`installed` via PATH, `auth` via arquivo de credencial — sempre frescos) + `version` vinda do snapshot. Retorna em <100ms.
3. **Refresh em background (single-flight):** `RefreshAsync` dispara probes `--version` em **paralelo** (`Task.WhenAll`, timeout 5s por probe) + probes de modelos (10s por CLI, só para CLIs instaladas); deduplicado — chamadas concorrentes reutilizam o mesmo `Task`; atualiza memória + persiste JSON.
4. **Triggers de refresh:**
   - `IHostedService` no startup (não bloqueia boot).
   - TTL: `GET /api/agent-clis` com snapshot mais velho que `AgentCliProbe:TtlSeconds` (default 120s) dispara refresh em background e responde o snapshot atual imediatamente.
   - `POST /api/agent-clis/refresh` — botão refresh do usuário (202, single-flight).
5. **Status de refresh para a UI:** `GET /api/agent-clis/refresh` → `{ running, lastCompletedAt, lastDurationMs }`; `Agents.razor` renderiza instantâneo, mostra indicador "updating…" enquanto `running`, e re-busca `/api/agent-clis` uma vez ao completar (poll leve ~1.5s, máx ~15s).
6. **Models available:** `ListAvailableAsync` responde do snapshot in-memory imediatamente; quando stale/miss, retorna último conhecido (ou `[]`) e agenda probe em background — nunca bloqueia 10s; `?refresh=true` continua forçando probe síncrono bounded (compat).
7. **Resposta não-breaking:** `GET /api/agent-clis` mantém `AgentCliStatus[]` (campos podem refletir snapshot); `models/available` mantém shape, com campo aditivo opcional `refreshing` quando um warm-up está em curso.

**Out of scope:**

- Push SignalR/SSE para as telas (poll leve basta nesta entrega; SSE pode vir com o slot de realtime das SPECs irmãs).
- Mudar timeouts de probe (5s/10s) ou o formato dos parsers.
- Cache/snapshot por usuário — o estado de CLIs é do servidor (multi-tenant fora).
- Probes de modelos para CLIs não instaladas (já skipped hoje).

## 3. Technical Context

**Where the change happens:**

- `Taskboard.Integrations/Agents`:
  - `CliProbeSnapshotService.cs` (**new**) — snapshot em memória + JSON persistido (`DataDir/agent-probe-snapshot.json`), `RefreshAsync` single-flight com `Task.WhenAll` de probes paralelos, expõe `Refreshing`/`LastCompletedAt`.
  - `AgentCliStatusService.cs` (**modified**) — `GetStatusAsync` vira fast path (PATH + auth inline, version do snapshot); injeta `CliProbeSnapshotService`; TTL-check dispara `_ = refresher.RefreshAsync()` fire-and-forget.
  - `AgentModelCatalogService.cs` (**modified**) — lê/escreve via `CliProbeSnapshotService` em vez de só `_cache`; responde instantâneo e agenda warm-up; `forceRefresh` mantém probe síncrono.
  - `CliProbeRefreshHostedService.cs` (**new**) — `IHostedService` que chama `RefreshAsync` no startup sem bloquear.
- `Taskboard.Server/Program.cs` — registra snapshot service + hosted service; novos endpoints `POST/GET /api/agent-clis/refresh`; `models/available` anota `refreshing`.
- `Taskboard.Blazor` — `Agents.razor`: indicador "updating…", poll de `refresh` status e refetch único ao completar; `TaskboardClient.cs` + métodos `GetAgentCliRefreshStatusAsync`, `RefreshAgentClisAsync`; `AgentModelConfigDialog` mostra "carregando modelos…" e re-tenta uma vez quando `refreshing`.
- `Taskboard.Application.Contracts` — `AgentCliRefreshStatusDto` (**new**); `AgentCliStatus`/`models/available` payloads com campo aditivo.

**Files to read before implementing:**

- `src/Taskboard.Integrations/Agents/AgentCliStatusService.cs`, `AgentModelCatalogService.cs`, `AgentCliInstallService.cs` (padrão de runner/timeout)
- `src/Taskboard.Domain.Shared/Agents/AgentCliMap.cs`
- `src/Taskboard.Application/Agents/AgentEligibilityService.cs`, `src/Taskboard.Application/Settings/SettingsService.cs:150-165`, `src/Taskboard.Application/AiChat/AiChatCatalogService.cs`
- `src/Taskboard.Server/Program.cs` (registro DI ~450, endpoints ~2361)
- `src/Taskboard.Blazor/Components/Pages/Agents.razor`, `Components/AgentModelConfigDialog.razor`, `Services/TaskboardClient.cs`, `Services/HttpAgentModelConfigService.cs`
- `src/Taskboard.Application.Contracts/Configuration/TaskboardEnvironment.cs` (DataDir)

**Files to create or modify:**

```text
src/Taskboard.Integrations/Agents/CliProbeSnapshotService.cs        # new
src/Taskboard.Integrations/Agents/CliProbeRefreshHostedService.cs   # new
src/Taskboard.Integrations/Agents/AgentCliStatusService.cs          # modified — fast path + snapshot
src/Taskboard.Integrations/Agents/AgentModelCatalogService.cs       # modified — snapshot + background warm
src/Taskboard.Server/Program.cs                                     # modified — DI + endpoints refresh
src/Taskboard.Application.Contracts/Dtos/AgentCliRefreshStatusDto.cs # new
src/Taskboard.Blazor/Components/Pages/Agents.razor                  # modified — updating indicator + refetch
src/Taskboard.Blazor/Components/AgentModelConfigDialog.razor        # modified — loading/retry de modelos
src/Taskboard.Blazor/Services/TaskboardClient.cs                    # modified — refresh status endpoints
tests/Taskboard.Tests.Unit/Agents/CliProbeSnapshotServiceTests.cs   # new
tests/Taskboard.Tests.Unit/Agents/AgentCliStatusFastPathTests.cs    # new
tests/Taskboard.Tests.Integration/AgentCliRefreshEndpointsTests.cs  # new
```

## 4. Requirements

### RF-001: Resposta instantânea de `/api/agent-clis`

- **Description:** O endpoint responde em <200ms típico com `installed`/`auth` computados inline e `version` do último snapshot — nunca aguarda subprocesso `--version`.
- **Rules:** Snapshot ausente (primeiro boot) → `version: null` até o refresh completar; `installed`/`auth` sempre reais.
- **Input → Output:** GET → `AgentCliStatus[]` imediato.

### RF-002: Refresh paralelo em background

- **Description:** `RefreshAsync` roda os probes `--version` das CLIs instaladas **em paralelo** com timeout individual de 5s, e os probes de modelos elegíveis em paralelo com timeout de 10s; atualiza memória e persiste JSON ao concluir.
- **Rules:** single-flight — chamadas durante um refresh retornam o mesmo `Task`; falha de um probe não afeta os demais (degrada para null/[] como hoje).
- **Input → Output:** N×probes paralelas → wall time ≈ max(5s) em vez de N×5s.

### RF-003: Snapshot persistido

- **Description:** Resultados gravados em `{DataDir}/agent-probe-snapshot.json` (write atômico tmp+rename); carregados no startup para servir `version`/`models` antes do primeiro refresh.
- **Rules:** arquivo corrompido → ignora e parte de snapshot vazio; conteúdo nunca inclui credenciais (só versões e nomes de modelos).

### RF-004: Triggers + endpoint de status

- **Description:** Refresh disparado por hosted service no startup, por TTL (default 120s, configurável `AgentCliProbe:TtlSeconds`) na leitura, e por `POST /api/agent-clis/refresh` (202 single-flight); `GET /api/agent-clis/refresh` reporta `{ running, lastCompletedAt, lastDurationMs }`.
- **Rules:** TTL desligável (`0` = só manual+startup); request cancellation token **não** cancela o refresh em background (usa token próprio com timeout global ~20s).

### RF-005: UI responsiva

- **Description:** `Agents.razor` renderiza o snapshot imediato, exibe indicador discreto "Updating CLI info…" enquanto `running`, e faz um único refetch ao completar; dialog de modelos exibe "carregando modelos…" e re-consulta uma vez se `refreshing`.
- **Rules:** nenhuma tela trava em spinner bloqueante; estado de erro de refresh vira warning não-fatal (último snapshot continua servido).

### RF-006: Elegibilidade e consumidores transparentes

- **Description:** `AgentEligibilityService`, `AiChatCatalogService`, `SettingsService` e orchestration consomem o fast path sem mudança de contrato.
- **Rules:** `installed`/`auth` nunca servidos de snapshot (sempre inline) — auth não pode ficar stale.

**Business rules / invariants:**

- Dados baratos (PATH, cred-file) = sempre inline; dados caros (subprocesso) = sempre snapshot.
- Um único refresh em voo por vez, no processo inteiro.
- Snapshot é best-effort: indisponível ≠ erro.

## 5. API Contract

**Endpoint:** `POST /api/agent-clis/refresh`
**Auth:** mesma sessão admin dos endpoints `/api/agent-clis*`.

**Response (success):** `202 { "running": true }` (ou `200` se um refresh já estava em voo — mesmo `Task`).

**Endpoint:** `GET /api/agent-clis/refresh`

**Response (success):**
```json
{ "running": false, "lastCompletedAt": "2026-09-28T15:10:03Z", "lastDurationMs": 4120 }
```

**Expected errors:** `401` auth — formato genérico existente.

## 6. Acceptance Criteria

- [x] **Given** o server recém-iniciado com snapshot persistido **when** `GET /api/agent-clis` **then** responde <200ms com versões do snapshot e `installed`/`auth` frescos.
- [x] **Given** snapshot expirado (TTL) **when** `GET /api/agent-clis` **then** responde o snapshot stale imediato **e** um refresh background é disparado (observável via `GET /refresh`).
- [x] **Given** 14 CLIs com 6 instaladas **when** refresh roda **then** completa em ~5s (paralelo), não ~30s sequenciais — `lastDurationMs` confirma.
- [x] **Given** `/agents` carregando **when** o refresh está em curso **then** a página mostra cards imediatamente + indicador "updating", e atualiza sozinha ao fim.
- [x] **Given** `models/available` cold (pós-restart) **when** abro o dialog de modelos **then** retorna instantâneo (cache/[]) e o background warm-up preenche; segunda abertura já tem modelos.
- [x] **Given** um CLI que trava no `--version` **when** probe estoura 5s **then** demais CLIs completam normalmente (isolamento).
- [x] **Given** AI Code/NewThreadDialog **when** lista agentes elegíveis **then** sem bloqueio perceptível (eligibility no fast path).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Primeiro boot sem snapshot | GET `/api/agent-clis` | `version: null`, `installed`/`auth` corretos; refresh em voo |
| Snapshot JSON corrompido | startup | Log warning, snapshot vazio, refresh agenda |
| Dois requests simultâneos com TTL expirado | GET concorrentes | Um único `Task` de refresh (single-flight) |
| `refresh=true` em models/available | force sync | Probe síncrono bounded 10s (compat) |
| Restart durante write | tmp+rename | Nunca JSON parcial |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** ler arquivos da seção 3; confirmar pontos de bloqueio e contratos do frontend.
- [x] **T2 — Snapshot service:** `CliProbeSnapshotService` (memória + JSON atômico + RefreshAsync paralelo single-flight) + hosted service de startup.
- [x] **T3 — Fast path:** `AgentCliStatusService` e `AgentModelCatalogService` no snapshot; endpoints `refresh` + DI.
- [x] **T4 — UI:** indicador/poll em `Agents.razor`, retry no `AgentModelConfigDialog`, `TaskboardClient` novos métodos.
- [x] **T5 — Verification:** unit tests (single-flight, paralelismo com fakes de runner, snapshot corrupt), integração (endpoints, TTL), `dotnet build` + `dotnet test`.
- [x] **T6 — Validation:** DoD; evidência de latência no PR (antes/depois com `lastDurationMs` e tempo de resposta do endpoint).
- [x] **T7 — Done + PR:** `Status = Done`, PR na branch `feature/devin-20260928-agent-cli-probe-background`.

**7.1 Validation strategy**

- `.NET`: unit tests com `ISkillsInstallRunner` fake (probes lentas simuladas — prova de paralelismo e single-flight); integração para endpoints/TTL; coverage ≥ gate vigente.
- Evidência de perf obrigatória no PR: tempo de resposta de `GET /api/agent-clis` antes/depois.

## 8. Organization Guardrails

- **Branches:** `feature/devin-20260928-agent-cli-probe-background`; nunca direto em `main`.
- **Workflows:** intocado.
- **Security:** snapshot contém apenas versões/nomes de modelos — nunca tokens; cred-file segue só `File.Exists` (conteúdo nunca lido, regra existente preservada).
- **Scope:** sem push realtime, sem mudança de timeouts, sem probes novos além dos existentes.
- **Architecture:** probe/spawn em Integrations; composição de resposta no service; endpoints finos.

## 9. Definition of Done

- [x] RF-001 a RF-006 implementados.
- [x] Acceptance criteria cobertos; latência medida e documentada.
- [x] Edge cases tratados (single-flight, corrupt snapshot, primeiro boot).
- [x] `dotnet build` limpo, `dotnet test` verde, coverage ≥ gate.
- [x] Guardrails respeitados; logs sem segredos.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/devin-20260928-agent-cli-probe-background`.
