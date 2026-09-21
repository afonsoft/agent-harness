# SPEC-20260918-agent-model-config

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-model-config` |
| Type | `Feature` |
| Stack | `.NET 10 / Blazor` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260918-agent-model-config` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** administrador na tela CLI Agents
**I want** configurar os modelos de cada tier (Lite / Normal / Ultra) por CLI instalada
**So that** eu controle exatamente qual modelo cada tier usa, sem depender da tabela curada embutida.

**Problem context:**
O mapeamento tier→modelo vive hoje na tabela curada `AgentCliModels` (hardcoded). O usuário quer um botão de configuração por CLI instalada que abra um popup listando os modelos disponíveis e permitindo redefinir cada tier; sem configuração, os defaults curados valem.

**Bugs corrigidos neste SPEC:**
1. `POST /api/agents/executions` aceita `repositoryFullName` inválido (ex.: literal `"RepositoryFullName"`) e só falha ao mover a issue para review, depois de queimar uma execução inteira. Deve validar `owner/name` e retornar 400 antes de enfileirar.
2. `KnownCliAgentAdapter` chama `BuildArguments(type, prompt)` sem `ModelTier` — o argv sempre carrega o modelo do tier Normal mesmo quando Lite/Ultra foi escolhido (o `modelName` persistido divergia do argv real).

## 2. Scope

**In scope:**
- `IAgentModelConfigService` (contracts) + `AgentModelConfigService` (Application): lê/grava overrides por `AgentType` na tabela `ConfigurationOverrides` (chave `Taskboard:Agents:Models:{AgentType}`, JSON `{lite, normal, ultra}`).
- `AgentCliModels.Catalog(AgentType)` — lista de modelos conhecidos por CLI para o picker (curados + extras).
- `AgentExecutionRequest.ResolvedModelName` (opcional, default null) — modelo efetivo resolvido pela orquestração e propagado ao adapter.
- `AgentCliInvocation.BuildArguments(..., string? modelName)` — nome explícito vence o curado.
- Endpoints `GET/PUT/DELETE /api/agents/{agentType}/models` (auth).
- Client `HttpAgentModelConfigService` + `AgentModelConfigDialog` (AutoComplete editável com busca, alimentado pelo catálogo curado + modelos reportados pela CLI instalada) + botão **Models** ao lado de Login (somente CLIs instaladas com suporte a flag).
- `IAgentModelCatalogService` + `AgentModelCatalogService` (Integrations): probe headless `GET /api/agents/{type}/models/available` — `opencode models`, `devin models list`, `agy models` (10s timeout, cache 5min, parser por formato em `AgentModelListParser`).
- Bug fixes (1) e (2) acima.

**Out of scope:**
- Renomear tiers ou adicionar novos.
- _(Implementado depois:_ descoberta dinâmica via `<cli> models` — RF-008._)_

## 3. Functional Requirements

- **RF-001** `POST /api/agents/executions` valida `repositoryFullName` contra `^[\w.-]+/[\w.-]+$`; inválido → `400 { error: "invalid-repository" }`.
- **RF-002** O adapter passa `request.ModelTier` e `request.ResolvedModelName` ao `BuildArguments` — argv reflete o tier/modelo efetivo.
- **RF-003** `AgentModelConfigService.ResolveAsync(type, tier)` → override ?? curado ?? null (CLI-managed).
- **RF-004** `GET /api/agents/{type}/models` retorna `{ agentType, supportsModelSelection, source, lite, normal, ultra, defaults, catalog }`; `404` para tipo desconhecido, `422` para CLI-managed.
- **RF-005** `PUT` valida strings não-vazias ≤128 chars (cada tier pode ser omitido → usa default); `DELETE` remove o override.
- **RF-006** `AgentOrchestrationService.EnqueueAsync` resolve o modelo via o service (scope), grava em `AgentRun.ModelName` e propaga `ResolvedModelName` — argv e run record nunca divergem.
- **RF-007** CLI Agents: botão **Models** (ícone engrenagem) na coluna Actions, visível apenas para `Installed && SupportsModelSelection`. Dialog mostra os 3 tiers com **AutoComplete editável** (digitação livre + busca por conteúdo, `StringFilterOperator.Contains`), badge `Override|Default`, botões Salvar / Restaurar defaults / Cancelar.
- **RF-008** `GET /api/agents/{type}/models/available` retorna `{ models }` — ids que a CLI instalada reporta headless (`opencode models` linhas, `devin models list` famílias/variantes/aliases, `agy models` `id<TAB>nome`; probe 10s, cache 5min — `?refresh=true` fura o cache, falha → `[]`). `422 model-selection-unsupported` para CLI-managed. O dialog mescla available + catálogo curado + valores atuais (distinct, case-insensitive) nas sugestões e tem botão **Sync** que chama `?refresh=true` para atualizar a lista sob demanda.

## 4. Technical Notes

- Override key: `Taskboard:Agents:Models:{AgentType}` — JSON `{"lite":"...","normal":"...","ultra":"..."}`; campo ausente cai no curado por-tier.
- Orquestração resolve via `IServiceScopeFactory` (service é scoped — `IRepository<ConfigurationOverride>`), mesmo padrão do `IAgentEligibilityService`.
- `ResolvedModelName` é setado server-side; o cliente não o envia (o servidor sobrescreve sempre).

## 5. Acceptance Criteria

- [x] POST executions com `repositoryFullName: "RepositoryFullName"` → 400 imediato, sem run criado.
- [x] Execução com tier Ultra produz argv com o modelo Ultra (e `modelName` persistido igual).
- [x] Salvar override `lite=custom-x` → próxima execução Lite usa `custom-x`; DELETE restaura o curado.
- [x] CLI Agents mostra o botão Models apenas em CLIs instaladas com flag de modelo.
- [x] `dotnet build` limpo; suites verdes.

## 6. Task Breakdown

1. **T1** — Validação `repositoryFullName` no endpoint + teste de integração.
2. **T2** — Fix tier no adapter + `modelName` param em `BuildArguments` + testes.
3. **T3** — Contracts (catalog, DTO, service interface, `ResolvedModelName`) + `AgentModelConfigService`.
4. **T4** — Endpoints + resolução na orquestração + testes.
5. **T5** — Client service + dialog + botão + docs.

## 7. Security & Constraints

- Endpoints atrás do auth existente.
- Modelos são passados via argv (ProcessStartInfo.ArgumentList) — nunca shell-interpolados.
- Sem secrets em logs; nomes de modelo não são sensíveis.
