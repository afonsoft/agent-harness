# SPEC-20260929-cli-probe-hardening

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cli-probe-hardening` |
| Type | `Bugfix / Integrations + Blazor` |
| Stack | `.NET 10` |
| Repository | `afonsoft/agent-harness` |
| Branch | `feature/devin-20260929-cli-probe-hardening` |
| Ticket | [#371](https://github.com/afonsoft/agent-harness/issues/371) (Epic [#366](https://github.com/afonsoft/agent-harness/issues/366)) |
| Status | `Done` |
| Related | `SPEC-20260928-agent-cli-probe-background` (PR #351) |

## 1. User Story

**As a** usuário do Harness
**I want** que o catálogo de modelos respeite TTL, que Sync persista o resultado, que o diálogo aguarde sondagens lentas e que falhas de rede no refresh sejam tratadas
**So that** o pipeline não valide modelos contra listas obsoletas e a UI não fique muda ou desatualizada.

**Problem context:**

Achados do Devin Review e SonarCloud no PR #351 (quality gate do Sonar **falhou** — B Security Rating; issues ainda OPEN):

1. 🔴 **Catálogo pode ficar obsoleto indefinidamente** — `AgentModelCatalogService.ListAvailableAsync` serve o snapshot em memória sem verificar TTL; só `GetStatusAsync` aplica expiração. Se o pipeline usa o catálogo sem que ninguém consulte status, a lista nunca expira.
2. 🟡 **Sync manual não persiste** — `SetModels` altera só memória; o snapshot em disco só é gravado ao fim de `RunRefreshAsync`. Restart carrega a lista antiga até a próxima sondagem.
3. 🟡 **Diálogo desiste cedo** — `AgentModelConfigDialog` re-consulta uma vez após ~2,5s, mas `ProbeModelsAsync` permite até ~10s por CLI; modelos que chegam depois nunca aparecem.
4. 🟡 **Falha do POST quebra o refresh** — `Agents.razor` aguarda `RefreshAgentClisAsync` antes de `RefreshAsync`; exceção de rede escapa do handler e a página não atualiza nem avisa.
5. 🔍 **Tooltip obsoleto** — ajuda do Sync ainda promete ignorar um "cache de 5 minutos" removido na mudança.
6. **SonarCloud OPEN:** `S6444` regex sem timeout (`CliProbeSnapshotService.cs:25`, VULNERABILITY — causa do B Security Rating); `S8970` null-forgiving (`:146`); `S6667` catch sem logar exceção (`:161`); `S4487` `_logger` não lido (`AgentCliStatusService.cs:21`, CRITICAL code smell).

## 2. Scope

**In scope:**

- TTL/expiração nas leituras do catálogo (não só no serviço de status).
- Persistência atômica do snapshot quando `SetModels` produz mudança.
- Diálogo de modelos: re-poll até refresh concluir ou deadline > timeout da sonda, com cancelamento no dispose.
- `Agents.razor`: try/catch no POST de refresh + notificação + leitura de status conforme apropriado.
- Tooltip corrigido para descrever o snapshot persistido.
- Correção dos 4 issues SonarCloud listados.
- Testes por correção.

**Out of scope:**

- Mudança do modelo de background refresh/intervalos configuráveis.
- Novos endpoints.

## 3. Technical Context

**Where the change happens:**

- `src/Taskboard.Integrations/Agents/AgentModelCatalogService.cs` — TTL + persistência.
- `src/Taskboard.Integrations/Agents/CliProbeSnapshotService.cs` — persistência atômica, regex timeout, S8970/S6667.
- `src/Taskboard.Integrations/Agents/AgentCliStatusService.cs` — `_logger` não lido.
- `src/Taskboard.Blazor/Components/Pages/AgentModelConfigDialog.razor` — re-poll.
- `src/Taskboard.Blazor/Components/Pages/Agents.razor` — tratamento do POST + tooltip.

## 4. Requirements

### RF-001: TTL no catálogo

- **Description:** `ListAvailableAsync` verifica a idade do snapshot e agenda/aciona refresh quando expirado (mesmo TTL do status service), em vez de servir indefinidamente.

### RF-002: Sync persiste

- **Description:** `SetModels` persiste o snapshot (escrita serializada/atômica junto à gravação do refresh); restart após Sync manual devolve a lista nova.

### RF-003: Diálogo aguarda a sonda

- **Description:** Re-consultar até refresh terminar ou deadline > timeout da sonda; cancelamento ao descartar o componente; opcionalmente consultar estado de refresh.

### RF-004: POST failure não quebra refresh

- **Description:** `RefreshAllAsync` captura exceção do POST, notifica (toast) e executa a leitura de status conforme apropriado.

### RF-005: Tooltip e issues Sonar

- **Description:** Tooltip descreve o snapshot persistido; corrigir `S6444` (timeout no regex), `S8970`, `S6667`, `S4487` (usar ou remover `_logger`).

## 5. API Contract

Sem mudança de API.

## 6. Acceptance Criteria

- [ ] **Given** snapshot expirado **when** o catálogo é consultado **then** refresh é disparado (teste verde).
- [ ] **Given** Sync bem-sucedido + restart simulado **when** o snapshot é relido **then** devolve a lista nova (teste verde).
- [ ] **Given** sonda de ~6s **when** o diálogo abre **then** os modelos aparecem quando a sonda conclui (teste com delays controlados).
- [ ] **Given** POST de refresh falha **when** o botão é clicado **then** a página notifica e ainda lê status.
- [ ] **Given** SonarCloud re-run **then** os 4 issues fecham e o quality gate volta a A.

## 7. Task Plan (agent execution)

- [ ] **T1 — TTL catálogo** + teste.
- [ ] **T2 — Persistência SetModels** + teste.
- [ ] **T3 — Diálogo re-poll** + teste.
- [ ] **T4 — Agents.razor POST catch + tooltip.**
- [ ] **T5 — Sonar issues (4).**
- [ ] **T6 — Done + PR.**

## 8. Organization Guardrails

- Escrita de snapshot atômica (tmp+rename), sem corrupção parcial.
- `.github/workflows/**` intocado.

## 9. Definition of Done

- [ ] Catálogo nunca serve lista expirada silenciosamente; Sync persiste.
- [ ] Diálogo/refresh resilientes; tooltip correto.
- [ ] SonarCloud quality gate A; build/test verde; `Status = Done`.
