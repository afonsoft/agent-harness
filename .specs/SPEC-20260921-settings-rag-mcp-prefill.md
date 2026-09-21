# SPEC-20260921-settings-rag-mcp-prefill

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `settings-rag-mcp-prefill` |
| Type | `Bugfix` (Frontend/UX) |
| Stack | `.NET 10 / Blazor WASM` |
| Repository | `/home/ubuntu/repos/agent-harness` |
| Branch | `feature/devin-20260921-settings-rag-mcp-prefill` |
| Ticket | [#269](https://github.com/afonsoft/agent-harness/issues/269) |
| Status | `Done` |

## 1. User Story

**As a** usuário do Harness
**I want** que os campos do card "RAG / Knowledge MCP" em Settings mostrem o valor **efetivamente configurado** (name + URL) como valor real do input — nunca apenas como placeholder — e que eu veja de onde o valor veio (db/env/appsettings/default)
**So that** eu saiba com certeza se o MCP está configurado e com qual URL, sem confundir placeholder com valor salvo.

**Problem context:**
O input `rag-url` usa `placeholder="https://rag.afonsoft.dev/mcp"` — que é exatamente a URL de produção real do servidor RAG. Resultado: quando o campo está vazio, a tela *parece* configurada; quando está preenchido com o valor real, é visualmente indistinguível do placeholder. O usuário não consegue afirmar se `Taskboard:Rag:Url` está persistida.

Evidências coletadas:
- `Settings.razor` já faz bind: `LoadConfigurationAsync()` preenche `_ragName`/`_ragUrl` a partir de `GET /api/configuration` (`Taskboard:Rag:ServerName`, `Taskboard:Rag:Url`) — `Settings.razor:873-875`.
- O banco de produção **contém** `Taskboard:Rag:Url = https://rag.afonsoft.dev/mcp` (override em `ConfigurationOverrides`, verificado via sqlite3) — ou seja, o valor salvo é idêntico ao placeholder, tornando o estado ambíguo.
- Se `GET /api/configuration` falhar, `_configError` só é exibido na seção "Configuration" no fim da página (`Settings.razor:327-330`) — os campos RAG ficam silenciosamente vazios mostrando só o placeholder.
- `GET /api/mcp/status` já expõe a visão resolvida pelo motor de provisionamento (`ServerName`, `ConfiguredUrl` — `McpProvisionStatus`), mas o formulário não a usa — duas leituras do mesmo dado podem divergir.

## 2. Scope

**In scope:**
- Prefill confiável dos campos `Server name` e `MCP URL` com o valor efetivo, com fallback para `mcp/status` quando `/api/configuration` falhar ou não trouxer as chaves.
- Placeholders genéricos que nunca imitam um valor real (`e.g. knowledge`, `e.g. https://mcp.example.com/mcp`).
- Indicador de fonte do valor efetivo (`db`/`env`/`appsettings`/`default`) e estado "salvo" ao lado dos campos.
- Erro de `/api/configuration` exibido também dentro da seção RAG (não só na tabela de Configuration).
- Indicação visual clara de "configurado vs não configurado" (ex.: badge `configured` quando `ConfiguredUrl` ≠ vazio).

**Out of scope:**
- Mudanças no `McpProvisioningService`, endpoints `mcp/*` ou `RuntimeConfigurationService` (server já resolve e expõe tudo).
- Health-check/conectividade da URL MCP.
- Suporte a múltiplos servidores MCP.
- Detecção de drift entre a URL salva e a URL presente nos arquivos dos CLIs (futuro — `McpAgentResult` não expõe a URL observada hoje).

## 3. Technical Context

**Where the change happens:** apenas `Taskboard.Blazor` — `Components/Pages/Settings.razor` (markup da seção RAG + `LoadConfigurationAsync`/`LoadMcpStatusAsync`). Nenhuma mudança de API: `GET /api/configuration` e `GET /api/mcp/status` já retornam o necessário.

**Files to read before implementing:**
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — seção RAG (160-258), estado (441-455), `LoadConfigurationAsync` (860-883), `LoadMcpStatusAsync` (687-698)
- `src/Taskboard.Blazor/Services/TaskboardClient.cs` — `GetConfigurationEntriesAsync` (194), `GetMcpStatusAsync` (264)
- `src/Taskboard.Application.Contracts/Mcp/McpProvisionStatus.cs` — `ServerName`, `ConfiguredUrl`
- `src/Taskboard.Application.Contracts/Configuration/ConfigurationEntryDto.cs` — `EffectiveValue`, `Source`, `Masked`
- `src/Taskboard.Application/Configuration/RuntimeConfigurationService.cs` — catálogo + `ResolveValue`/`ResolveSource` (precedência db > env-alias > env/appsettings > default)
- `.specs/SPEC-20260918-rag-mcp-sync.md`, `.specs/SPEC-20260917-rag-mcp-provisioning.md`

**Files to create or modify:**
```text
src/Taskboard.Blazor/Components/Pages/Settings.razor   [mod]
tests/Taskboard.Tests.Unit/Blazor/*                    [mod/new — se houver harness de teste de componente; caso contrário cobertura via teste de lógica extraída]
docs/settings.md / docs/settings.pt-br.md              [mod, se existirem]
```

**Fatos confirmados:**
- `McpProvisionStatus` carrega `ServerName` + `ConfiguredUrl` resolvidos por `McpProvisioningService.ResolveConfig()` (mesma precedência do catálogo: override DB > `TASKBOARD_RAG_*` env > `IConfiguration`).
- `ConfigurationEntryDto.EffectiveValue` da URL **não** é mascarado (só `*ApiKey*`, `*Token*`, `*Password*`, `*ConnectionString*` são).
- `_ragUrlSaved` já alimenta o estado do botão Sync e o banner "unsaved changes" — preservar essa semântica (valor **persistido**, não o digitado).

## 4. Requirements

### RF-001: Prefill com valor efetivo
- **Description:** ao abrir Settings, `Server name` e `MCP URL` exibem o valor efetivo como **value** do input. Ordem de resolução: (1) `GET /api/configuration` → `Taskboard:Rag:ServerName`/`Taskboard:Rag:Url` (`EffectiveValue`); (2) fallback `GET /api/mcp/status` → `ServerName`/`ConfiguredUrl` quando a chamada de configuração falhar ou omitir as chaves.
- **Rules:** `_ragUrlSaved` continua refletindo exclusivamente o valor persistido/resolvido — o banner "unsaved changes" (`_ragUrl != _ragUrlSaved`) permanece correto; ApiKey nunca é exibida (campo segue com placeholder `•••• (unchanged)` quando `_ragKeySet`).
- **Input → Output:** config com `Taskboard:Rag:Url=https://rag.afonsoft.dev/mcp` → input `rag-url` renderiza com `value="https://rag.afonsoft.dev/mcp"`.

### RF-002: Placeholders não ambíguos
- **Description:** placeholders viram exemplos genéricos que não podem ser confundidos com um valor real: `rag-name` → `e.g. knowledge`; `rag-url` → `e.g. https://mcp.example.com/mcp`.
- **Rules:** nenhum placeholder da seção RAG pode ser igual a um valor padrão/produção conhecido.

### RF-003: Fonte e estado visíveis
- **Description:** ao lado do campo URL, badge pequeno com a `Source` da entry (`db`/`env`/`appsettings`) e, quando vazio, texto "not configured"; abaixo dos campos, linha de status "Configured as `name` → `url`" derivada de `mcp/status` (ou "Not configured" quando `ConfiguredUrl` vazio).
- **Input → Output:** entry com `Source="db"` → badge `db`; `mcp/status` sem URL → linha "Not configured — Sync disabled until you save a URL".

### RF-004: Falha de configuração visível na seção
- **Description:** se `GetConfigurationEntriesAsync` falhar, a seção RAG exibe alerta inline (`_configError` ou mensagem específica) em vez de campos silenciosamente vazios; o fallback de RF-001 (mcp/status) ainda é tentado antes de exibir o erro.

### RF-005: Sem regressão nos fluxos existentes
- **Description:** Save & Sync (`PUT mcp/rag`), Sync, Remove, Verify, badges por agente e polling de status continuam funcionando; após salvar, `LoadConfigurationAsync` + `LoadMcpStatusAsync` re-hidratam os campos.

**Business rules / invariants:**
- ApiKey nunca entra no value do input nem em badges/tooltips.
- O valor exibido como "salvo" é sempre o resolvido server-side — nunca eco do que o usuário digitou.

## 5. API Contract

Sem endpoints novos ou alterados. Consumo existente:

```http
GET /api/configuration → 200 { entries: [{ key, effectiveValue, source, editable, requiresRestart, masked, readOnlyReason }] }
GET /api/mcp/status    → 200 { state, lastRunUtc, lastDurationMs, serverName, configuredUrl, agents[], error }
```

## 6. Acceptance Criteria

- [ ] **Given** `Taskboard:Rag:Url` persistida no DB **when** abro Settings **then** o input `rag-url` mostra a URL como value (não placeholder) e badge `db` aparece.
- [ ] **Given** nenhuma configuração RAG **when** abro Settings **then** os inputs ficam vazios com placeholder `e.g. …` e a linha de status diz "Not configured".
- [ ] **Given** `GET /api/configuration` falha **and** `mcp/status` retorna `ConfiguredUrl` **when** abro Settings **then** os campos mostram o valor do status + alerta informando que a fonte de configuração falhou.
- [ ] **Given** valor digitado diferente do salvo **when** edito o campo **then** o banner "Unsaved changes" continua aparecendo (sem regressão).
- [ ] **Given** placeholder novo **when** o campo está vazio **then** não é possível confundir o placeholder com a URL real de produção.
- [ ] **Given** `Taskboard:Rag:Url` vinda de env `TASKBOARD_RAG_URL` **when** abro Settings **then** input preenchido + badge `env`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Config e status divergem (override removido entre chamadas) | config vazio, status com URL | campos preenchidos pelo status; hint indica fonte `mcp/status` |
| URL salva com espaços | `"  https://… "` | exibida trimmed; Save envia trimmed (já existente) |
| ApiKey setada | `_ragKeySet=true` | placeholder `•••• (unchanged)` preservado; value vazio |
| Somente `ServerName` configurado | name=custom, url=null | name preenchido; URL vazia + "Not configured" |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** ler arquivos da seção 3; mapear todos os usos de `_ragName`, `_ragUrl`, `_ragUrlSaved`, `_ragKeySet`, `_mcpStatus`.
- [ ] **T2 — Implementation:** fallback `mcp/status` no load; placeholders genéricos; badge de fonte; linha "Configured as …"; erro inline na seção RAG.
- [ ] **T3 — Verification:** testes cobrindo a lógica de resolução (extrair para método testável se necessário); verificação manual: cenários AC1-AC6.
- [ ] **T4 — Validation:** `dotnet build` (warnings=errors) + `dotnet test`; screenshots da seção em cada estado.
- [ ] **T5 — Done + PR:** DoD completo → `Status = Done` → PR em `feature/devin-20260921-settings-rag-mcp-prefill`.

## 8. Organization Guardrails

- **Branches:** nunca commit em `main`/`master`/`develop`.
- **Workflows:** não editar `.github/workflows/**`.
- **Segurança:** ApiKey jamais exibida/logada; sem secrets no commit.
- **Escopo:** zero mudança de contrato de API ou no motor de provisionamento.
- **Specs:** este SPEC é o contrato; divergências encontradas na implementação atualizam o SPEC primeiro.

## 9. Definition of Done

- [ ] RF-001…RF-005 implementados.
- [ ] ACs cobertos por teste ou evidência de verificação (screenshots dos estados).
- [ ] `dotnet build` limpo; `dotnet test` verde; cobertura ≥ gate vigente.
- [ ] Guardrails respeitados; docs en/pt-br atualizadas se a seção for documentada.

## Open Questions / Pending Ambiguity

- Nenhuma bloqueante. Decisão registrada: a fonte primária do formulário continua sendo `/api/configuration` (catálogo único de chaves); `mcp/status` é fallback e alimenta a linha de status "Configured as".
