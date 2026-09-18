# SPEC-20260918-rag-mcp-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `rag-mcp-sync` |
| Type | `Bugfix` (UX + comportamento de provisionamento) |
| Stack | `.NET 10 / Blazor / config files (JSON/TOML)` |
| Repository | `/home/ubuntu/repos/taskboard-ai` |
| Branch | `feature/devin-20260918-rag-mcp-sync` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** usuário do Harness
**I want** que clicar em Sync no card "RAG / Knowledge MCP" realmente escreva a configuração nos arquivos dos CLIs — e que uma remoção nunca aconteça por engano
**So that** os agentes ganhem o servidor `knowledge` configurado, e eu entenda exatamente o que cada botão faz.

**Problem context:**
Log de produção: clicar em **Sync** produziu `MCP provision started — name 'knowledge', remove mode, 12 target(s)` → todos `NotConfigured`/`Removed` → `Succeeded`. Nenhum arquivo foi escrito com a URL.

Causa raiz confirmada:
1. `ConfigurationOverrides` em produção está **vazio** — `Taskboard:Rag:Url` nunca foi persistida. O usuário provavelmente digitou a URL no formulário e clicou **Sync**, que provisiona a partir da config **persistida** (`IConfiguration`), ignorando os campos da tela.
2. URL vazia → `McpProvisioningService` entra em **remove mode** por design (SPEC-20260917-rag-mcp-provisioning) — um botão rotulado "Sync" executa remoção. O banner de aviso existe, mas não impede a ação.
3. Vetor secundário: `POST mcp/sync` resolve a config do `IConfiguration` singleton **sem recarregar** o `SqliteConfigurationProvider` — overrides gravados por outro caminho podem ficar stale até restart (o `PUT mcp/rag` recarrega explicitamente; o sync não).

## 2. Scope

**In scope:**
- `POST /api/local/mcp/sync` recusa provisionamento sem URL persistida → `400 { code: "rag-not-configured" }`; remove deixa de ser efeito colateral do Sync.
- Novo `POST /api/local/mcp/remove` — remoção explícita do servidor `knowledge` de todos os targets (mantém a feature de desprovisionar, agora intencional).
- `mcp/sync` e `mcp/remove` chamam `overridesProvider.Reload()` antes de provisionar (elimina stale config).
- Settings UI:
  - **Sync** bloqueado quando não há URL **persistida** (mensagem clara: "Save a URL first — Sync provisions the saved configuration"); quando a URL da tela difere da persistida, hint "unsaved changes — use Save & Sync".
  - Novo botão **Remove** (danger, com confirmação) chamando `mcp/remove` — substitui o efeito de remoção do Sync.
  - **Save & Sync** continua: `PUT mcp/rag` (que já dispara provision).
- Precisão do relatório por target: `Configured` (novo ou já conforme), `Updated` (entrada existia com URL/key diferente e foi reescrita), `Removed`, `NotConfigured`, `Skipped`, `Failed`, `Repaired` — `McpAgentResult`/`McpAgentState` ganham `Updated` (ou equivalente) para não reportar "Configured" num overwrite.
- Pós-provisionamento: o status (`GET mcp/status`) reflete o estado real dos arquivos (re-leitura já existe via `ReadAgentStatus` — garantir que os badges da UI mostram o resultado do run + estado atual).

**Out of scope:**
- Novos targets de CLI (o mapa atual de 12 se mantém).
- Validação de conectividade do endpoint MCP (health-check na URL) — futuro.
- Suporte a múltiplos servidores MCP — continua um só (`knowledge`).

## 3. Technical Context

**Files to read:**
- `src/Taskboard.Server/Program.cs` — endpoints `mcp/status`, `mcp/sync` (1819), `mcp/log`, `mcp/rag` (1830)
- `src/Taskboard.Integrations/Mcp/McpProvisioningService.cs` — `ResolveConfig`, `ProvisionCoreAsync` (modo remove por `Url` vazia), `ProvisionAgent*` por CLI
- `src/Taskboard.Integrations/Mcp/{JsonConfigMerger,TomlConfigMerger,SecureConfigWriter,AgentMcpConfigMap}.cs`
- `src/Taskboard.Application.Contracts/Mcp/{McpAgentResult,McpAgentState,McpProvisionStatus,IMcpProvisioningService}.cs`, `Requests/SaveRagMcpRequest.cs`
- `src/Taskboard.Blazor/Components/Pages/Settings.razor` — `_ragUrl`, `SyncMcpAsync`, `SaveRagAsync`, badges (160-237, 700-792)
- `TaskboardClient.SyncMcpAsync`/`SaveRagMcpAsync` + testes existentes de MCP (`tests/**/Mcp*`)

**Fatos confirmados:**
- `McpProvisioningService` é singleton; `ResolveConfig()` lê `Taskboard:Rag:{ServerName,Url,ApiKey}` do `IConfiguration`.
- `PUT mcp/rag` faz `overridesProvider.Reload()` antes de `RequestProvision()`; `POST mcp/sync` não recarrega.
- Escrita já é atômica + `0600` + `.bak` via `SecureConfigWriter`/mergers — preservar.
- `RequestProvision` é fire-and-forget com gate (`SemaphoreSlim`) — `POST sync` retorna 202 + status.

## 4. Functional Requirements

- **RF-001** `POST /api/local/mcp/sync`: reload do provider de overrides → resolve config → **URL vazia = `400 rag-not-configured`** (sem disparar provision); URL presente = provision normal (202).
- **RF-002** `POST /api/local/mcp/remove`: reload → provision em remove mode explícito (independente da URL) → 202; log registra "remove mode (explicit)".
- **RF-003** `McpAgentState.Updated` reportado quando a entrada existia com valor diferente e foi sobrescrita (mergers distinguem `NoChange`/`Updated`/`Created` no `MergeOutcome` — estender o enum se necessário); badges da UI mapeiam `Updated`.
- **RF-004** UI: Sync desabilitado + tooltip quando `Taskboard:Rag:Url` persistida está vazia; hint "unsaved changes" quando campo ≠ persistido; botão **Remove** (btn-outline-danger + confirmação) chama `mcp/remove`.
- **RF-005** Save & Sync segue `PUT mcp/rag` → reload → provision (comportamento atual, correto); validar URL não-vazia no request (`400` se vazio — hoje `""` limpa a entry, o que combinado com este SPEC equivale a remove → PUT com URL vazia passa a exigir confirmação na UI, apontando para o botão Remove).
- **RF-006** ApiKey nunca aparece em log, status payload ou erro (`Sanitize` existente — manter e cobrir `Updated`).
- **RF-007** Provisionamento idempotente: 2º Sync consecutivo reporta `Configured`/`NoChange` — não `Updated` — nos targets já conformes.

## 5. API Contract

```
POST /api/local/mcp/sync          → 202 McpProvisionStatus | 400 { code: "rag-not-configured" } | 401
POST /api/local/mcp/remove        → 202 McpProvisionStatus (remove mode) | 401
GET  /api/local/mcp/status        → 200 McpProvisionStatus (sem mudança)
PUT  /api/local/mcp/rag           → 204 | 400 { code: "rag-url-required" } quando Url=="" | 401
```

## 6. Acceptance Criteria

- **AC1** Sem URL salva, Sync retorna 400 e **nenhum** arquivo é tocado (nada de remove mode implícito).
- **AC2** Com URL salva, Sync escreve/atualiza a entrada `knowledge` nos arquivos dos CLIs habilitados — log mostra `Configured`/`Updated`, não `NotConfigured`.
- **AC3** Remove explícito remove a entrada de todos os targets e reporta `Removed`/`NotConfigured`.
- **AC4** Override gravado fora do `PUT` (ex.: taskctl/db direto) é lido corretamente pelo Sync graças ao reload.
- **AC5** URL digitada mas não salva → UI sinaliza "unsaved changes" em vez de deixar o Sync rodar contra config antiga.
- **AC6** API key jamais aparece em `mcp/log`, status ou mensagens de erro.

## 7. Task Plan

- T1: `MergeOutcome.Updated` + `McpAgentState.Updated` nos mergers/service; unit tests (new/updated/nochange/remove).
- T2: Endpoints — sync com reload+guard 400; `mcp/remove`; `PUT rag` rejeita URL vazia; integration tests.
- T3: Settings UI — Sync bloqueado sem URL persistida, hint unsaved-changes, botão Remove com confirmação, badge `updated`.
- T4: Testes ponta-a-ponta (save → sync → arquivos reais em temp HOME) + docs en/pt-br + deploy.

## 8. Organization Guardrails

- Remoção só via ação explícita — nunca como efeito colateral de Sync.
- ApiKey sanitizado em todos os caminhos (log/status/erro).
- Escritas atômicas + backup `.bak` + `0600` preservados; diretórios-pai criados quando ausentes.
- Não alterar o mapa de targets (`AgentMcpConfigMap`) neste SPEC.

## 9. Definition of Done

- [ ] Sync com URL salva grava os arquivos dos CLIs (verificado em produção: `~/.codex/config.toml` etc. contêm `knowledge`).
- [ ] Sync sem URL → 400 + UI explica; Remove explícito funciona.
- [ ] Unit + integration verdes; docs atualizados; SPEC → Done; deploy verificado.
